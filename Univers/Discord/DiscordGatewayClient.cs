/*
 * =====================================================================
 *  DiscordGatewayClient.cs - Native .NET 9.0 Discord Gateway v10 & REST
 *  Namespace: Univers.Discord (.NET 9.0 / TShock 6.x)
 *  Tanpa library eksternal: menggunakan ClientWebSocket + HttpClient
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TShockAPI;

namespace Univers.Discord
{
    public sealed class DiscordIncomingMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string GuildId { get; set; } = string.Empty;
        public string AuthorId { get; set; } = string.Empty;
        public string AuthorUsername { get; set; } = string.Empty;
        public string AuthorDisplayName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsDirectMessage { get; set; }
        public List<string> RoleIds { get; set; } = new();
    }

    internal sealed class OutboundDiscordPost
    {
        public string ChannelId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public sealed class DiscordGatewayClient : IDisposable
    {
        private const string RestBaseUrl = "https://discord.com/api/v10";
        private const string DefaultGatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";

        // Guilds (1) | GuildMessages (512) | DirectMessages (4096) | MessageContent (32768)
        private const int GatewayIntents = (1 << 0) | (1 << 9) | (1 << 12) | (1 << 15);

        private readonly HttpClient _http;
        private readonly string _botToken;
        private readonly Action<DiscordIncomingMessage> _onMessage;
        private readonly Action<string> _onReady;

        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<OutboundDiscordPost> _outboundQueue = new();
        private readonly SemaphoreSlim _sendWsLock = new(1, 1);

        private ClientWebSocket? _ws;
        private string? _sessionId;
        private string? _resumeGatewayUrl;
        private long? _lastSequence;
        private bool _heartbeatAcked = true;
        private bool _fatalAuthError;

        public bool IsConnected { get; private set; }
        public string BotUserId { get; private set; } = string.Empty;
        public string BotUsername { get; private set; } = string.Empty;

        public DiscordGatewayClient(
            string botToken,
            Action<DiscordIncomingMessage> onMessage,
            Action<string> onReady)
        {
            _botToken = botToken;
            _onMessage = onMessage;
            _onReady = onReady;

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (UniversMMORPG, 2.0)");
        }

        public void Start()
        {
            _ = Task.Run(() => GatewaySupervisorLoopAsync(_cts.Token));
            _ = Task.Run(() => OutboundPumpLoopAsync(_cts.Token));
        }

        public void EnqueueRelayMessage(string channelId, string content)
        {
            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(content) || _cts.IsCancellationRequested)
            {
                return;
            }

            if (_outboundQueue.Count > 250)
            {
                _outboundQueue.TryDequeue(out _);
            }

            _outboundQueue.Enqueue(new OutboundDiscordPost
            {
                ChannelId = channelId,
                Content = content
            });
        }

        private async Task OutboundPumpLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_outboundQueue.TryDequeue(out var first))
                    {
                        var sb = new StringBuilder(first.Content);
                        string targetChannel = first.ChannelId;

                        // Gabungkan pesan beruntun ke channel yang sama agar hemat rate-limit
                        int merged = 1;
                        while (merged < 6 &&
                               _outboundQueue.TryPeek(out var next) &&
                               next.ChannelId == targetChannel &&
                               sb.Length + next.Content.Length + 2 <= 1850)
                        {
                            _outboundQueue.TryDequeue(out _);
                            sb.Append('\n').Append(next.Content);
                            merged++;
                        }

                        await SendMessageAsync(targetChannel, sb.ToString(), suppressMentions: true, ct);
                        await Task.Delay(350, ct);
                    }
                    else
                    {
                        await Task.Delay(150, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(500, ct); } catch { break; }
                }
            }
        }

        private async Task GatewaySupervisorLoopAsync(CancellationToken ct)
        {
            int backoffSec = 3;

            while (!ct.IsCancellationRequested && !_fatalAuthError)
            {
                try
                {
                    string wsUrl = !string.IsNullOrWhiteSpace(_resumeGatewayUrl)
                        ? AppendGatewayQuery(_resumeGatewayUrl!)
                        : await ResolveGatewayUrlAsync(ct);

                    await RunSingleGatewaySessionAsync(wsUrl, ct);
                    backoffSec = 3;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    IsConnected = false;
                    if (!_fatalAuthError && !ct.IsCancellationRequested)
                    {
                        TShock.Log.ConsoleWarn($"[Univers.Discord] Koneksi Gateway terputus ({ex.Message}). Menyambung ulang dalam {backoffSec}d...");
                    }
                }

                IsConnected = false;
                if (_fatalAuthError || ct.IsCancellationRequested) break;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(backoffSec), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoffSec = Math.Min(30, backoffSec * 2);
            }
        }

        private async Task<string> ResolveGatewayUrlAsync(CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync($"{RestBaseUrl}/gateway/bot", ct);
                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync(ct);
                    var jo = JObject.Parse(body);
                    string? url = jo["url"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return AppendGatewayQuery(url);
                    }
                }
                else if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _fatalAuthError = true;
                    TShock.Log.ConsoleError("[Univers.Discord] BotToken tidak valid (401 Unauthorized)! Periksa DiscordConfig.json.");
                }
            }
            catch { }

            return DefaultGatewayUrl;
        }

        private static string AppendGatewayQuery(string baseUrl)
        {
            return baseUrl.Contains('?') ? baseUrl : $"{baseUrl.TrimEnd('/')}/?v=10&encoding=json";
        }

        private async Task RunSingleGatewaySessionAsync(string wsUrl, CancellationToken outerCt)
        {
            using var ws = new ClientWebSocket();
            _ws = ws;

            await ws.ConnectAsync(new Uri(wsUrl), outerCt);

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            CancellationToken sct = sessionCts.Token;
            Task? heartbeatTask = null;

            try
            {
                while (ws.State == WebSocketState.Open && !sct.IsCancellationRequested)
                {
                    string? rawJson = await ReceiveFullTextMessageAsync(ws, sct);
                    if (rawJson == null)
                    {
                        break;
                    }

                    JObject payload;
                    try
                    {
                        payload = JObject.Parse(rawJson);
                    }
                    catch
                    {
                        continue;
                    }

                    int op = payload["op"]?.Value<int>() ?? -1;
                    if (payload["s"] != null && payload["s"]!.Type != JTokenType.Null)
                    {
                        _lastSequence = payload["s"]!.Value<long>();
                    }

                    switch (op)
                    {
                        case 10: // Hello
                            int intervalMs = payload["d"]?["heartbeat_interval"]?.Value<int>() ?? 41250;
                            _heartbeatAcked = true;
                            heartbeatTask ??= Task.Run(() => HeartbeatLoopAsync(ws, intervalMs, sct), sct);

                            if (!string.IsNullOrWhiteSpace(_sessionId) && _lastSequence.HasValue)
                            {
                                await SendResumeAsync(ws, sct);
                            }
                            else
                            {
                                await SendIdentifyAsync(ws, sct);
                            }
                            break;

                        case 11: // Heartbeat ACK
                            _heartbeatAcked = true;
                            break;

                        case 1: // Heartbeat requested
                            await SendHeartbeatAsync(ws, sct);
                            break;

                        case 7: // Reconnect requested
                            sessionCts.Cancel();
                            break;

                        case 9: // Invalid Session
                            bool resumable = payload["d"]?.Value<bool>() ?? false;
                            if (!resumable)
                            {
                                _sessionId = null;
                                _resumeGatewayUrl = null;
                                _lastSequence = null;
                            }
                            await Task.Delay(2500, sct);
                            sessionCts.Cancel();
                            break;

                        case 0: // Dispatch
                            string eventName = payload["t"]?.ToString() ?? string.Empty;
                            var data = payload["d"] as JObject;
                            if (data != null)
                            {
                                HandleDispatchEvent(eventName, data);
                            }
                            break;
                    }
                }
            }
            finally
            {
                IsConnected = false;
                sessionCts.Cancel();

                if (ws.CloseStatus.HasValue)
                {
                    int closeCode = (int)ws.CloseStatus.Value;
                    if (closeCode == 4004)
                    {
                        _fatalAuthError = true;
                        TShock.Log.ConsoleError("[Univers.Discord] Gateway Close 4004: BotToken ditolak Discord! Periksa token di DiscordConfig.json.");
                    }
                    else if (closeCode == 4014)
                    {
                        _fatalAuthError = true;
                        TShock.Log.ConsoleError("[Univers.Discord] Gateway Close 4014: Disallowed Intents! Aktifkan 'MESSAGE CONTENT INTENT' di Discord Developer Portal -> Bot.");
                    }
                    else if (closeCode == 4007 || closeCode == 4009)
                    {
                        _sessionId = null;
                        _resumeGatewayUrl = null;
                        _lastSequence = null;
                    }
                }

                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                }
                catch { }
                _ws = null;
            }
        }

        private void HandleDispatchEvent(string eventName, JObject d)
        {
            if (eventName == "READY")
            {
                _sessionId = d["session_id"]?.ToString();
                _resumeGatewayUrl = d["resume_gateway_url"]?.ToString();
                BotUserId = d["user"]?["id"]?.ToString() ?? string.Empty;
                BotUsername = d["user"]?["username"]?.ToString() ?? "DiscordBot";
                IsConnected = true;
                _onReady(BotUsername);
                return;
            }

            if (eventName == "RESUMED")
            {
                IsConnected = true;
                return;
            }

            if (eventName == "MESSAGE_CREATE")
            {
                var author = d["author"] as JObject;
                if (author == null) return;
                if (author["bot"]?.Value<bool>() == true) return;
                if (d["webhook_id"] != null && d["webhook_id"]!.Type != JTokenType.Null) return;

                string authorId = author["id"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(authorId) || authorId == BotUserId) return;

                string username = author["username"]?.ToString() ?? "user";
                string globalName = author["global_name"]?.ToString() ?? string.Empty;
                string memberNick = d["member"]?["nick"]?.ToString() ?? string.Empty;
                string displayName = !string.IsNullOrWhiteSpace(memberNick)
                    ? memberNick
                    : (!string.IsNullOrWhiteSpace(globalName) ? globalName : username);

                var roles = new List<string>();
                if (d["member"]?["roles"] is JArray roleArr)
                {
                    foreach (var r in roleArr)
                    {
                        string? rid = r?.ToString();
                        if (!string.IsNullOrWhiteSpace(rid)) roles.Add(rid);
                    }
                }

                string guildId = d["guild_id"]?.ToString() ?? string.Empty;

                var msg = new DiscordIncomingMessage
                {
                    MessageId = d["id"]?.ToString() ?? string.Empty,
                    ChannelId = d["channel_id"]?.ToString() ?? string.Empty,
                    GuildId = guildId,
                    AuthorId = authorId,
                    AuthorUsername = username,
                    AuthorDisplayName = displayName,
                    Content = d["content"]?.ToString() ?? string.Empty,
                    IsDirectMessage = string.IsNullOrWhiteSpace(guildId),
                    RoleIds = roles
                };

                _onMessage(msg);
            }
        }

        private async Task HeartbeatLoopAsync(ClientWebSocket ws, int intervalMs, CancellationToken ct)
        {
            int safeInterval = Math.Max(5000, intervalMs);
            try
            {
                await Task.Delay((int)(safeInterval * 0.4), ct);
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    if (!_heartbeatAcked)
                    {
                        TShock.Log.ConsoleWarn("[Univers.Discord] Heartbeat ACK tidak diterima; mereset sesi Gateway...");
                        try { ws.Abort(); } catch { }
                        break;
                    }

                    _heartbeatAcked = false;
                    await SendHeartbeatAsync(ws, ct);
                    await Task.Delay(safeInterval, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private Task SendHeartbeatAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["op"] = 1,
                ["d"] = _lastSequence.HasValue ? JToken.FromObject(_lastSequence.Value) : JValue.CreateNull()
            };
            return SendWsJsonAsync(ws, payload, ct);
        }

        private Task SendIdentifyAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["op"] = 2,
                ["d"] = new JObject
                {
                    ["token"] = _botToken,
                    ["intents"] = GatewayIntents,
                    ["properties"] = new JObject
                    {
                        ["os"] = "linux",
                        ["browser"] = "UniversCore",
                        ["device"] = "UniversCore"
                    },
                    ["presence"] = new JObject
                    {
                        ["status"] = "online",
                        ["afk"] = false,
                        ["activities"] = new JArray
                        {
                            new JObject
                            {
                                ["name"] = "Univers MMORPG",
                                ["type"] = 0
                            }
                        }
                    }
                }
            };
            return SendWsJsonAsync(ws, payload, ct);
        }

        private Task SendResumeAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["op"] = 6,
                ["d"] = new JObject
                {
                    ["token"] = _botToken,
                    ["session_id"] = _sessionId,
                    ["seq"] = _lastSequence ?? 0
                }
            };
            return SendWsJsonAsync(ws, payload, ct);
        }

        private async Task SendWsJsonAsync(ClientWebSocket ws, JObject payload, CancellationToken ct)
        {
            if (ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            await _sendWsLock.WaitAsync(ct);
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                }
            }
            finally
            {
                _sendWsLock.Release();
            }
        }

        private static async Task<string?> ReceiveFullTextMessageAsync(ClientWebSocket ws, CancellationToken ct)
        {
            byte[] buffer = new byte[8192];
            using var ms = new MemoryStream();

            while (!ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.Count > 0)
                {
                    ms.Write(buffer, 0, result.Count);
                }

                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }

            return null;
        }

        #region Discord REST Helpers

        public async Task<string?> SendMessageAsync(string channelId, string content, bool suppressMentions = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(content)) return null;

            string safeText = content.Length > 1950 ? content.Substring(0, 1950) : content;
            var body = new JObject
            {
                ["content"] = safeText
            };

            if (suppressMentions)
            {
                body["allowed_mentions"] = new JObject
                {
                    ["parse"] = new JArray()
                };
            }

            try
            {
                using var reqContent = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync($"{RestBaseUrl}/channels/{channelId}/messages", reqContent, ct);
                if ((int)resp.StatusCode == 429)
                {
                    await HandleRateLimitAsync(resp, ct);
                    using var retryContent = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using var retryResp = await _http.PostAsync($"{RestBaseUrl}/channels/{channelId}/messages", retryContent, ct);
                    if (retryResp.IsSuccessStatusCode)
                    {
                        var jo = JObject.Parse(await retryResp.Content.ReadAsStringAsync(ct));
                        return jo["id"]?.ToString();
                    }
                    return null;
                }

                if (resp.IsSuccessStatusCode)
                {
                    var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
                    return jo["id"]?.ToString();
                }
            }
            catch { }

            return null;
        }

        public async Task<bool> SendDmAsync(string userId, string content)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(content)) return false;

            try
            {
                var dmBody = new JObject { ["recipient_id"] = userId };
                using var dmReq = new StringContent(dmBody.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using var dmResp = await _http.PostAsync($"{RestBaseUrl}/users/@me/channels", dmReq);
                if (!dmResp.IsSuccessStatusCode) return false;

                var dmJo = JObject.Parse(await dmResp.Content.ReadAsStringAsync());
                string? dmChannelId = dmJo["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(dmChannelId)) return false;

                string? sentId = await SendMessageAsync(dmChannelId, content, suppressMentions: true);
                return !string.IsNullOrWhiteSpace(sentId);
            }
            catch
            {
                return false;
            }
        }

        public async Task DeleteMessageAsync(string channelId, string messageId)
        {
            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(messageId)) return;

            try
            {
                using var resp = await _http.DeleteAsync($"{RestBaseUrl}/channels/{channelId}/messages/{messageId}");
            }
            catch { }
        }

        // Ambil SEMUA ID member yang punya salah satu role di protectRoleIds (untuk sweep aman role-based).
        public async Task<List<string>> GetMemberIdsByRolesAsync(string guildId, IEnumerable<string> roleIds, CancellationToken ct = default)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(guildId) || roleIds == null) return result;

            var wanted = new HashSet<string>(roleIds.Where(r => !string.IsNullOrWhiteSpace(r)));
            if (wanted.Count == 0) return result;

            string? after = null;
            while (!ct.IsCancellationRequested)
            {
                string url = $"{RestBaseUrl}/guilds/{guildId}/members?limit=1000";
                if (!string.IsNullOrWhiteSpace(after)) url += $"&after={after}";

                JArray page;
                using (var resp = await _http.GetAsync(url, ct))
                {
                    if (!resp.IsSuccessStatusCode) break;
                    page = JArray.Parse(await resp.Content.ReadAsStringAsync(ct));
                }
                if (page == null || page.Count == 0) break;

                foreach (var m in page)
                {
                    string? uid = m["user"]?["id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(uid)) continue;
                    if (m["roles"] is JArray roles && roles.Any(r => wanted.Contains(r?.ToString() ?? string.Empty)))
                    {
                        result.Add(uid);
                    }
                }

                after = page[page.Count - 1]?["user"]?["id"]?.ToString();
                if (page.Count < 1000) break;
            }

            return result;
        }

        // Ambil ID pemilik server (guild owner) supaya chat-nya tidak ikut terhapus saat sweep.
        public async Task<string?> GetGuildOwnerIdAsync(string guildId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(guildId)) return null;
            try
            {
                using var resp = await _http.GetAsync($"{RestBaseUrl}/guilds/{guildId}", ct);
                if (resp.IsSuccessStatusCode)
                {
                    var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
                    return jo["owner_id"]?.ToString();
                }
            }
            catch { }
            return null;
        }

        // Hapus SELURUH pesan di channel (kecuali bot, guild owner, ProtectUserIds, & member ber-ProtectRoleIds).
        // Pakai bulk-delete (maks 100 pesan / batch, pesan >14 hari di-skip Discord). Return jumlah terhapus.
        public async Task<int> PurgeChannelAsync(string channelId, string guildId, IEnumerable<string> protectUserIds, IEnumerable<string>? protectRoleIds = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channelId)) return 0;

            var protectedSet = new HashSet<string>(protectUserIds ?? Enumerable.Empty<string>());
            if (!string.IsNullOrWhiteSpace(BotUserId)) protectedSet.Add(BotUserId);
            if (!string.IsNullOrWhiteSpace(guildId))
            {
                string? ownerId = await GetGuildOwnerIdAsync(guildId, ct);
                if (!string.IsNullOrWhiteSpace(ownerId)) protectedSet.Add(ownerId!);

                // Tambahkan semua member yang punya role owner/admin (role-based, buat partner).
                foreach (var uid in await GetMemberIdsByRolesAsync(guildId, protectRoleIds ?? Enumerable.Empty<string>(), ct))
                {
                    protectedSet.Add(uid);
                }
            }

            int totalDeleted = 0;
            string? before = null;

            while (!ct.IsCancellationRequested)
            {
                string url = $"{RestBaseUrl}/channels/{channelId}/messages?limit=100";
                if (!string.IsNullOrWhiteSpace(before)) url += $"&before={before}";

                JArray batch;
                using (var resp = await _http.GetAsync(url, ct))
                {
                    if (!resp.IsSuccessStatusCode) break;
                    batch = JArray.Parse(await resp.Content.ReadAsStringAsync(ct));
                }
                if (batch == null || batch.Count == 0) break;

                before = batch[batch.Count - 1]?["id"]?.ToString();

                var toDelete = batch
                    .Where(m => !protectedSet.Contains(m["author"]?["id"]?.ToString() ?? string.Empty))
                    .Select(m => m["id"]?.ToString())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToList();

                if (toDelete.Count == 0) continue;

                // bulk-delete per maks 100 id
                var body = new JObject { ["messages"] = new JArray(toDelete.Cast<object>().ToArray()) };
                using (var req = new HttpRequestMessage(HttpMethod.Post, $"{RestBaseUrl}/channels/{channelId}/messages/bulk-delete")
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
                })
                {
                    using var resp = await _http.SendAsync(req, ct);
                    if (resp.StatusCode == (HttpStatusCode)429) { await HandleRateLimitAsync(resp, ct); }
                    totalDeleted += toDelete.Count;
                }

                await Task.Delay(600, ct);
                if (batch.Count < 100) break;
            }

            return totalDeleted;
        }

        // Ambil semua role di guild (id -> name) untuk memetakan role yg sudah ada.
        public async Task<List<(string Id, string Name)>> GetGuildRolesAsync(string guildId, CancellationToken ct = default)
        {
            var list = new List<(string, string)>();
            if (string.IsNullOrWhiteSpace(guildId)) return list;
            try
            {
                using var resp = await _http.GetAsync($"{RestBaseUrl}/guilds/{guildId}/roles", ct);
                if (resp.IsSuccessStatusCode)
                {
                    var arr = JArray.Parse(await resp.Content.ReadAsStringAsync(ct));
                    foreach (var r in arr)
                    {
                        string? id = r["id"]?.ToString();
                        string? name = r["name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(id) && name != null) list.Add((id!, name));
                    }
                }
            }
            catch { }
            return list;
        }

        // Buat role baru. colorHex = 6 char hex (mis. "f97316"). Return role ID baru atau null.
        public async Task<string?> CreateGuildRoleAsync(string guildId, string name, string colorHex = "99aab5", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(name)) return null;

            int color = 0x99AAB5;
            if (!string.IsNullOrWhiteSpace(colorHex))
            {
                try { color = Convert.ToInt32(colorHex.TrimStart('#'), 16); } catch { }
            }

            var body = new JObject
            {
                ["name"] = name,
                ["color"] = color,
                ["mentionable"] = true,
                ["hoist"] = false
            };
            try
            {
                using var reqContent = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync($"{RestBaseUrl}/guilds/{guildId}/roles", reqContent, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
                    return jo["id"]?.ToString();
                }
            }
            catch { }
            return null;
        }

        // Ambil role ID milik member (untuk hindari hapus role yg bukan milik sistem).
        public async Task<List<string>?> GetMemberRoleIdsAsync(string guildId, string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(userId)) return null;
            try
            {
                using var resp = await _http.GetAsync($"{RestBaseUrl}/guilds/{guildId}/members/{userId}", ct);
                if (resp.IsSuccessStatusCode)
                {
                    var jo = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
                    var roles = new List<string>();
                    if (jo["roles"] is JArray arr)
                        foreach (var r in arr) { var s = r?.ToString(); if (!string.IsNullOrWhiteSpace(s)) roles.Add(s!); }
                    return roles;
                }
            }
            catch { }
            return null;
        }

        public async Task<bool> AddGuildMemberRoleAsync(string guildId, string userId, string roleId)
        {
            if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(roleId))
            {
                return false;
            }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{RestBaseUrl}/guilds/{guildId}/members/{userId}/roles/{roleId}");
                using var resp = await _http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RemoveGuildMemberRoleAsync(string guildId, string userId, string roleId)
        {
            if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(roleId))
            {
                return false;
            }

            try
            {
                using var resp = await _http.DeleteAsync($"{RestBaseUrl}/guilds/{guildId}/members/{userId}/roles/{roleId}");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ModifyGuildMemberNickAsync(string guildId, string userId, string nickname)
        {
            if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(nickname))
            {
                return false;
            }

            string safeNick = nickname.Length > 32 ? nickname.Substring(0, 32) : nickname;
            var body = new JObject { ["nick"] = safeNick };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestBaseUrl}/guilds/{guildId}/members/{userId}")
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
                };
                using var resp = await _http.SendAsync(req);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task HandleRateLimitAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            int delayMs = 1500;
            try
            {
                string json = await resp.Content.ReadAsStringAsync(ct);
                var jo = JObject.Parse(json);
                double? sec = jo["retry_after"]?.Value<double>();
                if (sec.HasValue && sec.Value > 0)
                {
                    delayMs = Math.Clamp((int)Math.Ceiling(sec.Value * 1000.0) + 150, 300, 10000);
                }
            }
            catch { }

            await Task.Delay(delayMs, ct);
        }

        #endregion

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _ws?.Abort(); } catch { }
            try { _http.Dispose(); } catch { }
            try { _sendWsLock.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }
        }
    }
}
