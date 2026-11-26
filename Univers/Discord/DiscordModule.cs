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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using Skynomi.Ranks;
using Terraria;
using Terraria.ID;
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
        private static DateTime _lastGuildMetaSyncUtc = DateTime.MinValue;

        private static bool _lastBloodMoonState;
        private static bool _lastEclipseState;
        private static int _lastInvasionType;
        private static DateTime _lastWorldEventScanUtc = DateTime.MinValue;
        private static DateTime _moduleInitUtc = DateTime.UtcNow;
        private static DateTime _lastGatewayReadyUtc = DateTime.MinValue;
        private static DateTime _firstPlayerJoinGraceUntilUtc = DateTime.MinValue;
        private static int _lastObservedActivePlayerCount;
        private static bool _worldEventBaselineSeeded;

        // Pastikan broadcast "server started" & purge #verify hanya terjadi SEKALI per proses.
        private static bool _startupAnnounced;
        private static readonly Dictionary<int, string> _activeTownNpcSlots = new();
        private static readonly Dictionary<string, DateTime> _recentTownNpcAnnouncements = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> _activeBossSlots = new();
        private static readonly Dictionary<string, DateTime> _recentBossAwokenAnnouncements = new(StringComparer.OrdinalIgnoreCase);

        public static void BroadcastServerMessage(string message)
        {
            if (!_initialized || !Config.Enabled) return;
            if (string.IsNullOrWhiteSpace(message)) return;

            string stripped = StripWorldAndServerTags(message);
            if (string.IsNullOrWhiteSpace(stripped)) return;

            if (MultiWorldModule.IsChildProcess)
            {
                _ = MultiWorldModule.SendChildEventToDiscordAsync(MultiWorldModule.CurrentWorldId, stripped);
            }
            else
            {
                EnqueueRelayPost(MultiWorldModule.CurrentWorldId, stripped, useEmbed: true);
            }
        }

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

        // Cache nama role (Id -> Name), warna role world (worldId -> 24-bit RGB int), & custom emoji guild.
        private static readonly ConcurrentDictionary<string, string> _guildRoleNames = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, int> _worldRoleColors = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _guildEmojisByNormName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _guildEmojisExactName = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, string[]> SwordBadgeAliases = new()
        {
            [ItemID.CopperShortsword] = new[] { "coppershortsword", "copperbroadsword", "coppersword", "copper", "lv1", "tier1", "badge1" },
            [ItemID.CopperBroadsword] = new[] { "copperbroadsword", "coppershortsword", "coppersword", "copper" },
            [ItemID.NightsEdge] = new[] { "nightsedge", "nightedge", "darkblade", "lv100", "tier2", "badge2" },
            [ItemID.TerraBlade] = new[] { "terrablade", "terrasword", "terra", "lv250", "tier3", "badge3" },
            [ItemID.FragmentSolar] = new[] { "solareruption", "solarfragment", "fragmentsolar", "solar", "lv500", "tier4", "badge4" },
            [ItemID.SolarEruption] = new[] { "solareruption", "solarfragment", "solar" },
            [ItemID.Meowmere] = new[] { "meowmere", "meowmare", "meow", "lv750", "tier5", "badge5" },
            [ItemID.Zenith] = new[] { "zenith", "zenithapex", "apex", "lv950", "tier6", "badge6" },
        };

        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static bool IsGatewayConnected => _gateway != null && _gateway.IsConnected;
        public static string BotUsername => _gateway?.BotUsername ?? "-";

        public static void Initialize(string savePath)
        {
            if (_initialized) return;

            _moduleInitUtc = DateTime.UtcNow;
            _worldEventBaselineSeeded = false;
            _lastObservedActivePlayerCount = 0;
            _firstPlayerJoinGraceUntilUtc = DateTime.MinValue;
            _activeTownNpcSlots.Clear();
            _recentTownNpcAnnouncements.Clear();
            _activeBossSlots.Clear();
            _recentBossAwokenAnnouncements.Clear();

            string localUniversDir = Path.Combine(savePath, "Univers");
            string masterUniversDir = ResolveMasterUniversDir(localUniversDir);
            Directory.CreateDirectory(masterUniversDir);

            _configPath = Path.Combine(masterUniversDir, "DiscordConfig.json");
            Config = DiscordConfig.Load(_configPath);

            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.NpcKilled.Register(UniversPlugin.Instance, OnNpcKilled);

            // Set _initialized SEBELUM start gateway: onReady bisa jalan cepat di thread lain,
            // dan BroadcastServerMessage() menolak kirim jika _initialized masih false
            // (penyebab "server started" & relay in-game tidak pernah sampai ke Discord).
            DiscordCommands.Register();
            _initialized = true;

            if (!MultiWorldModule.IsChildProcess)
            {
                Database = new DiscordDatabase(Path.Combine(masterUniversDir, "univers_discord.db"));
                StartGatewayIfConfigured();
            }
            else
            {
                TShock.Log.ConsoleInfo("[Univers.Discord] Child World mode aktif: /verify & event relay tersambung ke Master Hub via IPC.");
            }
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

        public static void RefreshGuildMetadataAsync()
        {
            if (_gateway == null || !_gateway.IsConnected) return;
            _ = CacheGuildMetadataAndEnsureRolesAsync();
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

            if (_gateway != null && _gateway.IsConnected && !string.IsNullOrWhiteSpace(Config.RelayChannelId))
            {
                try
                {
                    string shutdownText = "**Server shutdown.** Portal ke dunia Univers sedang tertutup.\n*(Server shutdown. Portal to the Univers world is now closed.)*";
                    if (Config.UseEmbedsForRelay)
                    {
                        var embed = BuildRelayEmbed(MultiWorldModule.CurrentWorldId, shutdownText);
                        _gateway.SendEmbedAsync(Config.RelayChannelId, embed, suppressMentions: true).GetAwaiter().GetResult();
                    }
                    else
                    {
                        _gateway.SendMessageAsync(Config.RelayChannelId, shutdownText, suppressMentions: true).GetAwaiter().GetResult();
                    }
                }
                catch { }
            }

            try { DiscordCommands.Unregister(); } catch { }
            try { ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate); } catch { }
            try { ServerApi.Hooks.NpcKilled.Deregister(UniversPlugin.Instance, OnNpcKilled); } catch { }

            StopGateway();

            _pendingInGameCodes.Clear();
            _pendingDiscordRequests.Clear();
            _lastUnverifiedWarnUtc.Clear();
            _lastDiscordChatUtc.Clear();
            _activeTownNpcSlots.Clear();
            _recentTownNpcAnnouncements.Clear();
            _activeBossSlots.Clear();
            _recentBossAwokenAnnouncements.Clear();
            _guildRoleNames.Clear();
            _worldRoleColors.Clear();
            _guildEmojisByNormName.Clear();
            _guildEmojisExactName.Clear();
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
                    _lastGatewayReadyUtc = DateTime.UtcNow;
                    TShock.Log.ConsoleInfo($"[Univers.Discord] Bot '@{botName}' terhubung ke Discord! Relay={(string.IsNullOrWhiteSpace(Config.RelayChannelId) ? "UNSET" : Config.RelayChannelId)} | AuthGate={(Config.RequireVerifiedAccountToChat ? "STRICT" : "OPEN")}");

                    _ = Task.Run(async () =>
                    {
                        // Ambil role colors & custom guild emojis dulu sebelum kirim startup embed
                        await CacheGuildMetadataAndEnsureRolesAsync();

                        // onReady juga terpicu pada resume-reconnect. Hanya kirim notifikasi startup & purge
                        // #verify sekali per proses, jangan tiap reconnect (penyebab spam "Portal terbuka").
                        bool firstReadyThisProcess = !_startupAnnounced;
                        _startupAnnounced = true;
                        if (firstReadyThisProcess)
                        {
                            if (!string.IsNullOrWhiteSpace(Config.RelayChannelId))
                            {
                                BroadcastServerMessage("**Server has started!** Portal ke dunia Univers telah terbuka.\n*(Server has started! Portal to the Univers world is now open.)*");
                            }
                            await PurgeVerifyChannelIfEnabledAsync();
                        }
                    });
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

        private static async Task CacheGuildMetadataAndEnsureRolesAsync()
        {
            await CacheGuildRolesAndEmojisAsync();
            await EnsureRoleMapsAsync();
        }

        // Ambil peta Role (Id, Name, Color -> WorldRoleIds & warna Embed) + seluruh Custom Emoji server Discord.
        private static async Task CacheGuildRolesAndEmojisAsync()
        {
            if (_gateway == null || string.IsNullOrWhiteSpace(Config.GuildId)) return;
            _lastGuildMetaSyncUtc = DateTime.UtcNow;

            try
            {
                var roles = await _gateway.GetGuildRolesDetailedAsync(Config.GuildId);
                var rolesById = new Dictionary<string, DiscordGuildRole>(StringComparer.OrdinalIgnoreCase);
                var rolesByName = new Dictionary<string, DiscordGuildRole>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in roles)
                {
                    _guildRoleNames[r.Id] = r.Name;
                    rolesById[r.Id] = r;
                    if (!string.IsNullOrWhiteSpace(r.Name))
                    {
                        rolesByName[r.Name.Trim()] = r;
                    }
                }

                bool cfgChanged = false;
                var candidateWorldIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lobby", "survival" };
                try
                {
                    foreach (var w in MultiWorldModule.Config.Worlds)
                    {
                        if (!string.IsNullOrWhiteSpace(w.Id)) candidateWorldIds.Add(w.Id);
                    }
                }
                catch { }

                foreach (string wid in candidateWorldIds)
                {
                    DiscordGuildRole? matched = null;

                    if (Config.WorldRoleIds.TryGetValue(wid, out var configuredRoleId) &&
                        !string.IsNullOrWhiteSpace(configuredRoleId) &&
                        rolesById.TryGetValue(configuredRoleId, out var byIdRole))
                    {
                        matched = byIdRole;
                    }
                    else
                    {
                        var worldDef = MultiWorldModule.Config?.GetWorld(wid);
                        string[] candidates =
                        {
                            wid,
                            worldDef?.DisplayName ?? string.Empty,
                            worldDef?.ChatTag ?? string.Empty
                        };

                        foreach (string cand in candidates)
                        {
                            if (!string.IsNullOrWhiteSpace(cand) && rolesByName.TryGetValue(cand.Trim(), out var found))
                            {
                                matched = found;
                                break;
                            }
                        }

                        if (matched != null)
                        {
                            Config.WorldRoleIds[wid] = matched.Id;
                            cfgChanged = true;
                        }
                    }

                    if (matched != null && matched.Color > 0)
                    {
                        _worldRoleColors[wid] = matched.Color;
                    }
                }

                if (cfgChanged && !string.IsNullOrEmpty(_configPath))
                {
                    Config.Save(_configPath);
                }

                var emojis = await _gateway.GetGuildEmojisAsync(Config.GuildId);
                _guildEmojisByNormName.Clear();
                _guildEmojisExactName.Clear();
                foreach (var e in emojis)
                {
                    string tag = e.FormatTag();
                    if (string.IsNullOrEmpty(tag)) continue;

                    _guildEmojisExactName[e.Name] = tag;
                    string norm = NormalizeEmojiKey(e.Name);
                    if (!string.IsNullOrEmpty(norm) && !_guildEmojisByNormName.ContainsKey(norm))
                    {
                        _guildEmojisByNormName[norm] = tag;
                    }
                }

                TShock.Log.ConsoleInfo($"[Univers.Discord] Metadata Discord dimuat: {roles.Count} role, {emojis.Count} custom emoji.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Discord] Gagal memuat role/emoji Discord: {ex.Message}");
            }
        }

        // Ambil nama role admin pertama yang dimiliki msg (berdasarkan AdminRoleIds).
        private static string GetAdminRoleName(DiscordIncomingMessage msg)
        {
            if (Config.AdminRoleIds == null) return "Admin";
            foreach (var rid in msg.RoleIds)
            {
                if (Config.AdminRoleIds.Contains(rid) && _guildRoleNames.TryGetValue(rid, out var nm) && !string.IsNullOrWhiteSpace(nm))
                {
                    return nm;
                }
            }
            return "Admin";
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

            // Real-time role sync & refresh role color/emoji ke Discord (polling berkala).
            if (!MultiWorldModule.IsChildProcess && _gateway != null && _gateway.IsConnected)
            {
                int pollSec = Config.RoleSyncPollSeconds > 0 ? Config.RoleSyncPollSeconds : 120;
                if ((DateTime.UtcNow - _lastRoleSyncUtc).TotalSeconds >= pollSec)
                {
                    _lastRoleSyncUtc = DateTime.UtcNow;
                    _ = Task.Run(async () =>
                    {
                        if ((DateTime.UtcNow - _lastGuildMetaSyncUtc).TotalMinutes >= 2)
                        {
                            await CacheGuildRolesAndEmojisAsync();
                        }
                        if (Config.RoleSyncPollSeconds > 0 && (Config.SyncRankRoles || Config.SyncElementRoles || Config.SyncWorldRoles))
                        {
                            await SyncAllOnlineRolesAsync();
                        }
                    });
                }
            }

            // Pindai event dunia, town NPC, dan boss spawn untuk relay ke Discord
            if ((DateTime.UtcNow - _lastWorldEventScanUtc).TotalSeconds >= 2.5)
            {
                _lastWorldEventScanUtc = DateTime.UtcNow;
                ScanAndBroadcastWorldEvents();
            }
        }

        private static bool IsLobbyWorld()
        {
            string wid = MultiWorldModule.CurrentWorldId ?? string.Empty;
            return !MultiWorldModule.IsChildProcess || wid.Equals("lobby", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountActiveRealPlayers()
        {
            int count = 0;
            try
            {
                foreach (var p in TShock.Players)
                {
                    if (p != null && p.Active && p.RealPlayer) count++;
                }
            }
            catch { }
            return count;
        }

        private static void ScanAndBroadcastWorldEvents()
        {
            if (Main.npc == null) return;

            try
            {
                int activePlayers = CountActiveRealPlayers();
                if (_lastObservedActivePlayerCount == 0 && activePlayers > 0)
                {
                    // Ketika player pertama masuk dari kondisi idle/kosong, serap NPC yg baru respawn selama 12 detik tanpa spam
                    _firstPlayerJoinGraceUntilUtc = DateTime.UtcNow.AddSeconds(12);
                }
                _lastObservedActivePlayerCount = activePlayers;

                // Baseline pass pertama: rekam semua state, Town NPC, dan Boss yg sudah ada secara diam-diam!
                if (!_worldEventBaselineSeeded)
                {
                    _lastBloodMoonState = Main.bloodMoon;
                    _lastEclipseState = Main.eclipse;
                    _lastInvasionType = Main.invasionType;

                    for (int i = 0; i < Main.maxNPCs; i++)
                    {
                        var npc = Main.npc[i];
                        if (npc == null || !npc.active) continue;

                        if ((npc.townNPC || npc.isLikeATownNPC) && !string.IsNullOrEmpty(npc.GivenName))
                        {
                            string sig = $"{npc.type}:{npc.GivenName}";
                            _activeTownNpcSlots[i] = sig;
                            _recentTownNpcAnnouncements[sig] = DateTime.UtcNow;
                        }

                        if (npc.boss || BossContributionTracker.IsBossDefeatNpc(npc))
                        {
                            _activeBossSlots.Add(i);
                            if (!string.IsNullOrWhiteSpace(npc.FullName))
                            {
                                _recentBossAwokenAnnouncements[npc.FullName] = DateTime.UtcNow;
                            }
                        }
                    }

                    _worldEventBaselineSeeded = true;
                    return;
                }

                bool readyToBroadcast = Config.Enabled &&
                    (DateTime.UtcNow - _moduleInitUtc).TotalSeconds >= 20 &&
                    (MultiWorldModule.IsChildProcess ||
                     (_gateway != null && _gateway.IsConnected && !string.IsNullOrWhiteSpace(Config.RelayChannelId) &&
                      _lastGatewayReadyUtc != DateTime.MinValue && (DateTime.UtcNow - _lastGatewayReadyUtc).TotalSeconds >= 10));

                // 1. Blood Moon State
                if (Main.bloodMoon && !_lastBloodMoonState)
                {
                    _lastBloodMoonState = true;
                    if (readyToBroadcast)
                    {
                        BroadcastServerMessage("A Blood Moon is rising... / Darah bulan mulai naik di malam hari!");
                    }
                }
                else if (!Main.bloodMoon && _lastBloodMoonState)
                {
                    _lastBloodMoonState = false;
                }

                // 2. Solar Eclipse State
                if (Main.eclipse && !_lastEclipseState)
                {
                    _lastEclipseState = true;
                    if (readyToBroadcast)
                    {
                        BroadcastServerMessage("A Solar Eclipse is happening! / Gerhana matahari terjadi!");
                    }
                }
                else if (!Main.eclipse && _lastEclipseState)
                {
                    _lastEclipseState = false;
                }

                // 3. Invasions
                int inv = Main.invasionType;
                if (inv > 0 && inv != _lastInvasionType)
                {
                    _lastInvasionType = inv;
                    if (readyToBroadcast)
                    {
                        string invName = inv switch
                        {
                            1 => "The Goblin Army is approaching from the east! (Pasukan Goblin mendekat)",
                            2 => "The Frost Legion has arrived! (Legiun Es tiba)",
                            3 => "Pirates are arriving from the sea! (Bajak laut menyerang)",
                            4 => "Martian Madness has begun! (Kegilaan Martian dimulai)",
                            _ => "An invasion has begun! (Invasi dimulai)"
                        };
                        BroadcastServerMessage(invName);
                    }
                }
                else if (inv == 0 && _lastInvasionType > 0)
                {
                    _lastInvasionType = 0;
                    if (readyToBroadcast)
                    {
                        BroadcastServerMessage("The invasion has been defeated! / Invasi telah dikalahkan!");
                    }
                }

                // Prune slot Town NPC & Boss yg sudah tidak aktif (JANGAN pernah .Clear() massal agar tidak re-spam!)
                foreach (var slot in _activeTownNpcSlots.Keys.ToList())
                {
                    var existingNpc = (slot >= 0 && slot < Main.maxNPCs) ? Main.npc[slot] : null;
                    if (existingNpc == null || !existingNpc.active || (!existingNpc.townNPC && !existingNpc.isLikeATownNPC))
                    {
                        _activeTownNpcSlots.Remove(slot);
                    }
                }
                foreach (var bossSlot in _activeBossSlots.ToList())
                {
                    var existingBoss = (bossSlot >= 0 && bossSlot < Main.maxNPCs) ? Main.npc[bossSlot] : null;
                    if (existingBoss == null || !existingBoss.active)
                    {
                        _activeBossSlots.Remove(bossSlot);
                    }
                }

                bool allowTownNpcBroadcast = readyToBroadcast &&
                    Config.RelayTownNpcArrivals &&
                    DateTime.UtcNow >= _firstPlayerJoinGraceUntilUtc &&
                    (!IsLobbyWorld() || Config.RelayLobbyTownNpcArrivals);

                // 4. Town NPC Arrivals & Boss Spawns
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    var npc = Main.npc[i];
                    if (npc == null || !npc.active) continue;

                    // Town NPC
                    if ((npc.townNPC || npc.isLikeATownNPC) && !string.IsNullOrEmpty(npc.GivenName))
                    {
                        string sig = $"{npc.type}:{npc.GivenName}";
                        if (!_activeTownNpcSlots.TryGetValue(i, out var oldSig) || oldSig != sig)
                        {
                            _activeTownNpcSlots[i] = sig;
                            bool onCooldown = _recentTownNpcAnnouncements.TryGetValue(sig, out var lastNpcTime) &&
                                              (DateTime.UtcNow - lastNpcTime).TotalMinutes < 20;
                            _recentTownNpcAnnouncements[sig] = DateTime.UtcNow;

                            if (allowTownNpcBroadcast && !onCooldown)
                            {
                                BroadcastServerMessage($"{npc.FullName} ({npc.GivenName}) has arrived! / telah tiba di dunia!");
                            }
                        }
                    }

                    // Boss Spawn
                    if (npc.boss || BossContributionTracker.IsBossDefeatNpc(npc))
                    {
                        if (_activeBossSlots.Add(i) && readyToBroadcast)
                        {
                            string bossTitle = string.IsNullOrWhiteSpace(npc.FullName) ? "Boss" : npc.FullName;
                            bool bossCooldown = _recentBossAwokenAnnouncements.TryGetValue(bossTitle, out var lastBossTime) &&
                                                (DateTime.UtcNow - lastBossTime).TotalSeconds < 15;
                            _recentBossAwokenAnnouncements[bossTitle] = DateTime.UtcNow;

                            if (!bossCooldown)
                            {
                                BroadcastServerMessage($"Boss **{bossTitle}** has awoken! / telah bangkit!");
                            }
                        }
                    }
                }
            }
            catch { }
        }

        #region In-Game -> Discord Event Hooks

        public static void NotifyInGameChat(string worldId, string senderName, string formattedChat)
        {
            if (!_initialized || !Config.Enabled || !Config.RelayInGameChatToDiscord) return;
            if (MultiWorldModule.IsChildProcess) return;
            if (_gateway == null || !_gateway.IsConnected || string.IsNullOrWhiteSpace(Config.RelayChannelId)) return;

            string formattedDesc = FormatInGameChatForDiscord(senderName, formattedChat);
            if (string.IsNullOrWhiteSpace(formattedDesc)) return;

            EnqueueRelayPost(worldId, PrependWorldPylon(formattedDesc, worldId));
        }

        // Ikon pylon per world: lobby = Universal_Pylon, world lain = Forest_Pylon.
        private static string ResolveWorldPylonEmoji(string? worldId)
        {
            string name = (string.IsNullOrWhiteSpace(worldId) || worldId.Equals("lobby", StringComparison.OrdinalIgnoreCase))
                ? "Universal_Pylon"
                : "Forest_Pylon";

            if (_guildEmojisExactName.TryGetValue(name, out string? tag) && !string.IsNullOrWhiteSpace(tag))
                return tag!;

            string norm = NormalizeEmojiKey(name);
            if (!string.IsNullOrEmpty(norm))
            {
                if (_guildEmojisByNormName.TryGetValue(norm, out tag) && !string.IsNullOrWhiteSpace(tag))
                    return tag!;
                foreach (var kv in _guildEmojisByNormName)
                {
                    if (kv.Key.Contains(norm, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }
            }
            return string.Empty;
        }

        private static string PrependWorldPylon(string description, string? worldId)
        {
            string pylon = ResolveWorldPylonEmoji(worldId);
            return string.IsNullOrEmpty(pylon) ? description : $"[{pylon}] {description}";
        }

        public static void NotifyPresenceEvent(string eventKind, string worldId, string playerName)
        {
            if (!_initialized || !Config.Enabled || !Config.RelayJoinLeaveToDiscord) return;
            if (MultiWorldModule.IsChildProcess) return;
            if (_gateway == null || !_gateway.IsConnected || string.IsNullOrWhiteSpace(Config.RelayChannelId)) return;
            if (string.IsNullOrWhiteSpace(playerName)) return;

            string safeName = SanitizeDiscordMarkdown(playerName);

            string line = eventKind switch
            {
                "server_join" => $"**[+]** `{safeName}` bergabung ke server.",
                "server_leave" => $"**[-]** `{safeName}` keluar dari server.",
                "world_switch" => $"**[~]** `{safeName}` pindah ke world **{GetWorldDisplayName(worldId)}**.",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(line))
            {
                EnqueueRelayPost(worldId, line, useEmbed: true, mentionWorldRole: eventKind == "world_switch");
            }
        }

        private static string GetWorldDisplayName(string? worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId)) return "lobby";
            try
            {
                var def = MultiWorldModule.Config?.GetWorld(worldId);
                if (def != null && !string.IsNullOrWhiteSpace(def.DisplayName)) return def.DisplayName;
            }
            catch { }
            return worldId;
        }

        public static void NotifyExternalEventFromChild(string worldId, string message)
        {
            if (!_initialized || !Config.Enabled) return;
            if (MultiWorldModule.IsChildProcess) return;
            if (_gateway == null || !_gateway.IsConnected || string.IsNullOrWhiteSpace(Config.RelayChannelId)) return;

            string clean = CleanTerrariaTextForDiscord(message, preserveEmojiTags: true);
            clean = StripWorldAndServerTags(clean);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                EnqueueRelayPost(worldId, clean);
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
            string msg = $"**Boss Ditaklukkan:** `{SanitizeDiscordMarkdown(bossName)}` telah dikalahkan!";

            if (MultiWorldModule.IsChildProcess)
            {
                _ = MultiWorldModule.SendChildEventToDiscordAsync(MultiWorldModule.CurrentWorldId, msg);
            }
            else
            {
                EnqueueRelayPost(MultiWorldModule.CurrentWorldId, msg, useEmbed: true);
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
            bool isAdminBypass = IsDiscordAdmin(msg);
            if (!isAdminBypass && Config.RequireVerifiedAccountToChat && (link == null || !link.IsVerified))
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

            string senderDisplay;
            string rankPart;
            if (link != null && link.IsVerified)
            {
                senderDisplay = link.AccountName;
                rankPart = BuildInGameRankTag(link.AccountName);
            }
            else if (isAdminBypass)
            {
                senderDisplay = msg.AuthorDisplayName;
                rankPart = $"[c/ef4444:{SanitizeDiscordMarkdown(GetAdminRoleName(msg))}]";
            }
            else
            {
                senderDisplay = msg.AuthorDisplayName;
                rankPart = "[c/94a3b8:Guest]";
            }
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
            int badgeId = ExpModule.Config?.GetBadgeIdForLevel(level) ?? ItemID.CopperShortsword;
            string swordEmoji = Config.UseLevelSwordEmojisInRelay ? ResolveItemEmojiTag(badgeId) : string.Empty;
            string badgePrefix = string.IsNullOrEmpty(swordEmoji) ? string.Empty : $"{swordEmoji} ";

            string text = $"{badgePrefix}Discord: <@{rec.DiscordUserId}> (`@{SanitizeDiscordMarkdown(rec.DiscordUsername)}`) <-> TShock: **`{rec.AccountName}`** | Rank: `{rankName}` | Lv.{level}";
            if (Config.UseEmbedsForRelay)
            {
                var embed = new JObject
                {
                    ["color"] = ResolveWorldEmbedColorInt(MultiWorldModule.CurrentWorldId),
                    ["title"] = "Identitas Player",
                    ["description"] = text
                };
                await _gateway.SendEmbedAsync(msg.ChannelId, embed, suppressMentions: true);
            }
            else
            {
                await _gateway.SendMessageAsync(msg.ChannelId, text);
            }
        }

        private static async Task HandleDiscordOnlineAsync(DiscordIncomingMessage msg)
        {
            if (_gateway == null) return;
            var worlds = await MultiWorldModule.FetchAllWorldSnapshotsAsync();
            int total = worlds.Sum(w => w.PlayerCount);

            if (Config.UseEmbedsForRelay)
            {
                if (total == 0)
                {
                    var emptyEmbed = new JObject
                    {
                        ["color"] = ResolveWorldEmbedColorInt("lobby"),
                        ["title"] = "Pemain Online (0)",
                        ["description"] = "Belum ada pemain yang sedang online."
                    };
                    await _gateway.SendEmbedAsync(msg.ChannelId, emptyEmbed, suppressMentions: true);
                    return;
                }

                var fields = new JArray();
                foreach (var w in worlds)
                {
                    string names = w.OnlinePlayerNames.Count > 0 ? SanitizeDiscordMarkdown(string.Join(", ", w.OnlinePlayerNames)) : "-";
                    string label = string.IsNullOrWhiteSpace(w.DisplayName) ? w.Id : w.DisplayName;
                    string icon = ResolveWorldPylonEmoji(w.Id);
                    fields.Add(new JObject
                    {
                        ["name"] = $"{(string.IsNullOrEmpty(icon) ? string.Empty : icon + " ")}{label} ({w.PlayerCount})",
                        ["value"] = names,
                        ["inline"] = true
                    });
                }

                var embed = new JObject
                {
                    ["color"] = ResolveWorldEmbedColorInt("survival"),
                    ["title"] = $"Pemain Online ({total})",
                    ["fields"] = fields
                };
                await _gateway.SendEmbedAsync(msg.ChannelId, embed, suppressMentions: true);
                return;
            }

            if (total == 0)
            {
                await _gateway.SendMessageAsync(msg.ChannelId, "**Pemain Online:** 0 pemain sedang online.");
                return;
            }

            var lines = new List<string> { $"**Pemain Online ({total}):**" };
            foreach (var w in worlds)
            {
                string names = w.OnlinePlayerNames.Count > 0 ? string.Join(", ", w.OnlinePlayerNames) : "-";
                string label = string.IsNullOrWhiteSpace(w.DisplayName) ? w.Id : w.DisplayName;
                string icon = ResolveWorldPylonEmoji(w.Id);
                lines.Add($"- {(string.IsNullOrEmpty(icon) ? string.Empty : icon + " ")}**{label}** ({w.PlayerCount}): {SanitizeDiscordMarkdown(names)}");
            }
            await _gateway.SendMessageAsync(msg.ChannelId, string.Join("\n", lines));
        }

        private static async Task HandleDiscordHelpAsync(DiscordIncomingMessage msg)
        {
            if (_gateway == null) return;
            string p = Config.CommandPrefix;
            string help =
                $"- `{p}verify <KODE>` : Verifikasi kode dari `/verify` in-game agar bisa chat\n" +
                $"- `{p}link <AkunTShock>` : Minta verifikasi ke akunmu yang sedang online di game\n" +
                $"- `{p}unlink` : Putuskan tautan akun Discord <-> TShock\n" +
                $"- `{p}online` : Lihat daftar pemain online di Lobby & Survival\n" +
                $"- `{p}who [nama/@user]` : Cek identitas resmi TShock & Rank player";

            if (Config.UseEmbedsForRelay)
            {
                var embed = new JObject
                {
                    ["color"] = ResolveWorldEmbedColorInt("lobby"),
                    ["title"] = "Univers Discord Authenticator & Relay",
                    ["description"] = help
                };
                await _gateway.SendEmbedAsync(msg.ChannelId, embed, suppressMentions: true);
            }
            else
            {
                await _gateway.SendMessageAsync(msg.ChannelId, $"**Univers Discord Authenticator & Relay**\n{help}");
            }
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
                foreach (var r in existing)
                {
                    byName[r.Name] = r.Id;
                    _guildRoleNames[r.Id] = r.Name;
                }

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

        // Sinkronkan role rank + element + world untuk satu member (hapus role sistem lain yg tidak dipakai).
        private static async Task SyncRolesForAccountAsync(string guildId, string discordUserId, string accountName, string? worldId = null)
        {
            if (_gateway == null || string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(discordUserId)) return;
            if (!Config.SyncRankRoles && !Config.SyncElementRoles && !Config.SyncWorldRoles) return;

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

            if (Config.SyncWorldRoles && Config.WorldRoleIds.Count > 0 && !string.IsNullOrWhiteSpace(worldId))
            {
                var allWorldRoles = new HashSet<string>(Config.WorldRoleIds.Values);
                string? want = Config.WorldRoleIds.TryGetValue(worldId, out var wr) ? wr : null;
                if (string.IsNullOrWhiteSpace(want))
                {
                    TShock.Log.ConsoleWarn($"[Univers.Discord] World role '{worldId}' belum terpetakan (WorldRoleIds kosong utk key ini) — sync world role DILEWATI. Jalankan '/discord roles refresh'.");
                }
                foreach (var rid in allWorldRoles)
                {
                    if (string.IsNullOrWhiteSpace(rid)) continue;
                    if (rid == want)
                    {
                        if (!curSet.Contains(rid))
                        {
                            if (await _gateway.AddGuildMemberRoleAsync(guildId, discordUserId, rid)) curSet.Add(rid);
                            else TShock.Log.ConsoleWarn($"[Univers.Discord] Gagal add world role {rid} ke user {discordUserId}. Cek permission 'Manage Roles' + posisi role bot harus di atas role world.");
                        }
                    }
                    else if (curSet.Contains(rid))
                    {
                        if (await _gateway.RemoveGuildMemberRoleAsync(guildId, discordUserId, rid)) curSet.Remove(rid);
                    }
                }
            }
        }

        // Dipanggil saat player pindah world -> sync world role segera (tak perlu tunggu polling).
        public static void RequestImmediateRoleSync(string accountName, string worldId)
        {
            if (MultiWorldModule.IsChildProcess) return;
            if (_gateway == null || !_gateway.IsConnected) return;
            if (!Config.SyncWorldRoles && !Config.SyncRankRoles && !Config.SyncElementRoles) return;
            if (Database == null || string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(worldId)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var link = Database.GetByAccount(accountName);
                    if (link == null)
                    {
                        // Nama player != nama akun TShock: coba resolve lewat player lokal.
                        var local = TShock.Players.FirstOrDefault(p =>
                            p != null && p.Active && p.IsLoggedIn && p.Account != null &&
                            p.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));
                        if (local?.Account != null) link = Database.GetByAccount(local.Account.Name);
                    }
                    if (link == null || !link.IsVerified) return;
                    await SyncRolesForAccountAsync(Config.GuildId, link.DiscordUserId, link.AccountName, worldId);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleWarn($"[Univers.Discord] Immediate world-role sync gagal ({accountName} -> {worldId}): {ex.Message}");
                }
            });
        }

        // Polling: sinkronkan semua player online (Lintas World: Lobby + Survival) yg sudah verify.
        private static async Task SyncAllOnlineRolesAsync()
        {
            if (_gateway == null || Database == null) return;
            string guildId = Config.GuildId;
            if (string.IsNullOrWhiteSpace(guildId)) return;

            var syncedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var snapshots = await MultiWorldModule.FetchAllWorldSnapshotsAsync();
                foreach (var w in snapshots)
                {
                    foreach (var pName in w.OnlinePlayerNames)
                    {
                        string accName = pName;
                        var localPlayer = TShock.Players.FirstOrDefault(p =>
                            p != null && p.Active && p.IsLoggedIn && p.Account != null &&
                            (p.Name.Equals(pName, StringComparison.OrdinalIgnoreCase) || p.Account.Name.Equals(pName, StringComparison.OrdinalIgnoreCase)));
                        if (localPlayer?.Account != null) accName = localPlayer.Account.Name;

                        if (!syncedAccounts.Add(accName)) continue;

                        var link = Database.GetByAccount(accName);
                        if (link == null || !link.IsVerified) continue;

                        try { await SyncRolesForAccountAsync(guildId, link.DiscordUserId, link.AccountName, w.Id); }
                        catch { }
                    }
                }
            }
            catch { }

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.IsLoggedIn || player.Account == null) continue;
                if (!syncedAccounts.Add(player.Account.Name)) continue;

                var link = Database.GetByAccount(player.Account.Name);
                if (link == null || !link.IsVerified) continue;

                try { await SyncRolesForAccountAsync(guildId, link.DiscordUserId, link.AccountName, MultiWorldModule.CurrentWorldId); }
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

        // useEmbed: true = kirim sebagai Embed (event penting: join/leave, boss, event dunia).
        // false = teks biasa (chat in-game). Fallback teks otomatis ada di OutboundPumpLoop kalau embed ditolak.
        private static void EnqueueRelayPost(string? worldId, string description, bool useEmbed = false, bool mentionWorldRole = false)
        {
            if (_gateway == null || !_gateway.IsConnected || string.IsNullOrWhiteSpace(Config.RelayChannelId)) return;
            if (string.IsNullOrWhiteSpace(description)) return;

            if (useEmbed)
            {
                string? mentionRoleId = null;
                if (mentionWorldRole && !string.IsNullOrWhiteSpace(worldId) &&
                    Config.WorldRoleIds.TryGetValue(worldId, out var widRole) &&
                    !string.IsNullOrWhiteSpace(widRole))
                {
                    mentionRoleId = widRole;
                    // Prepend mention agar Discord benar-benar me-mention role (bukan cuma teks).
                    description = $"<@&{widRole}> {description}";
                }
                var embed = BuildRelayEmbed(worldId, description);
                _gateway.EnqueueRelayEmbed(Config.RelayChannelId, embed, mentionRoleId);
            }
            else
            {
                _gateway.EnqueueRelayMessage(Config.RelayChannelId, description);
            }
        }

        private static JObject BuildRelayEmbed(string? worldId, string description)
        {
            string safeDesc = description.Length > 3900 ? description.Substring(0, 3900) + "..." : description;
            return new JObject
            {
                ["color"] = ResolveWorldEmbedColorInt(worldId),
                ["description"] = safeDesc
            };
        }

        private static int ResolveWorldEmbedColorInt(string? worldId)
        {
            string wid = string.IsNullOrWhiteSpace(worldId) ? "lobby" : worldId.Trim().ToLowerInvariant();

            if (_worldRoleColors.TryGetValue(wid, out int roleColor) && roleColor > 0)
            {
                return roleColor;
            }

            if (Config.WorldEmbedColors != null && Config.WorldEmbedColors.TryGetValue(wid, out string? cfgHex) &&
                TryParseHexColor(cfgHex, out int parsedCfg))
            {
                return parsedCfg;
            }

            try
            {
                var wDef = MultiWorldModule.Config?.GetWorld(wid);
                if (wDef != null && TryParseHexColor(wDef.ChatColorHex, out int parsedWorld))
                {
                    return parsedWorld;
                }
            }
            catch { }

            return wid == "survival" ? 0x22C55E : (wid == "lobby" ? 0x38BDF8 : 0x94A3B8);
        }

        private static bool TryParseHexColor(string? hex, out int colorInt)
        {
            colorInt = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string clean = hex.Trim().TrimStart('#');
            if (clean.Length != 6) return false;
            return int.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out colorInt);
        }

        private static string StripWorldAndServerTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string s = text.Trim();

            var knownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SERVER",
                "LOBBY",
                "SURVIVAL"
            };

            try
            {
                if (MultiWorldModule.Config?.Worlds != null)
                {
                    foreach (var w in MultiWorldModule.Config.Worlds)
                    {
                        if (!string.IsNullOrWhiteSpace(w.Id) && !int.TryParse(w.Id, out _)) knownTags.Add(w.Id.Trim());
                        if (!string.IsNullOrWhiteSpace(w.ChatTag) && !int.TryParse(w.ChatTag, out _)) knownTags.Add(w.ChatTag.Trim());
                        if (!string.IsNullOrWhiteSpace(w.DisplayName) && !int.TryParse(w.DisplayName, out _)) knownTags.Add(w.DisplayName.Trim());
                    }
                }
            }
            catch { }

            string joined = string.Join("|", knownTags.Select(Regex.Escape));
            string leadingPattern = $@"^(?:\*\*\[(?:{joined})\]\*\*|\[(?:{joined})\])\s*";
            for (int i = 0; i < 3; i++)
            {
                string next = Regex.Replace(s, leadingPattern, string.Empty, RegexOptions.IgnoreCase).TrimStart();
                if (next == s) break;
                s = next;
            }

            s = Regex.Replace(s, $@"\s*\(\*\*(?:{joined})\*\*\)\.?$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return s;
        }

        private static string FormatInGameChatForDiscord(string senderName, string formattedChat)
        {
            if (string.IsNullOrWhiteSpace(formattedChat)) return string.Empty;

            // Hilangkan tag warna & achievement Terraria dulu, pertahankan tag item [i:ID]
            string work = CleanTerrariaTextForDiscord(formattedChat, preserveEmojiTags: true);
            work = StripWorldAndServerTags(work);
            if (string.IsNullOrWhiteSpace(work)) return string.Empty;

            string leadingEmoji = string.Empty;
            var leadingItemMatch = Regex.Match(work, @"^\[i(?:/[^\]]*)?:(\d+)\]\s*");
            if (leadingItemMatch.Success)
            {
                if (Config.UseLevelSwordEmojisInRelay && int.TryParse(leadingItemMatch.Groups[1].Value, out int badgeItemId))
                {
                    leadingEmoji = ResolveItemEmojiTag(badgeItemId);
                }
                work = work.Substring(leadingItemMatch.Length).TrimStart();
            }

            if (string.IsNullOrEmpty(leadingEmoji) && Config.UseLevelSwordEmojisInRelay && !string.IsNullOrWhiteSpace(senderName))
            {
                int lvl = ResolvePlayerLevelByName(senderName);
                int fallbackBadgeId = ExpModule.Config?.GetBadgeIdForLevel(lvl) ?? ItemID.CopperShortsword;
                leadingEmoji = ResolveItemEmojiTag(fallbackBadgeId);
            }

            // Konversi sisa tag [i:ID] di isi pesan menjadi custom Discord emoji atau [NamaItem]
            work = Regex.Replace(work, @"\[i(?:/[^\]]*)?:(\d+)\]", m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int itemId))
                {
                    string emoji = ResolveItemEmojiTag(itemId);
                    if (!string.IsNullOrEmpty(emoji)) return emoji;
                    string itemName = Lang.GetItemNameValue(itemId);
                    return string.IsNullOrWhiteSpace(itemName) ? string.Empty : $"[{itemName}]";
                }
                return string.Empty;
            });

            work = Regex.Replace(work, @"\s{2,}", " ").Trim();
            if (string.IsNullOrWhiteSpace(work)) return string.Empty;

            string prefixEmoji = string.IsNullOrEmpty(leadingEmoji) ? string.Empty : $"{leadingEmoji} ";

            int colonIdx = work.IndexOf(':');
            if (colonIdx > 0 && colonIdx < 96)
            {
                string header = work.Substring(0, colonIdx).Replace("*", string.Empty).Trim();
                string body = work.Substring(colonIdx + 1).TrimStart();
                if (!string.IsNullOrWhiteSpace(header))
                {
                    return $"{prefixEmoji}**{header}:** {body}".TrimEnd();
                }
            }

            return $"{prefixEmoji}{work}";
        }

        private static int ResolvePlayerLevelByName(string senderName)
        {
            try
            {
                foreach (var p in TShock.Players)
                {
                    if (p != null && p.Active && p.IsLoggedIn && p.Account != null &&
                        (p.Name.Equals(senderName, StringComparison.OrdinalIgnoreCase) ||
                         p.Account.Name.Equals(senderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return ExpApi.GetLevel(p.Account.Name);
                    }
                }
                return ExpApi.GetLevel(senderName);
            }
            catch
            {
                return 1;
            }
        }

        private static string NormalizeEmojiKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return Regex.Replace(input.ToLowerInvariant(), @"[^a-z0-9]", string.Empty);
        }

        private static string ResolveItemEmojiTag(int itemId)
        {
            if (itemId <= 0) return string.Empty;

            // 1. Cek override manual di Config.CustomEmojiMap berdasarkan ID
            if (Config.CustomEmojiMap != null &&
                Config.CustomEmojiMap.TryGetValue(itemId.ToString(CultureInfo.InvariantCulture), out string? mappedById) &&
                !string.IsNullOrWhiteSpace(mappedById))
            {
                return mappedById.Trim();
            }

            // 2. Kumpulkan kandidat nama normalisasi
            var candidates = new List<string>();

            if (SwordBadgeAliases.TryGetValue(itemId, out var aliases))
            {
                candidates.AddRange(aliases);
            }

            try
            {
                if (ExpModule.Config?.BadgeTiers != null)
                {
                    foreach (var tier in ExpModule.Config.BadgeTiers.Values)
                    {
                        if (tier != null && tier.BadgeItemId == itemId && !string.IsNullOrWhiteSpace(tier.Title))
                        {
                            string titleMain = tier.Title.Split('(')[0].Trim();
                            string normTitle = NormalizeEmojiKey(titleMain);
                            if (!string.IsNullOrEmpty(normTitle)) candidates.Add(normTitle);
                        }
                    }
                }
            }
            catch { }

            string officialName = string.Empty;
            try { officialName = Lang.GetItemNameValue(itemId) ?? string.Empty; } catch { }
            string normOfficial = NormalizeEmojiKey(officialName);
            if (!string.IsNullOrEmpty(normOfficial))
            {
                candidates.Add(normOfficial);
                if (normOfficial.Contains("broadsword", StringComparison.Ordinal))
                {
                    candidates.Add(normOfficial.Replace("broadsword", "sword", StringComparison.Ordinal));
                }
                if (normOfficial.Contains("shortsword", StringComparison.Ordinal))
                {
                    candidates.Add(normOfficial.Replace("shortsword", "broadsword", StringComparison.Ordinal));
                    candidates.Add(normOfficial.Replace("shortsword", "sword", StringComparison.Ordinal));
                }
            }

            // 3. Cek override manual Config.CustomEmojiMap berdasarkan nama
            if (Config.CustomEmojiMap != null)
            {
                foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (Config.CustomEmojiMap.TryGetValue(c, out string? mapped) && !string.IsNullOrWhiteSpace(mapped))
                    {
                        return mapped.Trim();
                    }
                }
            }

            // 4. Exact match terhadap custom emoji Discord server (normalized)
            foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_guildEmojisByNormName.TryGetValue(c, out string? tag))
                {
                    return tag;
                }
            }

            // 5. Fallback substring match jika user menamai emoji mis. "univ_zenith" atau "sword_copper_broadsword"
            foreach (var c in candidates.Where(x => x.Length >= 5).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var kv in _guildEmojisByNormName)
                {
                    if (kv.Key.Contains(c, StringComparison.OrdinalIgnoreCase))
                    {
                        return kv.Value;
                    }
                }
            }

            return string.Empty;
        }

        public static string GetCachedEmojiAndRoleSummary()
        {
            string lobbyCol = $"#{ResolveWorldEmbedColorInt("lobby"):x6}";
            string survCol = $"#{ResolveWorldEmbedColorInt("survival"):x6}";
            string emojiStatus =
                $"Copper={YesNo(!string.IsNullOrEmpty(ResolveItemEmojiTag(ItemID.CopperShortsword)))}, " +
                $"NightsEdge={YesNo(!string.IsNullOrEmpty(ResolveItemEmojiTag(ItemID.NightsEdge)))}, " +
                $"TerraBlade={YesNo(!string.IsNullOrEmpty(ResolveItemEmojiTag(ItemID.TerraBlade)))}, " +
                $"Solar={YesNo(!string.IsNullOrEmpty(ResolveItemEmojiTag(ItemID.FragmentSolar)))}, " +
                $"Meowmere={YesNo(!string.IsNullOrEmpty(ResolveItemEmojiTag(ItemID.Meowmere)))}, " +
                $"Zenith={YesNo(!string.IsNullOrEmpty(ResolveItemEmojiTag(ItemID.Zenith)))}";
            return $"[ DISCORD ] Total Custom Emoji Server: {_guildEmojisExactName.Count} | Role Colors: Lobby={lobbyCol}, Survival={survCol} | Badges: {emojiStatus}";
        }

        private static string YesNo(bool ok) => ok ? "OK" : "MISSING";

        private static string CleanTerrariaTextForDiscord(string text, bool preserveEmojiTags = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // Hilangkan tag warna & achievement Terraria agar rapi di Discord
            string clean = Regex.Replace(text, @"\[c/[0-9a-fA-F]{6}:([^\]]*)\]", "$1");
            if (!preserveEmojiTags)
            {
                clean = Regex.Replace(clean, @"\[i(?:/[^\]]*)?:(\d+)\]", "");
            }
            clean = Regex.Replace(clean, @"\[a:([^\]]*)\]", "$1");
            clean = clean.Replace("@everyone", "@ everyone").Replace("@here", "@ here");
            return Regex.Replace(clean, @"\s{2,}", " ").Trim();
        }

        private static string SanitizeDiscordChatForTerraria(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string s = text.Replace("\r", " ").Replace("\n", " ").Trim();
            // Ubah custom emoji Discord <:nama:123456> menjadi :nama: agar terbaca rapi sebagai ASCII di game
            s = Regex.Replace(s, @"<a?:([a-zA-Z0-9_]+):\d+>", ":$1:");

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
            // Buang karakter non-ASCII (emoji dll) agar aman tampil di chat Terraria
            var chars = input.Where(c => c >= 32 && c <= 126).ToArray();
            string ascii = new string(chars).Trim();
            return ascii.Replace("`", "'").Replace("*", "").Replace("_", "\\_").Replace("@", "@\u200b");
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
