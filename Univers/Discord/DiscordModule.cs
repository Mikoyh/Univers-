/*
 * =====================================================================
 *  DiscordModule.cs - Official Discord Chat Relay & TShock Authenticator
 *  Namespace: Univers.Discord (.NET 9.0 / TShock 6.x)
 *  Fitur:
 *   1. Chat Relay 2-Arah MultiWorld (Lobby + Survival <-> Discord)
 *   2. Wajib Verifikasi Akun TShock untuk bisa chat di Discord Relay
 *   3. Dual-Flow Authenticator:
 *      - In-Game (/verify) -> Discord (!verify <KODE>)
 *      - Discord (!link <AkunTShock>) -> In-Game (/discord confirm <KODE>)
 *   4. Auto-assign Role Verified + Opsional Sync Nickname Discord
 *   5. Command Discord (!online, !who, !verify, !link, !unlink, !cmd)
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Skynomi.Ranks;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using Univers.Exp;
using Univers.Aetherial;
using Univers.MultiWorld;

namespace Univers.Discord
{
    internal sealed class PendingInGameVerifyCode
    {
        public string Code { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public DateTime ExpiresUtc { get; set; }
    }

    internal sealed class PendingDiscordLinkRequest
    {
        public string Code { get; set; } = string.Empty;
        public string DiscordUserId { get; set; } = string.Empty;
        public string DiscordUsername { get; set; } = string.Empty;
        public string DiscordDisplayName { get; set; } = string.Empty;
        public string TargetAccountName { get; set; } = string.Empty;
        public DateTime ExpiresUtc { get; set; }
    }

    public static class DiscordModule
    {
        public static DiscordConfig Config { get; private set; } = new();
        public static DiscordDatabase? Database { get; private set; }

        private static DiscordGatewayClient? _gateway;
        private static string _configPath = string.Empty;
        private static bool _initialized;

        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly ConcurrentDictionary<string, PendingInGameVerifyCode> _pendingInGameCodes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, PendingDiscordLinkRequest> _pendingDiscordRequests = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _lastUnverifiedWarnUtc = new();
        private static readonly ConcurrentDictionary<string, DateTime> _lastDiscordChatUtc = new();
        private static DateTime _lastRoleSyncUtc = DateTime.MinValue;

        // Peta nama tampilan + warna untuk 7 elemen.
        private static readonly (string Id, string Name, string Hex)[] ElementDefs =
        {
            ("IGNIS", "Ignis", "f97316"),
            ("GLACIAL", "Glacial", "38bdf8"),
            ("TORRENT", "Torrent", "06b6d4"),
            ("VOLT", "Volt", "a855f7"),
            ("RADIANCE", "Radiance", "facc15"),
            ("BRAMBLE", "Bramble", "10b981"),
            ("OMNIA", "Omnia", "ef4444"),
        };

        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static bool IsGatewayConnected => _gateway != null && _gateway.IsConnected;
        public static string BotUsername => _gateway?.BotUsername ?? "-";

        public static void Initialize(string savePath)
        {
            if (_initialized) return;

            string localUniversDir = Path.Combine(savePath, "Univers");
            string masterUniversDir = ResolveMasterUniversDir(localUniversDir);
            Directory.CreateDirectory(masterUniversDir);

            _configPath = Path.Combine(masterUniversDir, "DiscordConfig.json");
            Config = DiscordConfig.Load(_configPath);

            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.NpcKilled.Register(UniversPlugin.Instance, OnNpcKilled);

            if (!MultiWorldModule.IsChildProcess)
            {
                Database = new DiscordDatabase(Path.Combine(masterUniversDir, "univers_discord.db"));
                StartGatewayIfConfigured();
            }
            else
            {
                TShock.Log.ConsoleInfo("[Univers.Discord] Child World mode aktif: /verify & event relay tersambung ke Master Hub via IPC.");
            }

            DiscordCommands.Register();
            _initialized = true;
        }

        public static void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config = DiscordConfig.Load(_configPath);
            }

            if (!MultiWorldModule.IsChildProcess)
            {
                RestartGateway();
            }
            TShock.Log.ConsoleInfo("[Univers.Discord] Konfigurasi DiscordConfig.json dimuat ulang.");
        }

        public static void EnqueueMainThread(Action action)
        {
            if (action == null || !_initialized) return;
            _mainThreadQueue.Enqueue(action);
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            try { DiscordCommands.Unregister(); } catch { }
            try { ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate); } catch { }
            try { ServerApi.Hooks.NpcKilled.Deregister(UniversPlugin.Instance, OnNpcKilled); } catch { }

            StopGateway();

            _pendingInGameCodes.Clear();
            _pendingDiscordRequests.Clear();
            _lastUnverifiedWarnUtc.Clear();
            _lastDiscordChatUtc.Clear();
            while (_mainThreadQueue.TryDequeue(out _)) { }

            Database?.Dispose();
            Database = null;
        }

        private static void StartGatewayIfConfigured()
        {
            StopGateway();

            if (!Config.Enabled)
            {
                TShock.Log.ConsoleInfo("[Univers.Discord] Modul Discord dinonaktifkan di DiscordConfig.json.");
                return;
            }

            if (!Config.HasValidBotCredentials())
            {
                TShock.Log.ConsoleInfo("[Univers.Discord] BotToken belum diisi di DiscordConfig.json (Gateway standby menunggu token).");
                return;
            }

            _gateway = new DiscordGatewayClient(
                Config.BotToken.Trim(),
                msg => _ = Task.Run(() => HandleIncomingDiscordMessageAsync(msg)),
                botName =>
                {
                    TShock.Log.ConsoleInfo($"[Univers.Discord] Bot '@{botName}' terhubung ke Discord! Relay={(string.IsNullOrWhiteSpace(Config.RelayChannelId) ? "UNSET" : Config.RelayChannelId)} | AuthGate={(Config.RequireVerifiedAccountToChat ? "STRICT" : "OPEN")}");
                    if (!string.IsNullOrWhiteSpace(Config.RelayChannelId))
                    {
                        _gateway?.EnqueueRelayMessage(Config.RelayChannelId, $"**[SERVER]** Univers MMORPG Bridge & Authenticator aktif (`{MultiWorldModule.CurrentWorldId}`).");
                    }
                    _ = PurgeVerifyChannelIfEnabledAsync();
                    _ = EnsureRoleMapsAsync();
                });

            _gateway.Start();
        }

        private static void StopGateway()
        {
            if (_gateway != null)
            {
                try { _gateway.Dispose(); } catch { }
                _gateway = null;
            }
        }

        private static void RestartGateway()
        {
            StartGatewayIfConfigured();
        }

        // Bersihkan seluruh chat lama di #verify saat bot nyala. Skip pesan bot, guild owner, & ProtectUserIds.
        private static async Task PurgeVerifyChannelIfEnabledAsync()
        {
            if (_gateway == null || !Config.Enabled || !Config.PurgeVerifyChannelOnStartup) return;
            if (string.IsNullOrWhiteSpace(Config.VerifyChannelId)) return;

            try
            {
                int deleted = await _gateway.PurgeChannelAsync(
                    Config.VerifyChannelId,
                    Config.GuildId,
                    Config.ProtectUserIds ?? new List<string>(),
                    Config.ProtectRoleIds ?? new List<string>());

                TShock.Log.ConsoleInfo($"[Univers.Discord] Bersih-bersih #verify selesai: {deleted} pesan dihapus (chat owner & admin dilewati).");

                if (deleted > 0)
                {
                    await _gateway.SendMessageAsync(
                        Config.VerifyChannelId,
                        $"Channel dibersihkan ({deleted} pesan lama dihapus). Ketik `/verify` di game, lalu kirim `{Config.CommandPrefix}verify <KODE>`.",
                        suppressMentions: true);
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Discord] Gagal bersih-bersih #verify: {ex.Message}");
            }
        }

        private static string ResolveMasterUniversDir(string localUniversDir)
        {
            string candidateRoot = Path.Combine(AppContext.BaseDirectory, "tshock", "Univers");
            if (MultiWorldModule.IsChildProcess && Directory.Exists(candidateRoot))
            {
                return candidateRoot;
            }
            return localUniversDir;
        }

        private static void OnGameUpdate(EventArgs args)
        {
            int count = 0;
            while (count < 24 && _mainThreadQueue.TryDequeue(out var act))
            {
                count++;
                try { act(); } catch { }
            }

            // Real-time role sync ke Discord (polling berkala).
            if (Config.RoleSyncPollSeconds > 0 && (Config.SyncRankRoles || Config.SyncElementRoles))
            {
                if ((DateTime.UtcNow - _lastRoleSyncUtc).TotalSeconds >= Config.RoleSyncPollSeconds)
                {
                    _lastRoleSyncUtc = DateTime.UtcNow;
                    _ = Task.Run(SyncAllOnlineRolesAsync);
                }
            }
        }

        #region In-Game -> Discord Event Hooks

        public static void NotifyInGameChat(string worldId, string senderName, string formattedChat)
        {
            if (!_initialized || !Config.Enabled || !Config.RelayInGameChatToDiscord) return;
            if (MultiWorldModule.IsChildProcess) return;
            if (_gateway == null || !_gateway.IsConnected || string.IsNullOrWhiteSpace(Config.RelayChannelId)) return;

            string clean = CleanTerrariaTextForDiscord(formattedChat);
            if (string.IsNullOrWhiteSpace(clean)) return;

            string worldTag = string.IsNullOrWhiteSpace(worldId) ? "SERVER" : worldId.Trim().ToUpperInvariant();
            if (!clean.StartsWith($"[{worldTag}]", StringComparison.OrdinalIgnoreCase))
            {
                clean = $"**[{worldTag}]** {clean}";
            }
            else
            {
                clean = $"**[{worldTag}]**" + clean.Substring(worldTag.Length + 2);
            }

            _gateway.EnqueueRelayMessage(Config.RelayChannelId, clean);
        }

        public static void NotifyPresenceEvent(string eventKind, string worldId, string playerName)
        {
            if (!_initialized || !Config.Enabled || !Config.RelayJoinLeaveToDiscord) return;
            if (MultiWorldModule.IsChildProcess) return;
            if (_gateway == null || !_gateway.IsConnected || string.IsNullOrWhiteSpace(Config.RelayChannelId)) return;
            if (string.IsNullOrWhiteSpace(playerName)) return;

            string worldTag = string.IsNullOrWhiteSpace(worldId) ? "SERVER" : worldId.Trim().ToUpperInvariant();
            string safeName = SanitizeDiscordMarkdown(playerName);

            string line = eventKind switch
            {
                "server_join" => $"**[+]** `{safeName}` bergabung ke server (**{worldTag}**).",
                "server_leave" => $"**[-]** `{safeName}` keluar dari server (**{worldTag}**).",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(line))
            {
                _gateway.EnqueueRelayMessage(Config.RelayChannelId, line);
            }
        }

        public static void NotifyExternalEventFromChild(string worldId, string message)
        {
            if (!_initialized || !Config.Enabled) return;
            if (MultiWorldModule.IsChildProcess) return;
            if (_gateway == null || !_gateway.IsConnected || string.IsNullOrWhiteSpace(Config.RelayChannelId)) return;

            string clean = CleanTerrariaTextForDiscord(message);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                _gateway.EnqueueRelayMessage(Config.RelayChannelId, clean);
            }
        }

        private static void OnNpcKilled(NpcKilledEventArgs e)
        {
            if (!_initialized || !Config.Enabled || !Config.RelayBossDefeatsToDiscord) return;
            var npc = e.npc;
            if (npc == null) return;

            if (!BossContributionTracker.IsBossDefeatNpc(npc)) return;
            string key = BossContributionTracker.IdentifyBossKey(npc);
            if (string.IsNullOrEmpty(key)) return;

            string bossName = BossContributionTracker.GetBossDisplayName(key, npc);
            string worldTag = MultiWorldModule.CurrentWorldId.ToUpperInvariant();
            string msg = $"**[BOSS KALAH - {worldTag}]** `{SanitizeDiscordMarkdown(bossName)}` telah ditaklukkan!";

            if (MultiWorldModule.IsChildProcess)
            {
                _ = MultiWorldModule.SendChildEventToDiscordAsync(MultiWorldModule.CurrentWorldId, msg);
            }
            else if (_gateway != null && _gateway.IsConnected && !string.IsNullOrWhiteSpace(Config.RelayChannelId))
            {
                _gateway.EnqueueRelayMessage(Config.RelayChannelId, msg);
            }
        }

        #endregion

        #region Discord -> Server & Authenticator Logic

        private static async Task HandleIncomingDiscordMessageAsync(DiscordIncomingMessage msg)
        {
            if (!_initialized || !Config.Enabled || _gateway == null || Database == null) return;

            string content = (msg.Content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content)) return;

            string prefix = string.IsNullOrWhiteSpace(Config.CommandPrefix) ? "!" : Config.CommandPrefix;
            bool isRelayChannel = !string.IsNullOrWhiteSpace(Config.RelayChannelId) && msg.ChannelId == Config.RelayChannelId;
            bool isVerifyChannel = !string.IsNullOrWhiteSpace(Config.VerifyChannelId) && msg.ChannelId == Config.VerifyChannelId;
            bool allowVerifyHere = isVerifyChannel || msg.IsDirectMessage || (string.IsNullOrWhiteSpace(Config.VerifyChannelId) && isRelayChannel);

            // 1. Tangani Command Discord (!verify, !link, !unlink, !online, !who, !cmd, !help)
            if (content.StartsWith(prefix, StringComparison.Ordinal))
            {
                string cmdLine = content.Substring(prefix.Length).Trim();
                if (string.IsNullOrWhiteSpace(cmdLine)) return;

                var parts = cmdLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLowerInvariant();

                if (cmd == "verify" || cmd == "verif")
                {
                    if (!allowVerifyHere)
                    {
                        await SendTempReplyAndCleanupSourceAsync(msg, $"<@{msg.AuthorId}> Gunakan perintah `!verify <KODE>` di <#{Config.VerifyChannelId}> atau DM bot.");
                        return;
                    }

                    string codeArg = parts.Length >= 2 ? parts[1].Trim() : string.Empty;
                    await HandleDiscordVerifyCodeAsync(msg, codeArg);
                    return;
                }

                if (cmd == "link")
                {
                    if (!allowVerifyHere)
                    {
                        await SendTempReplyAndCleanupSourceAsync(msg, $"<@{msg.AuthorId}> Gunakan `!link <AkunTShock>` di <#{Config.VerifyChannelId}> atau DM bot.");
                        return;
                    }

                    string arg = parts.Length >= 2 ? string.Join(" ", parts.Skip(1)).Trim() : string.Empty;
                    await HandleDiscordLinkCommandAsync(msg, arg);
                    return;
                }

                if (cmd == "unlink")
                {
                    if (!allowVerifyHere && !isRelayChannel) return;
                    await HandleDiscordUnlinkAsync(msg, parts.Skip(1).ToArray());
                    return;
                }

                if (cmd == "who" || cmd == "whois" || cmd == "profile" || cmd == "cek")
                {
                    if (!isRelayChannel && !isVerifyChannel && !msg.IsDirectMessage) return;
                    await HandleDiscordWhoAsync(msg, parts.Skip(1).ToArray());
                    return;
                }

                if (cmd == "online" || cmd == "players" || cmd == "list")
                {
                    if (!isRelayChannel && !isVerifyChannel && !msg.IsDirectMessage) return;
                    await HandleDiscordOnlineAsync(msg);
                    return;
                }

                if (cmd == "help" || cmd == "bantuan")
                {
                    if (!isRelayChannel && !isVerifyChannel && !msg.IsDirectMessage) return;
                    await HandleDiscordHelpAsync(msg);
                    return;
                }

                if (cmd == "cmd" || cmd == "exec")
                {
                    if (!isRelayChannel && !msg.IsDirectMessage) return;
                    await HandleDiscordAdminExecAsync(msg, string.Join(" ", parts.Skip(1)));
                    return;
                }
            }

            // 2. Jika berada di #verify dan user langsung mengetik kode 6 karakter tanpa prefix '!verify'
            if (isVerifyChannel || msg.IsDirectMessage)
            {
                string trimmedUpper = content.Trim().ToUpperInvariant();
                if (trimmedUpper.Length == 6 && _pendingInGameCodes.ContainsKey(trimmedUpper))
                {
                    await HandleDiscordVerifyCodeAsync(msg, trimmedUpper);
                    return;
                }

                if (isVerifyChannel)
                {
                    // Jaga agar #verify bersih dari obrolan acak
                    if (Config.AutoDeleteVerifyMessages)
                    {
                        await _gateway.DeleteMessageAsync(msg.ChannelId, msg.MessageId);
                    }
                    await SendTempWarningAsync(msg.ChannelId,
                        $"<@{msg.AuthorId}> Channel ini khusus verifikasi akun! Login ke server Terraria lalu ketik `/verify`, kemudian kirim `!verify <KODE>` di sini.");
                }
                return;
            }

            // 3. Jika berada di RelayChannel -> Jalankan AUTHENTICATOR GATE!
            if (!isRelayChannel || !Config.RelayDiscordChatToInGame) return;

            var link = Database.GetByDiscordId(msg.AuthorId);
            if (Config.RequireVerifiedAccountToChat && (link == null || !link.IsVerified))
            {
                if (Config.DeleteUnverifiedMessagesInRelay)
                {
                    await _gateway.DeleteMessageAsync(msg.ChannelId, msg.MessageId);
                }

                DateTime now = DateTime.UtcNow;
                if (!_lastUnverifiedWarnUtc.TryGetValue(msg.AuthorId, out DateTime lastWarn) ||
                    (now - lastWarn).TotalSeconds >= 15)
                {
                    _lastUnverifiedWarnUtc[msg.AuthorId] = now;
                    string where = !string.IsNullOrWhiteSpace(Config.VerifyChannelId)
                        ? $"<#{Config.VerifyChannelId}>"
                        : "channel ini atau DM bot";
                    string warnText = $"<@{msg.AuthorId}> **Akses Chat Ditolak!** Akun Discord kamu wajib terverifikasi dengan akun TShock di server.\nCara verifikasi: Login ke Terraria -> ketik `/verify` -> kirim `!verify <KODE>` di {where}.";

                    await SendTempWarningAsync(msg.ChannelId, warnText);
                    _ = _gateway.SendDmAsync(msg.AuthorId, $"[Univers Authenticator] Untuk bisa chat di Relay Discord & menghubungkan akunmu:\n1. Login ke server Terraria\n2. Ketik `/verify`\n3. Kirim `!verify <KODE>` ke {where}.");
                }
                return;
            }

            // Rate-limit chat Discord -> Terraria per user
            DateTime chatNow = DateTime.UtcNow;
            if (Config.DiscordChatCooldownSeconds > 0 &&
                _lastDiscordChatUtc.TryGetValue(msg.AuthorId, out DateTime lastChat) &&
                (chatNow - lastChat).TotalSeconds < Config.DiscordChatCooldownSeconds)
            {
                return;
            }
            _lastDiscordChatUtc[msg.AuthorId] = chatNow;

            string senderDisplay = link != null ? link.AccountName : msg.AuthorDisplayName;
            string rankPart = link != null ? BuildInGameRankTag(link.AccountName) : "[c/94a3b8:Guest]";
            string cleanBody = SanitizeDiscordChatForTerraria(content);
            if (string.IsNullOrWhiteSpace(cleanBody)) return;

            string formattedInGame = string.IsNullOrWhiteSpace(rankPart)
                ? $"[c/5865F2:DISCORD] {senderDisplay}: {cleanBody}"
                : $"[c/5865F2:DISCORD] {rankPart} {senderDisplay}: {cleanBody}";

            MultiWorldModule.BroadcastExternalGlobalChat(formattedInGame, 255, 255, 255);
            TShock.Log.ConsoleInfo($"[Chat:Discord] {senderDisplay}: {cleanBody}");
        }

        private static async Task HandleDiscordVerifyCodeAsync(DiscordIncomingMessage msg, string rawCode)
        {
            if (_gateway == null || Database == null) return;

            if (Config.AutoDeleteVerifyMessages && !msg.IsDirectMessage)
            {
                await _gateway.DeleteMessageAsync(msg.ChannelId, msg.MessageId);
            }

            string code = (rawCode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Format: `!verify <KODE>`. Ketik `/verify` di dalam game Terraria untuk mendapatkan kode.");
                return;
            }

            // Jika user memasukkan 6 char tanpa dash atau dengan prefix UNI-
            if (code.StartsWith("UNI-", StringComparison.OrdinalIgnoreCase))
            {
                code = code.Substring(4);
            }

            if (!_pendingInGameCodes.TryRemove(code, out var pending))
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Kode `{code}` tidak ditemukan atau sudah kedaluwarsa. Ketik `/verify` ulang di game.");
                return;
            }

            if (DateTime.UtcNow > pending.ExpiresUtc)
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Kode `{code}` sudah kedaluwarsa. Ketik `/verify` lagi di game.");
                return;
            }

            // Pastikan akun TShock belum dikaitkan ke Discord lain (jika sudah, wajib unlink dulu)
            var existingAccLink = Database.GetByAccount(pending.AccountName);
            if (existingAccLink != null && existingAccLink.DiscordUserId != msg.AuthorId)
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Akun TShock `{pending.AccountName}` sudah terhubung ke Discord lain! Pemilik akun harus mengetik `/discord unlink` di game terlebih dahulu.");
                return;
            }

            var record = new DiscordLinkRecord
            {
                AccountName = pending.AccountName,
                DiscordUserId = msg.AuthorId,
                DiscordUsername = msg.AuthorUsername,
                LinkedAtUtc = DateTime.UtcNow,
                IsVerified = true
            };
            Database.UpsertLink(record);

            await ApplyDiscordRewardsAndSyncAsync(msg.GuildId, msg.AuthorId, pending.AccountName);

            await SendTempWarningAsync(
                msg.ChannelId,
                $"**[VERIFIED]** <@{msg.AuthorId}> berhasil terhubung dengan akun resmi server **`{pending.AccountName}`**! Sekarang kamu bisa mengirim pesan di channel relay.");

            MultiWorldModule.SendDirectMessageToGlobalPlayer(
                pending.PlayerName,
                $"[ DISCORD ] Verifikasi berhasil! Akun TShock '{pending.AccountName}' sekarang terhubung permanen dengan Discord @{msg.AuthorUsername}.",
                74, 222, 128);
        }

        private static async Task HandleDiscordLinkCommandAsync(DiscordIncomingMessage msg, string arg)
        {
            if (_gateway == null || Database == null) return;

            if (Config.AutoDeleteVerifyMessages && !msg.IsDirectMessage)
            {
                await _gateway.DeleteMessageAsync(msg.ChannelId, msg.MessageId);
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Format: `!verify <KODE>` (jika ambil kode dari game) atau `!link <NamaAkunTShock>` (jika ingin konfirmasi di game).");
                return;
            }

            // Jika arg kebetulan adalah kode 6-char yang ada di pending
            string upper = arg.Trim().ToUpperInvariant();
            if (_pendingInGameCodes.ContainsKey(upper))
            {
                await HandleDiscordVerifyCodeAsync(msg, upper);
                return;
            }

            var account = TShock.UserAccounts.GetUserAccountByName(arg.Trim());
            if (account == null)
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Akun TShock `{SanitizeDiscordMarkdown(arg)}` tidak ditemukan di database server.");
                return;
            }

            var existing = Database.GetByAccount(account.Name);
            if (existing != null && existing.DiscordUserId != msg.AuthorId)
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Akun `{account.Name}` sudah terhubung ke pengguna Discord lain.");
                return;
            }

            // Pastikan player sedang online di Lobby atau Survival agar bisa langsung konfirmasi
            var worlds = await MultiWorldModule.FetchAllWorldSnapshotsAsync();
            bool isOnline = worlds.Any(w => w.OnlinePlayerNames.Any(n => n.Equals(account.Name, StringComparison.OrdinalIgnoreCase)));
            if (!isOnline)
            {
                await SendTempWarningAsync(msg.ChannelId, $"<@{msg.AuthorId}> Pemilik akun `{account.Name}` sedang offline. Login ke server lalu ketik `/verify`, atau login dulu sebelum `!link {account.Name}`.");
                return;
            }

            PurgeExpiredPendingCodes();
            string code = GenerateCode(6);
            _pendingDiscordRequests[code] = new PendingDiscordLinkRequest
            {
                Code = code,
                DiscordUserId = msg.AuthorId,
                DiscordUsername = msg.AuthorUsername,
                DiscordDisplayName = msg.AuthorDisplayName,
                TargetAccountName = account.Name,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(Config.VerifyCodeExpiryMinutes)
            };

            MultiWorldModule.SendDirectMessageToGlobalPlayer(
                account.Name,
                $"[ DISCORD ] Permintaan link dari Discord @{msg.AuthorUsername}! Ketik '/discord confirm {code}' dalam {Config.VerifyCodeExpiryMinutes} menit untuk menyetujui (atau '/discord deny {code}').",
                125, 211, 252);

            await SendTempWarningAsync(
                msg.ChannelId,
                $"<@{msg.AuthorId}> Permintaan verifikasi terkirim ke in-game `{account.Name}`! Masuk ke game dan ketik `/discord confirm {code}` dalam {Config.VerifyCodeExpiryMinutes} menit.");
        }

        private static async Task HandleDiscordUnlinkAsync(DiscordIncomingMessage msg, string[] args)
        {
            if (_gateway == null || Database == null) return;

            bool isDiscordAdmin = IsDiscordAdmin(msg);
            if (args.Length > 0 && isDiscordAdmin)
            {
                string target = string.Join(" ", args).Trim();
                var rec = Database.GetByAccount(target);
                if (rec == null)
                {
                    await _gateway.SendMessageAsync(msg.ChannelId, $"[INFO] Tidak ada tautan untuk `{SanitizeDiscordMarkdown(target)}`.");
                    return;
                }
                Database.UnlinkByAccount(rec.AccountName);
                await RemoveVerifiedRoleSafeAsync(msg.GuildId, rec.DiscordUserId);
                await _gateway.SendMessageAsync(msg.ChannelId, $"[OK] Tautan akun `{rec.AccountName}` <-> <@{rec.DiscordUserId}> dihapus oleh Admin.");
                return;
            }

            if (Database.UnlinkByDiscordId(msg.AuthorId, out string removedAcc))
            {
                await RemoveVerifiedRoleSafeAsync(msg.GuildId, msg.AuthorId);
                await _gateway.SendMessageAsync(msg.ChannelId, $"<@{msg.AuthorId}> Akun Discord kamu sudah dilepas dari akun TShock `{removedAcc}`.");
            }
            else
            {
                await _gateway.SendMessageAsync(msg.ChannelId, $"<@{msg.AuthorId}> Akun Discord kamu belum terhubung ke akun TShock mana pun.");
            }
        }

        private static async Task HandleDiscordWhoAsync(DiscordIncomingMessage msg, string[] args)
        {
            if (_gateway == null || Database == null) return;

            DiscordLinkRecord? rec = null;
            if (args.Length == 0)
            {
                rec = Database.GetByDiscordId(msg.AuthorId);
            }
            else
            {
                string q = args[0].Trim().Trim('<', '@', '!', '>');
                rec = Database.GetByDiscordId(q) ?? Database.GetByAccount(q);
            }

            if (rec == null)
            {
                await _gateway.SendMessageAsync(msg.ChannelId, $"[INFO] Data verifikasi tidak ditemukan.");
                return;
            }

            int level = ExpApi.GetLevel(rec.AccountName);
            string rankName = GetCleanRankName(rec.AccountName);
            await _gateway.SendMessageAsync(
                msg.ChannelId,
                $"**[IDENTITY]** Discord: <@{rec.DiscordUserId}> (`@{SanitizeDiscordMarkdown(rec.DiscordUsername)}`) <-> TShock: **`{rec.AccountName}`** | Rank: `{rankName}` | Lv.{level}");
        }

        private static async Task HandleDiscordOnlineAsync(DiscordIncomingMessage msg)
        {
            if (_gateway == null) return;
            var worlds = await MultiWorldModule.FetchAllWorldSnapshotsAsync();
            int total = worlds.Sum(w => w.PlayerCount);

            if (total == 0)
            {
                await _gateway.SendMessageAsync(msg.ChannelId, "**[SERVER ONLINE]** 0 pemain sedang online di seluruh world.");
                return;
            }

            var lines = new List<string> { $"**[SERVER ONLINE: {total} Pemain]**" };
            foreach (var w in worlds)
            {
                string names = w.OnlinePlayerNames.Count > 0 ? string.Join(", ", w.OnlinePlayerNames) : "-";
                lines.Add($"- **{w.Id.ToUpperInvariant()}** ({w.PlayerCount}): {SanitizeDiscordMarkdown(names)}");
            }
            await _gateway.SendMessageAsync(msg.ChannelId, string.Join("\n", lines));
        }

        private static async Task HandleDiscordHelpAsync(DiscordIncomingMessage msg)
        {
            if (_gateway == null) return;
            string p = Config.CommandPrefix;
            string help =
                $"**[UNIVERS DISCORD AUTHENTICATOR & RELAY]**\n" +
                $"- `{p}verify <KODE>` : Verifikasi kode dari `/verify` in-game agar bisa chat di server\n" +
                $"- `{p}link <AkunTShock>` : Minta verifikasi ke akunmu yang sedang online di game\n" +
                $"- `{p}unlink` : Putuskan tautan akun Discord <-> TShock\n" +
                $"- `{p}online` : Lihat daftar pemain online di Lobby & Survival\n" +
                $"- `{p}who [nama/@user]` : Cek identitas resmi TShock & Rank player";
            await _gateway.SendMessageAsync(msg.ChannelId, help);
        }

        private static async Task HandleDiscordAdminExecAsync(DiscordIncomingMessage msg, string rawCmd)
        {
            if (_gateway == null || Database == null) return;

            if (!Config.AllowDiscordConsoleCommands)
            {
                await _gateway.SendMessageAsync(msg.ChannelId, "[INFO] Eksekusi command server dari Discord dinonaktifkan di DiscordConfig.json (`AllowDiscordConsoleCommands=false`).");
                return;
            }

            if (!IsDiscordAdmin(msg))
            {
                await _gateway.SendMessageAsync(msg.ChannelId, $"<@{msg.AuthorId}> Kamu tidak memiliki Role Admin Discord yang diizinkan.");
                return;
            }

            var link = Database.GetByDiscordId(msg.AuthorId);
            if (link == null || !link.IsVerified)
            {
                await _gateway.SendMessageAsync(msg.ChannelId, $"<@{msg.AuthorId}> Admin Discord tetap wajib terverifikasi ke akun TShock admin untuk menjalankan command.");
                return;
            }

            var account = TShock.UserAccounts.GetUserAccountByName(link.AccountName);
            var group = account != null ? TShock.Groups.GetGroupByName(account.Group) : null;
            if (group == null || !group.HasPermission("univers.discord.console"))
            {
                await _gateway.SendMessageAsync(msg.ChannelId, $"<@{msg.AuthorId}> Akun TShock `{link.AccountName}` tidak memiliki permission `univers.discord.console`.");
                return;
            }

            if (string.IsNullOrWhiteSpace(rawCmd))
            {
                await _gateway.SendMessageAsync(msg.ChannelId, "Format: `!cmd <perintah_tshock>`");
                return;
            }

            string normalized = rawCmd.StartsWith("/") || rawCmd.StartsWith(".") ? rawCmd : "/" + rawCmd;
            _mainThreadQueue.Enqueue(() =>
            {
                Commands.HandleCommand(TSPlayer.Server, normalized);
            });

            await _gateway.SendMessageAsync(msg.ChannelId, $"[OK] Command `{SanitizeDiscordMarkdown(normalized)}` dieksekusi oleh `{link.AccountName}` di Hub.");
        }

        #endregion

        #region In-Game RPC & Verification Operations (Cross-World Safe!)

        public static async Task<(bool Success, string Message)> ExecutePlayerVerifyCommandAsync(
            string accountName,
            string playerName,
            string subAction,
            string arg)
        {
            if (MultiWorldModule.IsChildProcess)
            {
                var resp = await MultiWorldModule.SendDiscordRpcToMasterAsync(new IpcRequest
                {
                    Action = "discord_rpc",
                    AdminSubAction = subAction,
                    AccountName = accountName,
                    PlayerName = playerName,
                    FormattedMessage = arg ?? string.Empty
                });
                return (resp.Success, resp.Message);
            }

            return await ProcessMasterRpcInternalAsync(subAction, accountName, playerName, arg ?? string.Empty);
        }

        public static async Task<IpcResponse> HandleMasterDiscordRpcAsync(string subAction, string accountName, string playerName, string arg)
        {
            var res = await ProcessMasterRpcInternalAsync(subAction, accountName, playerName, arg);
            return new IpcResponse { Success = res.Success, Message = res.Message };
        }

        private static async Task<(bool Success, string Message)> ProcessMasterRpcInternalAsync(
            string subAction,
            string accountName,
            string playerName,
            string arg)
        {
            if (Database == null)
            {
                return (false, "[ DISCORD ] Database Discord belum siap.");
            }

            PurgeExpiredPendingCodes();
            string sub = (subAction ?? "verify").Trim().ToLowerInvariant();

            if (sub == "verify" || sub == "code")
            {
                var existing = Database.GetByAccount(accountName);
                if (existing != null && existing.IsVerified)
                {
                    return (false, $"[ DISCORD ] Akunmu ('{accountName}') sudah terverifikasi dengan Discord @{existing.DiscordUsername}. Ketik '/discord unlink' jika ingin mengganti.");
                }

                // Hapus kode lama milik akun yang sama agar hanya 1 kode aktif per akun
                foreach (var kvp in _pendingInGameCodes.ToArray())
                {
                    if (kvp.Value.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                    {
                        _pendingInGameCodes.TryRemove(kvp.Key, out _);
                    }
                }

                string code = GenerateCode(6);
                _pendingInGameCodes[code] = new PendingInGameVerifyCode
                {
                    Code = code,
                    AccountName = accountName,
                    PlayerName = playerName,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(Config.VerifyCodeExpiryMinutes)
                };

                string cmdHint = $"{Config.CommandPrefix}verify {code}";
                return (true, $"[ DISCORD ] Kode Verifikasi Kamu: [c/4ade80:{code}] (berlaku {Config.VerifyCodeExpiryMinutes} menit). Kirim '[c/facc15:{cmdHint}]' di channel verifikasi Discord / DM Bot!");
            }

            if (sub == "confirm")
            {
                string code = (arg ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(code) || !_pendingDiscordRequests.TryRemove(code, out var req))
                {
                    return (false, "[ DISCORD ] Kode konfirmasi tidak ditemukan atau sudah kedaluwarsa.");
                }

                if (DateTime.UtcNow > req.ExpiresUtc)
                {
                    return (false, "[ DISCORD ] Permintaan verifikasi sudah kedaluwarsa.");
                }

                if (!req.TargetAccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                {
                    // Kembalikan ke antrean jika salah orang
                    _pendingDiscordRequests[code] = req;
                    return (false, "[ DISCORD ] Kode konfirmasi tersebut bukan ditujukan untuk akunmu!");
                }

                var record = new DiscordLinkRecord
                {
                    AccountName = accountName,
                    DiscordUserId = req.DiscordUserId,
                    DiscordUsername = req.DiscordUsername,
                    LinkedAtUtc = DateTime.UtcNow,
                    IsVerified = true
                };
                Database.UpsertLink(record);

                await ApplyDiscordRewardsAndSyncAsync(Config.GuildId, req.DiscordUserId, accountName);

                if (_gateway != null && _gateway.IsConnected)
                {
                    string ch = !string.IsNullOrWhiteSpace(Config.VerifyChannelId) ? Config.VerifyChannelId : Config.RelayChannelId;
                    if (!string.IsNullOrWhiteSpace(ch))
                    {
                        await _gateway.SendMessageAsync(ch, $"**[VERIFIED]** <@{req.DiscordUserId}> dikonfirmasi langsung dari game oleh **`{accountName}`**!", suppressMentions: false);
                    }
                    _ = _gateway.SendDmAsync(req.DiscordUserId, $"[Univers Authenticator] Akun Discord kamu berhasil dihubungkan ke '{accountName}'!");
                }

                return (true, $"[ DISCORD ] Berhasil! Akun '{accountName}' sekarang terhubung dengan Discord @{req.DiscordUsername}.");
            }

            if (sub == "deny")
            {
                string code = (arg ?? string.Empty).Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(code) && _pendingDiscordRequests.TryGetValue(code, out var req) &&
                    req.TargetAccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingDiscordRequests.TryRemove(code, out _);
                    return (true, "[ DISCORD ] Permintaan link Discord ditolak.");
                }
                return (false, "[ DISCORD ] Tidak ada permintaan link Discord dengan kode tersebut.");
            }

            if (sub == "unlink")
            {
                var existing = Database.GetByAccount(accountName);
                if (existing == null)
                {
                    return (false, "[ DISCORD ] Akunmu belum terhubung ke Discord mana pun.");
                }

                Database.UnlinkByAccount(accountName);
                await RemoveVerifiedRoleSafeAsync(Config.GuildId, existing.DiscordUserId);
                return (true, $"[ DISCORD ] Tautan akun '{accountName}' dengan Discord @{existing.DiscordUsername} telah diputuskan.");
            }

            if (sub == "status")
            {
                var existing = Database.GetByAccount(accountName);
                string botState = IsGatewayConnected ? $"ONLINE (@{BotUsername})" : "OFFLINE / STANDBY";
                int total = Database.GetTotalVerifiedCount();

                if (existing != null && existing.IsVerified)
                {
                    return (true, $"[ DISCORD ] Status: TERVERIFIKASI <-> @{existing.DiscordUsername} | Gateway: {botState} | Total Verified: {total}");
                }
                return (true, $"[ DISCORD ] Status: BELUM TERVERIFIKASI (Ketik /verify untuk mendapatkan kode!) | Gateway: {botState} | Total Verified: {total}");
            }

            return (false, "[ DISCORD ] Sub-perintah tidak dikenal. Ketik '/discord help'.");
        }

        #endregion

        #region Helpers (Role Sync, Sanitization, Code Generator)

        private static async Task ApplyDiscordRewardsAndSyncAsync(string msgGuildId, string discordUserId, string accountName)
        {
            if (_gateway == null) return;

            string guildId = !string.IsNullOrWhiteSpace(Config.GuildId) ? Config.GuildId : msgGuildId;
            if (string.IsNullOrWhiteSpace(guildId)) return;

            if (!string.IsNullOrWhiteSpace(Config.VerifiedRoleId))
            {
                await _gateway.AddGuildMemberRoleAsync(guildId, discordUserId, Config.VerifiedRoleId);
            }

            if (Config.SyncNicknameWithTShockName)
            {
                string rankName = GetCleanRankName(accountName);
                string nick = Config.NicknameFormat
                    .Replace("{AccountName}", accountName, StringComparison.OrdinalIgnoreCase)
                    .Replace("{Rank}", rankName, StringComparison.OrdinalIgnoreCase);

                await _gateway.ModifyGuildMemberNickAsync(guildId, discordUserId, nick);
            }

            // Sinkronkan role rank + element saat verifikasi.
            await SyncRolesForAccountAsync(guildId, discordUserId, accountName);
        }

        // Pastikan semua role rank (9) & element (7) sudah dibuat di Discord. Simpan ID ke config.
        private static async Task EnsureRoleMapsAsync()
        {
            if (_gateway == null) return;
            string guildId = Config.GuildId;
            if (string.IsNullOrWhiteSpace(guildId)) return;
            if (!Config.SyncRankRoles && !Config.SyncElementRoles) return;

            try
            {
                var existing = await _gateway.GetGuildRolesAsync(guildId);
                var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in existing) byName[r.Name] = r.Id;

                bool changed = false;

                if (Config.SyncRankRoles)
                {
                    foreach (var rank in RankModule.Config.Ranks)
                    {
                        if (Config.RankRoleIds.TryGetValue(rank.Id, out var rid) && !string.IsNullOrWhiteSpace(rid))
                            continue;
                        if (byName.TryGetValue(rank.Name, out var found))
                        {
                            Config.RankRoleIds[rank.Id] = found; changed = true; continue;
                        }
                        var created = await _gateway.CreateGuildRoleAsync(guildId, rank.Name, "99aab5");
                        if (!string.IsNullOrWhiteSpace(created))
                        {
                            Config.RankRoleIds[rank.Id] = created!;
                            byName[rank.Name] = created!;
                            changed = true;
                        }
                    }
                }

                if (Config.SyncElementRoles)
                {
                    foreach (var el in ElementDefs)
                    {
                        if (Config.ElementRoleIds.TryGetValue(el.Id, out var rid) && !string.IsNullOrWhiteSpace(rid))
                            continue;
                        if (byName.TryGetValue(el.Name, out var found))
                        {
                            Config.ElementRoleIds[el.Id] = found; changed = true; continue;
                        }
                        var created = await _gateway.CreateGuildRoleAsync(guildId, el.Name, el.Hex);
                        if (!string.IsNullOrWhiteSpace(created))
                        {
                            Config.ElementRoleIds[el.Id] = created!;
                            byName[el.Name] = created!;
                            changed = true;
                        }
                    }
                }

                if (changed && !string.IsNullOrEmpty(_configPath))
                {
                    Config.Save(_configPath);
                    TShock.Log.ConsoleInfo($"[Univers.Discord] Role rank ({Config.RankRoleIds.Count}) & element ({Config.ElementRoleIds.Count}) siap.");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Discord] Gagal menyiapkan role Discord: {ex.Message}");
            }
        }

        // Sinkronkan role rank + element untuk satu member (hapus role sistem lain yg tidak dipakai).
        private static async Task SyncRolesForAccountAsync(string guildId, string discordUserId, string accountName)
        {
            if (_gateway == null || string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(discordUserId)) return;
            if (!Config.SyncRankRoles && !Config.SyncElementRoles) return;

            var current = await _gateway.GetMemberRoleIdsAsync(guildId, discordUserId);
            if (current == null) return;
            var curSet = new HashSet<string>(current);

            if (Config.SyncRankRoles && Config.RankRoleIds.Count > 0)
            {
                string rankId = GetRankId(accountName);
                var allRank = new HashSet<string>(Config.RankRoleIds.Values);
                string? want = Config.RankRoleIds.TryGetValue(rankId, out var rr) ? rr : null;
                foreach (var rid in allRank)
                {
                    if (string.IsNullOrWhiteSpace(rid)) continue;
                    if (rid == want)
                    {
                        if (!curSet.Contains(rid)) { if (await _gateway.AddGuildMemberRoleAsync(guildId, discordUserId, rid)) curSet.Add(rid); }
                    }
                    else if (curSet.Contains(rid))
                    {
                        if (await _gateway.RemoveGuildMemberRoleAsync(guildId, discordUserId, rid)) curSet.Remove(rid);
                    }
                }
            }

            if (Config.SyncElementRoles && Config.ElementRoleIds.Count > 0)
            {
                string? elemId = GetElementId(accountName);
                var allElem = new HashSet<string>(Config.ElementRoleIds.Values);
                string? want = (elemId != null && Config.ElementRoleIds.TryGetValue(elemId, out var er)) ? er : null;
                foreach (var rid in allElem)
                {
                    if (string.IsNullOrWhiteSpace(rid)) continue;
                    if (rid == want)
                    {
                        if (!curSet.Contains(rid)) { if (await _gateway.AddGuildMemberRoleAsync(guildId, discordUserId, rid)) curSet.Add(rid); }
                    }
                    else if (curSet.Contains(rid))
                    {
                        if (await _gateway.RemoveGuildMemberRoleAsync(guildId, discordUserId, rid)) curSet.Remove(rid);
                    }
                }
            }
        }

        // Polling: sinkronkan semua player online yg sudah verify.
        private static async Task SyncAllOnlineRolesAsync()
        {
            if (_gateway == null || Database == null) return;
            string guildId = Config.GuildId;
            if (string.IsNullOrWhiteSpace(guildId)) return;

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.IsLoggedIn || player.Account == null) continue;
                var link = Database.GetByAccount(player.Account.Name);
                if (link == null || !link.IsVerified) continue;

                try { await SyncRolesForAccountAsync(guildId, link.DiscordUserId, link.AccountName); }
                catch { }
            }
        }

        private static string GetRankId(string accountName)
        {
            try
            {
                var data = RankModule.Database?.GetOrCreate(accountName);
                var rank = data != null ? RankModule.Config?.GetRankByIndex(data.RankIndex) : null;
                if (rank != null && !string.IsNullOrWhiteSpace(rank.Id)) return rank.Id;
            }
            catch { }
            return "Mortal";
        }

        private static string? GetElementId(string accountName)
        {
            try
            {
                foreach (var p in TShock.Players)
                {
                    if (p != null && p.Active && p.IsLoggedIn && p.Account != null &&
                        p.Account.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                    {
                        string el = AetherialApi.GetPlayerElement(p);
                        return string.IsNullOrWhiteSpace(el) ? null : el.ToUpperInvariant();
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task RemoveVerifiedRoleSafeAsync(string msgGuildId, string discordUserId)
        {
            if (_gateway == null || string.IsNullOrWhiteSpace(Config.VerifiedRoleId)) return;
            string guildId = !string.IsNullOrWhiteSpace(Config.GuildId) ? Config.GuildId : msgGuildId;
            if (string.IsNullOrWhiteSpace(guildId)) return;

            await _gateway.RemoveGuildMemberRoleAsync(guildId, discordUserId, Config.VerifiedRoleId);
        }

        private static async Task SendTempReplyAndCleanupSourceAsync(DiscordIncomingMessage msg, string text)
        {
            if (_gateway == null) return;
            if (!msg.IsDirectMessage && Config.DeleteUnverifiedMessagesInRelay)
            {
                await _gateway.DeleteMessageAsync(msg.ChannelId, msg.MessageId);
            }
            await SendTempWarningAsync(msg.ChannelId, text);
        }

        private static async Task SendTempWarningAsync(string channelId, string text)
        {
            if (_gateway == null) return;
            string? sentId = await _gateway.SendMessageAsync(channelId, text, suppressMentions: false);
            int delaySec = Config.UnverifiedWarningAutoDeleteSeconds;
            if (!string.IsNullOrWhiteSpace(sentId) && delaySec > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySec));
                    if (_gateway != null)
                    {
                        await _gateway.DeleteMessageAsync(channelId, sentId);
                    }
                });
            }
        }

        private static bool IsDiscordAdmin(DiscordIncomingMessage msg)
        {
            if (Config.AdminRoleIds == null || Config.AdminRoleIds.Count == 0) return false;
            return msg.RoleIds.Any(r => Config.AdminRoleIds.Contains(r));
        }

        private static string BuildInGameRankTag(string accountName)
        {
            try
            {
                var rankData = RankModule.Database?.GetOrCreate(accountName);
                var rankInfo = rankData != null ? RankModule.Config?.GetRankByIndex(rankData.RankIndex) : null;
                if (rankInfo != null && !string.IsNullOrWhiteSpace(rankInfo.Prefix))
                {
                    return rankInfo.Prefix.Trim();
                }
                if (rankInfo != null && !string.IsNullOrWhiteSpace(rankInfo.Name))
                {
                    return $"[{rankInfo.Name}]";
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetCleanRankName(string accountName)
        {
            try
            {
                var rankData = RankModule.Database?.GetOrCreate(accountName);
                var rankInfo = rankData != null ? RankModule.Config?.GetRankByIndex(rankData.RankIndex) : null;
                if (rankInfo != null && !string.IsNullOrWhiteSpace(rankInfo.Name))
                {
                    return rankInfo.Name;
                }
            }
            catch { }
            return "Mortal";
        }

        private static string CleanTerrariaTextForDiscord(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // Hilangkan tag warna & item icon Terraria agar rapi di Discord
            string clean = Regex.Replace(text, @"\[c/[0-9a-fA-F]{6}:([^\]]*)\]", "$1");
            clean = Regex.Replace(clean, @"\[i(?:/[^\]]*)?:(\d+)\]", "");
            clean = Regex.Replace(clean, @"\[a:([^\]]*)\]", "$1");
            clean = clean.Replace("@everyone", "@ everyone").Replace("@here", "@ here");
            return Regex.Replace(clean, @"\s{2,}", " ").Trim();
        }

        private static string SanitizeDiscordChatForTerraria(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string s = text.Replace("\r", " ").Replace("\n", " ").Trim();

            if (Config.SanitizeTerrariaTagsFromDiscord)
            {
                // Cegah injeksi tag warna / item / achievement palsu dari Discord ke Terraria
                s = Regex.Replace(s, @"\[(?:c|i|n|a|g)(?:/[^\]]*)?:([^\]]*)\]", "$1", RegexOptions.IgnoreCase);
            }

            // Buang karakter non-ASCII (emoji Unicode Discord) agar chat Terraria tidak kotak-kotak
            var chars = s.Where(c => c >= 32 && c <= 126).ToArray();
            s = new string(chars).Trim();

            if (s.Length > 220)
            {
                s = s.Substring(0, 220) + "...";
            }

            return s;
        }

        private static string SanitizeDiscordMarkdown(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("`", "'").Replace("*", "").Replace("_", "\\_").Replace("@", "@\u200b");
        }

        private static string GenerateCode(int length)
        {
            Span<byte> buf = stackalloc byte[length];
            RandomNumberGenerator.Fill(buf);
            char[] chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = CodeAlphabet[buf[i] % CodeAlphabet.Length];
            }
            return new string(chars);
        }

        private static void PurgeExpiredPendingCodes()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var kvp in _pendingInGameCodes.ToArray())
            {
                if (now > kvp.Value.ExpiresUtc)
                {
                    _pendingInGameCodes.TryRemove(kvp.Key, out _);
                }
            }

            foreach (var kvp in _pendingDiscordRequests.ToArray())
            {
                if (now > kvp.Value.ExpiresUtc)
                {
                    _pendingDiscordRequests.TryRemove(kvp.Key, out _);
                }
            }
        }

        #endregion
    }
}
