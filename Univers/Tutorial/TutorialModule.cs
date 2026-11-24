/*
 * =====================================================================
 *  TutorialModule.cs - Engine Tutorial, Altar Takdir & Distorsi Waktu
 *  Namespace: Univers.Tutorial (.NET 9.0 / TShock 6.x)
 *  Alur:
 *   1. Pemain baru login -> diarahkan ke Spawn Tutorial (bukan Spawn utama)
 *   2. Masuk ke Region Altar Pembangkitan -> Channeling penuh efek chaos
 *   3. Eksekusi AetherialModule.PerformAwakening -> putaran waktu sesuai rarity
 *      (Common < Uncommon/Rare < Epic < Omnia 1 Jam Realtime Bencana)
 *   4. Countdown -> Teleport ke Spawn Utama
 *   5. Player lama bisa /tutorial lagi, tapi Altar tidak bisa dipakai ulang
 *   6. Waktu dunia otomatis kembali ke waktu semula setelah distorsi selesai
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Univers.Aetherial;
using Univers.MultiWorld;

namespace Univers.Tutorial
{
    internal enum TutorialRitualStage
    {
        Channeling,
        CountdownToSpawn
    }

    internal sealed class ActiveTutorialRitual
    {
        public int PlayerIndex { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public TutorialRitualStage Stage { get; set; }
        public DateTime StageStartedUtc { get; set; }
        public DateTime StageEndsUtc { get; set; }
        public int LastSecondAnnounced { get; set; } = -1;
        public DateTime LastChaosVisualUtc { get; set; } = DateTime.MinValue;
        public string AwakenedElement { get; set; } = string.Empty;
    }

    internal sealed class WorldTimeStormState
    {
        public string TriggeredByPlayer { get; set; } = string.Empty;
        public string ElementId { get; set; } = string.Empty;
        public string RarityLabel { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "ffffff";
        public double TicksPerSecond { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public bool IsOmniaCalamity { get; set; }
        public DateTime LastOmniaMeteorUtc { get; set; } = DateTime.MinValue;
        public DateTime LastCalamityReminderUtc { get; set; } = DateTime.MinValue;
    }

    public readonly record struct TimeDistortionPreset(
        string ElementId,
        string RarityLabel,
        double TicksPerSecond,
        int DurationSeconds,
        bool IsOmniaCalamity,
        string ColorHex
    );

    public static class TutorialModule
    {
        public static TutorialConfig Config { get; private set; } = new();
        public static TutorialDatabase Database { get; private set; } = null!;

        private static string _configPath = string.Empty;
        private static bool _initialized;

        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly ConcurrentDictionary<int, byte> _routedThisSession = new();
        private static readonly ConcurrentDictionary<int, DateTime> _lastAlreadyAwakenedNoticeUtc = new();
        private static readonly ConcurrentDictionary<int, DateTime> _lastBusyNoticeUtc = new();
        private static readonly ConcurrentDictionary<int, DateTime> _lastLockNoticeUtc = new();
        private static readonly Random _rng = new();

        private static ActiveTutorialRitual? _activeRitual;
        private static WorldTimeStormState? _activeTimeStorm;

        private static bool _hasSavedOriginalTime;
        private static double _savedOriginalTime;
        private static bool _savedOriginalDayTime;
        private static int _savedOriginalMoonPhase;

        private static DateTime _lastTickUtc = DateTime.UtcNow;
        private static DateTime _lastRegionScanUtc = DateTime.MinValue;
        private static DateTime _lastTimeSyncUtc = DateTime.MinValue;

        private static readonly int[] _chaosProjectilePool =
        {
            ProjectileID.RubyBolt,
            ProjectileID.DiamondBolt,
            ProjectileID.WaterBolt,
            ProjectileID.AmethystBolt,
            ProjectileID.Leaf,
            ProjectileID.TopazBolt,
            ProjectileID.HallowStar,
            ProjectileID.SkyFracture
        };

        public static bool IsTutorialActiveInCurrentWorld()
        {
            return _initialized && Config.IsWorldActive(MultiWorldModule.CurrentWorldId);
        }

        public static bool HasActiveRitual => _activeRitual != null;
        public static bool HasActiveTimeStorm => _activeTimeStorm != null;

        public static string GetTimeStormSummary()
        {
            var storm = _activeTimeStorm;
            if (storm == null) return "Normal (Tidak ada distorsi waktu)";

            int rem = Math.Max(0, (int)Math.Ceiling((storm.EndsAtUtc - DateTime.UtcNow).TotalSeconds));
            string remText = rem >= 60 ? $"{rem / 60}m {rem % 60}d" : $"{rem}d";
            return $"{storm.ElementId} ({storm.RarityLabel}) oleh {storm.TriggeredByPlayer} - Sisa {remText}";
        }

        public static void Initialize(string savePath)
        {
            if (_initialized) return;

            string localUniversDir = Path.Combine(savePath, "Univers");
            string masterUniversDir = ResolveMasterUniversDir(localUniversDir);
            Directory.CreateDirectory(masterUniversDir);

            _configPath = Path.Combine(masterUniversDir, "TutorialConfig.json");
            Config = TutorialConfig.Load(_configPath);
            Database = new TutorialDatabase(Path.Combine(masterUniversDir, "univers_tutorial.db"));

            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnPlayerGreet);
            ServerApi.Hooks.ServerLeave.Register(UniversPlugin.Instance, OnServerLeave);
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            PlayerHooks.PlayerCommand += OnPlayerCommand;
            GetDataHandlers.PlayerSpawn += OnPlayerSpawn;

            TutorialCommands.Register();

            _lastTickUtc = DateTime.UtcNow;
            _initialized = true;

            string status = IsTutorialActiveInCurrentWorld()
                ? (Config.HasTutorialSpawnConfigured()
                    ? $"AKTIF di '{MultiWorldModule.CurrentWorldId}' (Spawn Tutorial: {Config.TutorialSpawnX},{Config.TutorialSpawnY} | Region: '{Config.AwakenRegionName}')"
                    : $"AKTIF di '{MultiWorldModule.CurrentWorldId}' (PERINGATAN: Spawn Tutorial belum di-set! Gunakan /tutorial setspawn)")
                : $"STANDBY di '{MultiWorldModule.CurrentWorldId}' (Aktif di: {string.Join(", ", Config.ActiveWorldIds)})";

            TShock.Log.ConsoleInfo($"[Univers.Tutorial] {status}");
        }

        public static void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config = TutorialConfig.Load(_configPath);
            }
            TShock.Log.ConsoleInfo("[Univers.Tutorial] Konfigurasi TutorialConfig.json dimuat ulang.");
        }

        public static void SaveConfig()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config.Save(_configPath);
            }
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            try
            {
                if (_hasSavedOriginalTime)
                {
                    Main.time = _savedOriginalTime;
                    Main.dayTime = _savedOriginalDayTime;
                    Main.moonPhase = _savedOriginalMoonPhase;
                    _hasSavedOriginalTime = false;
                }
            }
            catch { }

            _activeRitual = null;
            _activeTimeStorm = null;

            try { ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate); } catch { }
            try { ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnPlayerGreet); } catch { }
            try { ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave); } catch { }
            try { PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin; } catch { }
            try { PlayerHooks.PlayerCommand -= OnPlayerCommand; } catch { }
            try { GetDataHandlers.PlayerSpawn -= OnPlayerSpawn; } catch { }

            _routedThisSession.Clear();
            _lastAlreadyAwakenedNoticeUtc.Clear();
            _lastBusyNoticeUtc.Clear();
            _lastLockNoticeUtc.Clear();
            while (_mainThreadQueue.TryDequeue(out _)) { }

            Database?.Dispose();
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

        #region Entry Routing & Tutorial Lock

        private static void OnPlayerGreet(GreetPlayerEventArgs args)
        {
            if (!IsTutorialActiveInCurrentWorld()) return;

            int who = args.Who;
            _ = Task.Run(async () =>
            {
                await Task.Delay(700);
                _mainThreadQueue.Enqueue(() =>
                {
                    var player = TShock.Players[who];
                    if (player != null && player.Active && player.RealPlayer && player.IsLoggedIn)
                    {
                        TryRouteUnawakenedPlayer(player);
                    }
                });
            });
        }

        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs args)
        {
            if (!IsTutorialActiveInCurrentWorld()) return;
            var player = args.Player;
            if (player == null || !player.Active || !player.RealPlayer) return;

            int who = player.Index;
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                _mainThreadQueue.Enqueue(() =>
                {
                    var cur = TShock.Players[who];
                    if (cur != null && cur.Active && cur.RealPlayer && cur.IsLoggedIn)
                    {
                        TryRouteUnawakenedPlayer(cur);
                    }
                });
            });
        }

        private static void OnServerLeave(LeaveEventArgs args)
        {
            _routedThisSession.TryRemove(args.Who, out _);
            _lastAlreadyAwakenedNoticeUtc.TryRemove(args.Who, out _);
            _lastBusyNoticeUtc.TryRemove(args.Who, out _);
            _lastLockNoticeUtc.TryRemove(args.Who, out _);

            if (_activeRitual != null && _activeRitual.PlayerIndex == args.Who)
            {
                if (_activeRitual.Stage == TutorialRitualStage.Channeling)
                {
                    CancelActiveRitual(notifyPlayer: false, reason: "Pemain keluar server");
                }
                else
                {
                    _activeRitual = null;
                }
            }
        }

        private static void OnPlayerSpawn(object? sender, GetDataHandlers.SpawnEventArgs args)
        {
            if (!IsTutorialActiveInCurrentWorld()) return;
            if (!Config.LockUnawakenedPlayersInTutorial || !Config.HasTutorialSpawnConfigured()) return;

            var player = args.Player;
            if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer) return;
            if (player.HasPermission("univers.tutorial.admin")) return;

            if (ShouldStayInTutorial(player))
            {
                int who = player.Index;
                _mainThreadQueue.Enqueue(() =>
                {
                    var cur = TShock.Players[who];
                    if (cur != null && cur.Active && cur.IsLoggedIn && cur.RealPlayer && ShouldStayInTutorial(cur))
                    {
                        TeleportToTutorialSpawn(cur, showGuide: false);
                    }
                });
            }
        }

        private static void OnPlayerCommand(PlayerCommandEventArgs args)
        {
            if (args.Handled || !IsTutorialActiveInCurrentWorld()) return;
            var player = args.Player;
            if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer) return;
            if (!Config.LockUnawakenedPlayersInTutorial || !Config.HasTutorialSpawnConfigured()) return;
            if (player.HasPermission("univers.tutorial.admin")) return;

            string cmd = args.CommandName?.ToLowerInvariant() ?? string.Empty;
            bool isSpawnCmd = cmd == "spawn" || cmd == "home";
            bool isWarpCmd = Config.BlockWorldWarpBeforeAwaken &&
                             (cmd == "survival" || cmd == "surv" || cmd == "rtp" || cmd == "randomtp" ||
                              cmd == "mw" || cmd == "multiworld" || cmd == "world" || cmd == "worlds");

            if (!isSpawnCmd && !isWarpCmd) return;
            if (!ShouldStayInTutorial(player)) return;

            args.Handled = true;
            SendThrottledLockNotice(player,
                "[ TUTORIAL ] Kamu belum membangkitkan elemen jiwamu! Selesaikan Tutorial dan masuklah ke Altar Pembangkitan Elemen terlebih dahulu.");
            if (isSpawnCmd)
            {
                TeleportToTutorialSpawn(player, showGuide: false);
            }
        }

        private static void TryRouteUnawakenedPlayer(TSPlayer player)
        {
            if (!Config.TeleportNewPlayersOnLogin || !Config.HasTutorialSpawnConfigured()) return;
            if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer || player.Account == null) return;

            if (!_routedThisSession.TryAdd(player.Index, 1))
            {
                return;
            }

            if (!ShouldStayInTutorial(player))
            {
                return;
            }

            var tutProfile = Database.GetOrCreateProfile(player.Account.Name);
            tutProfile.TutorialVisits++;
            Database.SaveProfile(tutProfile);

            TeleportToTutorialSpawn(player, showGuide: true);
        }

        public static bool ShouldStayInTutorial(TSPlayer player)
        {
            if (player == null || !player.IsLoggedIn || player.Account == null) return false;

            var tutProfile = Database.GetOrCreateProfile(player.Account.Name);
            if (tutProfile.HasCompletedTutorial)
            {
                return false;
            }

            var elemProfile = AetherialModule.Database.GetOrCreateProfile(player.Account.Name);
            if (elemProfile != null && !string.IsNullOrEmpty(elemProfile.AssignedElement))
            {
                if (Config.AutoMarkPlayersWhoAlreadyHaveElement)
                {
                    tutProfile.HasCompletedTutorial = true;
                    tutProfile.CompletedTutorialUtc = DateTime.UtcNow;
                    tutProfile.AwakenedElementSnapshot = elemProfile.AssignedElement;
                    Database.SaveProfile(tutProfile);
                    return false;
                }
            }

            return true;
        }

        public static bool TeleportToTutorialSpawn(TSPlayer player, bool showGuide)
        {
            if (player == null || !player.Active || !Config.HasTutorialSpawnConfigured()) return false;

            TeleportPlayerSafely(player, Config.TutorialSpawnX, Config.TutorialSpawnY);

            if (showGuide)
            {
                player.SendMessage("=== [ SELAMAT DATANG DI TUTORIAL UNIVERS ] ===", Color.Gold);
                player.SendMessage("[ TUTORIAL ] Kamu baru tiba di Tanah Awal. Ikuti jalur tutorial hingga ujung untuk menemukan Altar Pembangkitan Elemen!", new Color(125, 211, 252));
                player.SendMessage("[ TUTORIAL ] Berdirilah di dalam Region Altar tersebut untuk menguji takdir elemenmu dan membuka gerbang menuju dunia utama.", new Color(167, 243, 208));
            }

            return true;
        }

        public static void TeleportToMainSpawn(TSPlayer player)
        {
            if (player == null || !player.Active) return;
            TeleportPlayerSafely(player, Main.spawnTileX, Main.spawnTileY);
        }

        private static void TeleportPlayerSafely(TSPlayer player, int rawTileX, int rawTileY)
        {
            if (player == null || !player.Active || player.TPlayer == null) return;

            int tx = Math.Clamp(rawTileX, 15, Math.Max(16, Main.maxTilesX - 15));
            int ty = Math.Clamp(rawTileY, 15, Math.Max(16, Main.maxTilesY - 15));

            while (ty > 20 && Main.tile[tx, ty - 1] != null &&
                   Main.tile[tx, ty - 1].active() && Main.tileSolid[Main.tile[tx, ty - 1].type])
            {
                ty--;
            }

            try
            {
                int maxSecX = Math.Max(1, Main.maxTilesX / 200);
                int maxSecY = Math.Max(1, Main.maxTilesY / 150);
                int centerSx = Math.Clamp(tx / 200, 0, maxSecX - 1);
                int centerSy = Math.Clamp(ty / 150, 0, maxSecY - 1);
                int minSx = Math.Max(0, centerSx - 1);
                int maxSx = Math.Min(maxSecX - 1, centerSx + 1);
                int minSy = Math.Max(0, centerSy - 1);
                int maxSy = Math.Min(maxSecY - 1, centerSy + 1);

                for (int sx = minSx; sx <= maxSx; sx++)
                {
                    for (int sy = minSy; sy <= maxSy; sy++)
                    {
                        NetMessage.SendSection(player.Index, sx, sy);
                    }
                }
                NetMessage.SendData((int)PacketTypes.TileFrameSection, player.Index, -1, null, minSx, minSy, maxSx, maxSy);
            }
            catch { }

            float px = tx * 16f;
            float py = Math.Max(10, ty - 3) * 16f;

            player.TPlayer.velocity = Vector2.Zero;
            player.TPlayer.position = new Vector2(px, py);
            player.Teleport(px, py);
        }

        #endregion

        #region Main Loop, Awakening Ritual & World Time Distortion

        private static void OnGameUpdate(EventArgs args)
        {
            int processed = 0;
            while (processed < 24 && _mainThreadQueue.TryDequeue(out var act))
            {
                processed++;
                try { act(); } catch { }
            }

            if (!IsTutorialActiveInCurrentWorld()) return;

            DateTime now = DateTime.UtcNow;
            double dtSeconds = Math.Clamp((now - _lastTickUtc).TotalSeconds, 0.0, 0.25);
            _lastTickUtc = now;

            if (_activeRitual != null)
            {
                UpdateActiveRitual(now, dtSeconds);
            }
            else if ((now - _lastRegionScanUtc).TotalMilliseconds >= 300)
            {
                _lastRegionScanUtc = now;
                ScanPlayersEnteringAwakenRegion(now);
            }

            if (_activeTimeStorm != null)
            {
                UpdateWorldTimeStorm(now, dtSeconds);
            }
            else if (_activeRitual != null && _activeRitual.Stage == TutorialRitualStage.Channeling)
            {
                // Efek waktu dunia berputar cepat selama fase persiapan di layar pemain / dunia
                UpdateChannelingTimeTremor(now, dtSeconds);
            }
        }

        private static void ScanPlayersEnteringAwakenRegion(DateTime now)
        {
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || !p.IsLoggedIn || !p.RealPlayer || p.Dead || p.Account == null)
                {
                    continue;
                }

                if (!IsInsideAwakenArea(p.TileX, p.TileY, graceMarginTiles: 0))
                {
                    continue;
                }

                var elemProfile = AetherialModule.Database.GetOrCreateProfile(p.Account.Name);
                if (elemProfile != null && !string.IsNullOrEmpty(elemProfile.AssignedElement))
                {
                    if (!_lastAlreadyAwakenedNoticeUtc.TryGetValue(p.Index, out DateTime lastNotif) ||
                        (now - lastNotif).TotalSeconds >= 10)
                    {
                        _lastAlreadyAwakenedNoticeUtc[p.Index] = now;
                        p.SendMessage($"[ TUTORIAL ] Altar Pembangkitan Elemen tetap hening... Elemen '{elemProfile.AssignedElement}' sudah menyatu permanen di jiwamu dan tidak bisa dibangkitkan ulang.", new Color(148, 163, 184));
                    }
                    continue;
                }

                StartAwakeningRitual(p, now);
                break;
            }
        }

        private static void StartAwakeningRitual(TSPlayer player, DateTime now)
        {
            CaptureOriginalTimeIfNeeded();

            _activeRitual = new ActiveTutorialRitual
            {
                PlayerIndex = player.Index,
                AccountName = player.Account.Name,
                PlayerName = player.Name,
                Stage = TutorialRitualStage.Channeling,
                StageStartedUtc = now,
                StageEndsUtc = now.AddSeconds(Config.ChannelingSeconds),
                LastSecondAnnounced = -1,
                LastChaosVisualUtc = DateTime.MinValue
            };

            player.SendMessage("=== [ ALTAR PEMBANGKITAN ELEMEN TERPICU ] ===", Color.Gold);
            player.SendMessage("[ TAKDIR ] Energi purba berpusar! Tetaplah berada di dalam Altar sementara dimensi waktu berputar menguji jiwamu...", new Color(125, 211, 252));

            if (Config.BroadcastTimeDistortion)
            {
                TShock.Utils.Broadcast($"[ TUTORIAL ] {player.Name} melangkah ke Altar Pembangkitan Elemen! Langit dunia bergetar merasakan ujian takdir...", 125, 211, 252);
            }
        }

        private static void UpdateActiveRitual(DateTime now, double dtSeconds)
        {
            var ritual = _activeRitual;
            if (ritual == null) return;

            var player = TShock.Players[ritual.PlayerIndex];
            if (player == null || !player.Active || !player.IsLoggedIn || !player.RealPlayer || player.TPlayer == null)
            {
                if (ritual.Stage == TutorialRitualStage.Channeling)
                {
                    CancelActiveRitual(notifyPlayer: false, reason: "Player terputus");
                }
                else
                {
                    _activeRitual = null;
                }
                return;
            }

            if (ritual.Stage == TutorialRitualStage.Channeling)
            {
                // Beri tahu player lain jika ada yang mencoba ikut masuk altar saat ritual berlangsung
                if ((now - _lastRegionScanUtc).TotalMilliseconds >= 600)
                {
                    _lastRegionScanUtc = now;
                    NotifyOtherPlayersIfWaitingInAltar(player.Index, ritual.PlayerName, now);
                }

                if (!IsInsideAwakenArea(player.TileX, player.TileY, graceMarginTiles: 2))
                {
                    CancelActiveRitual(notifyPlayer: true, reason: "Kamu keluar dari region Altar Pembangkitan sebelum ritual selesai! Masuk kembali untuk mengulang.");
                    return;
                }

                double totalSec = Math.Max(1.0, (ritual.StageEndsUtc - ritual.StageStartedUtc).TotalSeconds);
                double elapsedSec = Math.Max(0.0, (now - ritual.StageStartedUtc).TotalSeconds);
                double progress = Math.Clamp(elapsedSec / totalSec, 0.0, 1.0);
                int secRemaining = Math.Max(0, (int)Math.Ceiling((ritual.StageEndsUtc - now).TotalSeconds));

                if (Config.ShowChaosVisualEffects && (now - ritual.LastChaosVisualUtc).TotalMilliseconds >= 380)
                {
                    ritual.LastChaosVisualUtc = now;
                    SpawnChaosChannelingBurst(player.TPlayer.Center, progress);
                }

                if (secRemaining != ritual.LastSecondAnnounced)
                {
                    ritual.LastSecondAnnounced = secRemaining;
                    int pct = Math.Clamp((int)Math.Round(progress * 100.0), 0, 100);
                    string bar = BuildAsciiBar(pct, 100, 14);
                    SendPlayerStatusHud(player, $"\n\n\n\n\n[ PEMBANGKITAN ELEMEN ] [{bar}] {pct}%  |  Bertahan di Altar ({secRemaining}s)");

                    if (secRemaining > 0)
                    {
                        player.SendData(
                            PacketTypes.CreateCombatTextExtended,
                            $"[ RESONANSI {pct}% ]",
                            (int)Color.Cyan.PackedValue,
                            player.X,
                            player.Y - 36f);
                    }
                }

                if (now >= ritual.StageEndsUtc)
                {
                    ExecuteRitualAwakening(player, ritual, now);
                }
                return;
            }

            if (ritual.Stage == TutorialRitualStage.CountdownToSpawn)
            {
                int secRemaining = Math.Max(0, (int)Math.Ceiling((ritual.StageEndsUtc - now).TotalSeconds));

                if (Config.ShowChaosVisualEffects && (now - ritual.LastChaosVisualUtc).TotalMilliseconds >= 650)
                {
                    ritual.LastChaosVisualUtc = now;
                    SpawnElementSignatureBurst(player.TPlayer.Center, ritual.AwakenedElement, intense: false);
                }

                if (secRemaining != ritual.LastSecondAnnounced)
                {
                    ritual.LastSecondAnnounced = secRemaining;
                    if (secRemaining > 0)
                    {
                        SendPlayerStatusHud(player, $"\n\n\n\n\n[ TAKDIR: {ritual.AwakenedElement} ] Portal ke Spawn terbuka dalam {secRemaining} detik...");
                        player.SendData(
                            PacketTypes.CreateCombatTextExtended,
                            $"[ SPAWN DALAM {secRemaining} ]",
                            (int)Color.Gold.PackedValue,
                            player.X,
                            player.Y - 36f);
                        player.SendInfoMessage($"[ TUTORIAL ] Mengirimmu ke Spawn Utama dalam {secRemaining} detik...");
                    }
                }

                if (now >= ritual.StageEndsUtc)
                {
                    ClearPlayerStatusHud(player);
                    TeleportToMainSpawn(player);
                    player.SendSuccessMessage($"[ TUTORIAL ] Selamat datang di Spawn Utama, {player.Name}! Petualanganmu di Univers resmi dimulai.");
                    player.SendInfoMessage("[ INFO ] Kamu tetap bisa berkunjung ke area tutorial kapan saja dengan perintah /tutorial.");
                    _activeRitual = null;
                }
            }
        }

        private static void ExecuteRitualAwakening(TSPlayer player, ActiveTutorialRitual ritual, DateTime now)
        {
            var existingElem = AetherialModule.Database.GetOrCreateProfile(player.Account.Name);
            string awakenedElement;

            if (existingElem != null && !string.IsNullOrEmpty(existingElem.AssignedElement))
            {
                awakenedElement = existingElem.AssignedElement.ToUpperInvariant();
            }
            else
            {
                awakenedElement = AetherialModule.PerformAwakening(player).ToUpperInvariant();
            }

            var tutProfile = Database.GetOrCreateProfile(player.Account.Name);
            tutProfile.HasCompletedTutorial = true;
            tutProfile.CompletedTutorialUtc = now;
            tutProfile.AwakenedElementSnapshot = awakenedElement;
            Database.SaveProfile(tutProfile);

            if (Config.ShowChaosVisualEffects && player.TPlayer != null)
            {
                SpawnElementSignatureBurst(player.TPlayer.Center, awakenedElement, intense: true);
            }

            var preset = GetTimeDistortionPreset(awakenedElement);
            StartOrUpgradeWorldTimeStorm(player.Name, preset, now);

            ritual.AwakenedElement = awakenedElement;
            ritual.Stage = TutorialRitualStage.CountdownToSpawn;
            ritual.StageStartedUtc = now;
            ritual.StageEndsUtc = now.AddSeconds(Config.PostAwakenCountdownSeconds);
            ritual.LastSecondAnnounced = -1;

            player.SendData(
                PacketTypes.CreateCombatTextExtended,
                $"[ {awakenedElement} BANGKIT! ]",
                (int)Color.Gold.PackedValue,
                player.X,
                player.Y - 46f);
        }

        private static void CancelActiveRitual(bool notifyPlayer, string reason)
        {
            var ritual = _activeRitual;
            _activeRitual = null;

            if (ritual != null && notifyPlayer)
            {
                var player = TShock.Players[ritual.PlayerIndex];
                if (player != null && player.Active && player.RealPlayer)
                {
                    ClearPlayerStatusHud(player);
                    player.SendWarningMessage($"[ TUTORIAL ] {reason}");
                    player.SendData(
                        PacketTypes.CreateCombatTextExtended,
                        "[ RITUAL BATAL ]",
                        (int)Color.OrangeRed.PackedValue,
                        player.X,
                        player.Y - 32f);
                }
            }

            if (_activeTimeStorm == null && _hasSavedOriginalTime)
            {
                RestoreOriginalWorldTime(announce: false, reason: reason);
            }
        }

        private static void NotifyOtherPlayersIfWaitingInAltar(int activePlayerIndex, string activePlayerName, DateTime now)
        {
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || !p.IsLoggedIn || !p.RealPlayer || p.Index == activePlayerIndex)
                {
                    continue;
                }

                if (!IsInsideAwakenArea(p.TileX, p.TileY, graceMarginTiles: 0)) continue;

                if (!_lastBusyNoticeUtc.TryGetValue(p.Index, out DateTime last) || (now - last).TotalSeconds >= 4)
                {
                    _lastBusyNoticeUtc[p.Index] = now;
                    p.SendInfoMessage($"[ TUTORIAL ] Altar Pembangkitan sedang digunakan oleh {activePlayerName}. Tunggu sebentar hingga ritualnya selesai.");
                }
            }
        }

        #endregion

        #region World Time Distortion Engine

        public static TimeDistortionPreset GetTimeDistortionPreset(string elementId)
        {
            string elem = (elementId ?? "IGNIS").Trim().ToUpperInvariant();
            return elem switch
            {
                "OMNIA" => new TimeDistortionPreset(
                    "OMNIA",
                    "SECRET COSMIC CALAMITY",
                    Config.OmniaTimeTicksPerSecond,
                    Config.OmniaTimeDurationSeconds,
                    true,
                    "e879f9"),

                "RADIANCE" => new TimeDistortionPreset(
                    "RADIANCE",
                    "EPIC",
                    Config.EpicTimeTicksPerSecond,
                    Config.EpicTimeDurationSeconds,
                    false,
                    "facc15"),

                "GLACIAL" or "VOLT" => new TimeDistortionPreset(
                    elem,
                    "UNCOMMON / RARE",
                    Config.UncommonTimeTicksPerSecond,
                    Config.UncommonTimeDurationSeconds,
                    false,
                    "38bdf8"),

                _ => new TimeDistortionPreset(
                    elem,
                    "COMMON",
                    Config.CommonTimeTicksPerSecond,
                    Config.CommonTimeDurationSeconds,
                    false,
                    "4ade80")
            };
        }

        public static void StartOrUpgradeWorldTimeStorm(string playerName, TimeDistortionPreset preset, DateTime now)
        {
            CaptureOriginalTimeIfNeeded();

            DateTime newEnds = now.AddSeconds(Math.Max(3, preset.DurationSeconds));

            if (_activeTimeStorm == null)
            {
                _activeTimeStorm = new WorldTimeStormState
                {
                    TriggeredByPlayer = playerName,
                    ElementId = preset.ElementId,
                    RarityLabel = preset.RarityLabel,
                    ColorHex = preset.ColorHex,
                    TicksPerSecond = preset.TicksPerSecond,
                    StartedUtc = now,
                    EndsAtUtc = newEnds,
                    IsOmniaCalamity = preset.IsOmniaCalamity
                };
            }
            else
            {
                // Jangan timpa snapshot waktu asli! Upgrade kecepatan & durasi jika lebih dahsyat.
                if (preset.TicksPerSecond >= _activeTimeStorm.TicksPerSecond)
                {
                    _activeTimeStorm.TicksPerSecond = preset.TicksPerSecond;
                    _activeTimeStorm.ElementId = preset.ElementId;
                    _activeTimeStorm.RarityLabel = preset.RarityLabel;
                    _activeTimeStorm.ColorHex = preset.ColorHex;
                    _activeTimeStorm.TriggeredByPlayer = playerName;
                }
                if (newEnds > _activeTimeStorm.EndsAtUtc)
                {
                    _activeTimeStorm.EndsAtUtc = newEnds;
                }
                if (preset.IsOmniaCalamity)
                {
                    _activeTimeStorm.IsOmniaCalamity = true;
                }
            }

            if (Config.BroadcastTimeDistortion)
            {
                if (preset.IsOmniaCalamity)
                {
                    TShock.Utils.Broadcast($"[c/e879f9:[ BENCANA KOSMIS - OMNIA ]] Langit dunia terbelah! {playerName} membangkitkan OMNIA - waktu dunia berputar brutal selama 1 JAM REALTIME!", 232, 121, 249);
                }
                else
                {
                    TShock.Utils.Broadcast($"[c/{preset.ColorHex}:[ DISTORSI WAKTU - {preset.RarityLabel} ]] Kebangkitan elemen {preset.ElementId} milik {playerName} memutar waktu dunia selama {preset.DurationSeconds} detik!", 250, 204, 21);
                }
            }
        }

        public static bool ForceStopTimeStorm(bool announce)
        {
            if (_activeTimeStorm == null && !_hasSavedOriginalTime)
            {
                return false;
            }

            RestoreOriginalWorldTime(announce, "Dihentikan paksa");
            return true;
        }

        private static void CaptureOriginalTimeIfNeeded()
        {
            if (_hasSavedOriginalTime) return;

            _savedOriginalTime = Main.time;
            _savedOriginalDayTime = Main.dayTime;
            _savedOriginalMoonPhase = Main.moonPhase;
            _hasSavedOriginalTime = true;
        }

        private static void UpdateChannelingTimeTremor(DateTime now, double dtSeconds)
        {
            var ritual = _activeRitual;
            if (ritual == null) return;

            double totalSec = Math.Max(1.0, (ritual.StageEndsUtc - ritual.StageStartedUtc).TotalSeconds);
            double progress = Math.Clamp((now - ritual.StageStartedUtc).TotalSeconds / totalSec, 0.0, 1.0);
            double speed = Config.ChannelingStartTicksPerSecond +
                           (Config.ChannelingMaxTicksPerSecond - Config.ChannelingStartTicksPerSecond) * progress;

            AdvanceTerrariaTime(speed * dtSeconds);

            if ((now - _lastTimeSyncUtc).TotalMilliseconds >= 100)
            {
                _lastTimeSyncUtc = now;
                try
                {
                    NetMessage.SendData((int)PacketTypes.TimeSet, -1, -1, null);
                }
                catch { }
            }
        }

        private static void UpdateWorldTimeStorm(DateTime now, double dtSeconds)
        {
            var storm = _activeTimeStorm;
            if (storm == null) return;

            if (now >= storm.EndsAtUtc)
            {
                RestoreOriginalWorldTime(announce: Config.BroadcastTimeDistortion, reason: "Durasi selesai");
                return;
            }

            AdvanceTerrariaTime(storm.TicksPerSecond * dtSeconds);

            if ((now - _lastTimeSyncUtc).TotalMilliseconds >= 100)
            {
                _lastTimeSyncUtc = now;
                try
                {
                    NetMessage.SendData((int)PacketTypes.TimeSet, -1, -1, null);
                }
                catch { }
            }

            // Efek bencana langit khusus jika ada pemain mendapat OMNIA (1 jam realtime)
            if (storm.IsOmniaCalamity)
            {
                if (Config.ShowChaosVisualEffects && (now - storm.LastOmniaMeteorUtc).TotalSeconds >= 5)
                {
                    storm.LastOmniaMeteorUtc = now;
                    EmitOmniaCalamitySkyVisuals();
                }

                if (Config.BroadcastTimeDistortion && (now - storm.LastCalamityReminderUtc).TotalMinutes >= 10)
                {
                    storm.LastCalamityReminderUtc = now;
                    int minsLeft = Math.Max(1, (int)Math.Ceiling((storm.EndsAtUtc - now).TotalMinutes));
                    TShock.Utils.Broadcast($"[c/e879f9:[ BENCANA OMNIA ]] Badai waktu kosmis akibat kebangkitan {storm.TriggeredByPlayer} masih berlangsung ({minsLeft} menit tersisa).", 232, 121, 249);
                }
            }
        }

        private static void RestoreOriginalWorldTime(bool announce, string reason)
        {
            _activeTimeStorm = null;

            if (_hasSavedOriginalTime)
            {
                Main.time = _savedOriginalTime;
                Main.dayTime = _savedOriginalDayTime;
                Main.moonPhase = _savedOriginalMoonPhase;
                _hasSavedOriginalTime = false;
            }

            try
            {
                NetMessage.SendData((int)PacketTypes.WorldInfo);
                NetMessage.SendData((int)PacketTypes.TimeSet);
            }
            catch { }

            if (announce)
            {
                TShock.Utils.Broadcast("[ TUTORIAL ] Badai waktu telah mereda. Waktu dunia kembali ke posisi semula sebelum kebangkitan elemen.", 125, 211, 252);
            }
        }

        private static void AdvanceTerrariaTime(double ticksToAdd)
        {
            if (ticksToAdd <= 0) return;

            double remaining = ticksToAdd;
            int safety = 0;
            while (remaining > 0 && safety++ < 32)
            {
                double limit = Main.dayTime ? Main.dayLength : Main.nightLength;
                if (limit <= 100) limit = 54000.0;

                double space = Math.Max(0.0, limit - Main.time);
                if (remaining < space)
                {
                    Main.time += remaining;
                    remaining = 0;
                }
                else
                {
                    remaining -= space;
                    Main.time = 0.0;
                    Main.dayTime = !Main.dayTime;
                    if (!Main.dayTime)
                    {
                        Main.moonPhase = (Main.moonPhase + 1) % 8;
                    }
                }
            }
        }

        #endregion

        #region Region & Visual Helpers

        public static bool IsInsideAwakenArea(int tileX, int tileY, int graceMarginTiles)
        {
            if (TShock.Regions?.Regions != null && !string.IsNullOrWhiteSpace(Config.AwakenRegionName))
            {
                foreach (var region in TShock.Regions.Regions)
                {
                    if (region == null) continue;
                    if (!region.Name.Equals(Config.AwakenRegionName, StringComparison.OrdinalIgnoreCase)) continue;

                    if (graceMarginTiles <= 0)
                    {
                        if (region.InArea(tileX, tileY)) return true;
                    }
                    else
                    {
                        var area = region.Area;
                        if (tileX >= area.Left - graceMarginTiles && tileX <= area.Right + graceMarginTiles &&
                            tileY >= area.Top - graceMarginTiles && tileY <= area.Bottom + graceMarginTiles)
                        {
                            return true;
                        }
                    }
                }
            }

            if (Config.HasDirectAltarConfigured())
            {
                int r = Config.AltarRadiusTiles + Math.Max(0, graceMarginTiles);
                if (Math.Abs(tileX - Config.AltarCenterX) <= r &&
                    Math.Abs(tileY - Config.AltarCenterY) <= r)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SpawnChaosChannelingBurst(Vector2 center, double progress)
        {
            int projCount = progress >= 0.65 ? 6 : 4;
            float radius = 34f + (float)(progress * 36.0);
            float outSpeed = 2.5f + (float)(progress * 3.0);

            for (int i = 0; i < projCount; i++)
            {
                double angle = (_rng.NextDouble() * Math.PI * 2.0);
                Vector2 offset = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                Vector2 vel = new Vector2((float)Math.Cos(angle) * outSpeed, (float)Math.Sin(angle) * outSpeed);
                int projId = _chaosProjectilePool[_rng.Next(_chaosProjectilePool.Length)];
                SpawnSafeVisualProjectile(center + offset, vel, projId, 24);
            }

            if (progress >= 0.45)
            {
                float spreadX = (float)((_rng.NextDouble() - 0.5) * 140.0);
                Vector2 skyPos = new Vector2(center.X + spreadX, center.Y - 150f);
                SpawnSafeVisualProjectile(skyPos, new Vector2(0f, 8.5f), ProjectileID.HallowStar, 24);
            }
        }

        private static void SpawnElementSignatureBurst(Vector2 center, string elementId, bool intense)
        {
            int projId = elementId switch
            {
                "IGNIS" => ProjectileID.RubyBolt,
                "GLACIAL" => ProjectileID.IceBolt,
                "TORRENT" => ProjectileID.Typhoon,
                "VOLT" => ProjectileID.Electrosphere,
                "BRAMBLE" => ProjectileID.TerraBeam,
                "RADIANCE" => ProjectileID.HallowStar,
                "OMNIA" => ProjectileID.FinalFractal,
                _ => ProjectileID.HallowStar
            };

            int ringCount = intense ? 8 : 4;
            float radius = intense ? 48f : 34f;
            float speed = intense ? 4.2f : 2.2f;

            for (int i = 0; i < ringCount; i++)
            {
                double angle = (Math.PI * 2.0 / ringCount) * i;
                Vector2 offset = new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                Vector2 vel = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed);
                SpawnSafeVisualProjectile(center + offset, vel, projId, 28);
            }

            if (intense)
            {
                int skyProj = elementId == "OMNIA" ? ProjectileID.LunarFlare : ProjectileID.HallowStar;
                for (int i = -2; i <= 2; i++)
                {
                    Vector2 skyPos = new Vector2(center.X + i * 36f, center.Y - 170f);
                    SpawnSafeVisualProjectile(skyPos, new Vector2(0f, 10f), skyProj, 28);
                }

                if (elementId == "OMNIA")
                {
                    SpawnSafeVisualProjectile(center, Vector2.Zero, ProjectileID.RainbowCrystalExplosion, 30);
                }
            }
        }

        private static void EmitOmniaCalamitySkyVisuals()
        {
            var online = TShock.Players.Where(p => p != null && p.Active && p.IsLoggedIn && p.RealPlayer && p.TPlayer != null).ToList();
            if (online.Count == 0) return;

            var chosen = online[_rng.Next(online.Count)];
            Vector2 center = chosen.TPlayer.Center;

            for (int i = 0; i < 3; i++)
            {
                float offsetX = (float)((_rng.NextDouble() - 0.5) * 420.0);
                int proj = i == 0 ? ProjectileID.FinalFractal : (i == 1 ? ProjectileID.StarWrath : ProjectileID.LunarFlare);
                Vector2 startPos = new Vector2(center.X + offsetX, center.Y - 260f);
                SpawnSafeVisualProjectile(startPos, new Vector2((float)((_rng.NextDouble() - 0.5) * 2.5), 11f), proj, 34);
            }
        }

        private static void SpawnSafeVisualProjectile(Vector2 pos, Vector2 vel, int projId, int timeLeft)
        {
            int idx = Projectile.NewProjectile(Projectile.GetNoneSource(), pos, vel, projId, 0, 0f, Main.myPlayer);
            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                var proj = Main.projectile[idx];
                proj.friendly = true;
                proj.hostile = false;
                proj.damage = 0;
                proj.knockBack = 0f;
                proj.direction = vel.X >= 0 ? 1 : -1;
                proj.spriteDirection = proj.direction;
                proj.timeLeft = timeLeft;
                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }

        private static void SendPlayerStatusHud(TSPlayer player, string text)
        {
            if (player == null || !player.Active) return;
            try
            {
                NetMessage.SendData((int)PacketTypes.Status, player.Index, -1, NetworkText.FromLiteral(text), 1, 1f);
            }
            catch { }
        }

        private static void ClearPlayerStatusHud(TSPlayer player)
        {
            if (player == null || !player.Active) return;
            try
            {
                NetMessage.SendData((int)PacketTypes.Status, player.Index, -1, NetworkText.FromLiteral(""), 0, 0f);
            }
            catch { }
        }

        private static void SendThrottledLockNotice(TSPlayer player, string message)
        {
            DateTime now = DateTime.UtcNow;
            if (_lastLockNoticeUtc.TryGetValue(player.Index, out DateTime last) && (now - last).TotalSeconds < 2)
            {
                return;
            }
            _lastLockNoticeUtc[player.Index] = now;
            player.SendWarningMessage(message);
        }

        private static string BuildAsciiBar(double current, double max, int width)
        {
            double ratio = max <= 0 ? 0.0 : Math.Clamp(current / max, 0.0, 1.0);
            int filled = (int)Math.Round(ratio * width);
            return new string('#', filled) + new string('-', Math.Max(0, width - filled));
        }

        #endregion
    }
}
