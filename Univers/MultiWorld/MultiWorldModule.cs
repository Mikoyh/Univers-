/*
 * =====================================================================
 *  MultiWorldModule.cs - Koordinator MultiWorld (Master Hub & Child World)
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 *  Mendukung Gateway Proxy, Auto-Sleep, Auto-Auth, Cross-World Chat & Roster
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;
using Univers.Discord;

namespace Univers.MultiWorld
{
    public static class MultiWorldModule
    {
        public static bool IsChildProcess { get; private set; }
        public static string CurrentWorldId { get; private set; } = "lobby";
        public static int MasterIpcPort { get; private set; } = 7775;

        public static MultiWorldConfig Config { get; private set; } = new();
        public static MultiWorldTicketStore TicketStore { get; private set; } = null!;
        public static MultiWorldSscStore? SscStore { get; private set; }
        public static MultiWorldProcessManager? ProcessManager { get; private set; }
        public static MultiWorldIpcServer? IpcServer { get; private set; }
        public static MultiWorldIpcServer? ChildPushServer { get; private set; }
        public static MultiWorldProxy? Proxy { get; private set; }

        public static bool IsCrossWorldChatActive => _initialized && Config.Enabled && Config.CrossWorldChat;

        private static string _configPath = string.Empty;
        private static string _multiWorldRoot = string.Empty;
        private static System.Timers.Timer? _heartbeatTimer;
        private static bool _initialized = false;
        private static bool _processExitHooked = false;
        private static int _effectiveMasterHubPort = 0;
        private static int _takenOverPublicPort = 0;

        [ThreadStatic]
        private static bool _suppressRemoteRouting;

        private sealed class TrackedSlotPlayer
        {
            public TSPlayer Player { get; init; } = null!;
            public string PlayerName { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public int AccountId { get; set; }
            public bool WasLoggedIn { get; set; }
        }

        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly ConcurrentDictionary<string, byte> _pendingEntityClearPlayers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _recentTransfers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (string SourceWorldId, DateTime ExpiresUtc)> _recentTransferSources = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (WorldTransferTicket Ticket, DateTime ExpiresUtc)> _claimedTickets = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _transferLockedPlayers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _pendingSscRestores = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _sscRestoreGuardUntil = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _playerJoinedUtc = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _lastSavedSscJson = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _lastMasterSyncedSscJson = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _inFlightMasterSyncs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<int, byte> _checkedPreGreetTicket = new();
        private static readonly ConcurrentDictionary<int, byte> _dirtySscSlots = new();
        private static readonly ConcurrentDictionary<int, byte> _preSyncedJoinSlots = new();
        private static readonly ConcurrentDictionary<int, TrackedSlotPlayer> _trackedSlotPlayers = new();
        private static Terraria.Player? _scratchSyncPlayer;

        private static void TrackSlotPlayer(TSPlayer? player)
        {
            if (player == null || player.Index < 0 || player.Index >= 255) return;
            if (string.IsNullOrWhiteSpace(player.Name) && player.Account == null) return;

            _trackedSlotPlayers.AddOrUpdate(
                player.Index,
                _ => new TrackedSlotPlayer
                {
                    Player = player,
                    PlayerName = player.Name ?? string.Empty,
                    AccountName = player.Account?.Name ?? string.Empty,
                    AccountId = player.Account?.ID ?? 0,
                    WasLoggedIn = player.IsLoggedIn && player.Account != null
                },
                (_, existing) =>
                {
                    if (!ReferenceEquals(existing.Player, player))
                    {
                        return new TrackedSlotPlayer
                        {
                            Player = player,
                            PlayerName = player.Name ?? string.Empty,
                            AccountName = player.Account?.Name ?? string.Empty,
                            AccountId = player.Account?.ID ?? 0,
                            WasLoggedIn = player.IsLoggedIn && player.Account != null
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(player.Name))
                    {
                        existing.PlayerName = player.Name;
                    }
                    if (player.Account != null)
                    {
                        existing.AccountName = player.Account.Name;
                        if (player.Account.ID > 0) existing.AccountId = player.Account.ID;
                    }
                    if (player.IsLoggedIn && player.Account != null)
                    {
                        existing.WasLoggedIn = true;
                    }
                    return existing;
                });
        }

        public static void SetTransferLock(string playerName, int seconds)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return;
            _transferLockedPlayers[playerName] = DateTime.UtcNow.AddSeconds(seconds);
        }

        public static void ClearTransferLock(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return;
            _transferLockedPlayers.TryRemove(playerName, out _);
        }

        public static bool IsTransferLocked(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return false;
            if (_transferLockedPlayers.TryGetValue(playerName, out var until))
            {
                if (DateTime.UtcNow <= until) return true;
                _transferLockedPlayers.TryRemove(playerName, out _);
            }
            return false;
        }

        public static void Initialize(string tshockSavePath)
        {
            if (_initialized) return;

            string? childEnv = Environment.GetEnvironmentVariable("UNIVERS_CHILD_WORLD");
            IsChildProcess = !string.IsNullOrWhiteSpace(childEnv);

            string universDir = Path.Combine(tshockSavePath, "Univers");
            Directory.CreateDirectory(universDir);

            string masterUniversDir = ResolveMasterUniversDir(universDir);
            _multiWorldRoot = Path.Combine(masterUniversDir, "MultiWorld");
            Directory.CreateDirectory(_multiWorldRoot);

            _configPath = Path.Combine(masterUniversDir, "MultiWorldConfig.json");
            Config = MultiWorldConfig.Load(_configPath);

            if (!Config.Enabled)
            {
                TShock.Log.ConsoleInfo("[Univers.MultiWorld] Modul MultiWorld dinonaktifkan di MultiWorldConfig.json.");
                return;
            }

            string ticketDbPath = Path.Combine(masterUniversDir, "univers_multiworld.db");
            TicketStore = new MultiWorldTicketStore(ticketDbPath);

            ServerApi.Hooks.GameInitialize.Register(UniversPlugin.Instance, OnGameInitialize);
            ServerApi.Hooks.ServerJoin.Register(UniversPlugin.Instance, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnNetGreetPlayer);
            ServerApi.Hooks.ServerLeave.Register(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.NetGetData.Register(UniversPlugin.Instance, OnNetGetData);
            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.GamePostInitialize.Register(UniversPlugin.Instance, OnGamePostInitialize);
            ServerApi.Hooks.ServerCommand.Register(UniversPlugin.Instance, OnServerCommand);
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            PlayerHooks.PlayerLogout += OnPlayerLogout;
            PlayerHooks.PlayerCommand += OnPlayerCommand;

            if (!_processExitHooked)
            {
                _processExitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => SaveAllActivePlayersSscSync();
            }

            MultiWorldCommands.Register();

            if (IsChildProcess)
            {
                CurrentWorldId = childEnv!;
                if (int.TryParse(Environment.GetEnvironmentVariable("UNIVERS_MASTER_IPC_PORT"), out int ipcPort) && ipcPort > 0)
                {
                    MasterIpcPort = ipcPort;
                }
            }
            else
            {
                CurrentWorldId = Config.HubWorldId;
                MasterIpcPort = Config.ControlIpcPort;
                string sscDbPath = Path.Combine(masterUniversDir, "univers_ssc.db");
                SscStore = new MultiWorldSscStore(sscDbPath);
                TryTakeoverPublicPortAndStartProxy();
            }

            _initialized = true;
        }

        private static void OnGameInitialize(EventArgs args)
        {
            if (!IsChildProcess)
            {
                TryTakeoverPublicPortAndStartProxy();
            }
        }

        private static void OnGamePostInitialize(EventArgs args)
        {
            TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Status SSC ({CurrentWorldId}): {(Main.ServerSideCharacter ? "AKTIF (sscconfig.json Enabled=true)" : "NONAKTIF! Cek tshock/sscconfig.json")}");

            if (IsChildProcess)
            {
                InitializeChildMode(CurrentWorldId);
            }
            else
            {
                InitializeMasterMode();
            }
        }

        private static void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            var player = e.Player;
            if (player == null || !player.Active || !player.RealPlayer) return;

            TrackSlotPlayer(player);

            if (!Main.ServerSideCharacter)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] '{player.Name}' login saat ServerSideCharacter=FALSE (karakter lokal Terraria diizinkan masuk). Aktifkan Enabled=true di tshock/sscconfig.json!");
                if (player.HasPermission("univers.mw.admin"))
                {
                    player.SendWarningMessage("[SSC Info] ServerSideCharacter saat ini NONAKTIF di sscconfig.json!");
                }
                return;
            }

            // Jika TShock UUID auto-login memicu PlayerPostLogin sebelum packet ticket diproses, klaim ticket dulu
            if (!_pendingSscRestores.ContainsKey(player.Name))
            {
                TryApplyTransferTicket(player, markForCleanup: true, consumeFromMaster: true);
            }

            // Fresh login (Master Hub atau Child langsung tanpa transfer ticket): restore snapshot terbaru dari shared SSC store / child SQLite
            if (player.Account != null &&
                !_pendingSscRestores.ContainsKey(player.Name) &&
                !IsPlayerTransferring(player))
            {
                string snapshotJson = string.Empty;
                if (!IsChildProcess)
                {
                    snapshotJson = ResolveLatestMasterSscSnapshot(player.Account.Name, player.Name);
                    SscStore?.MarkEnteredWorld(player.Account.Name, player.Name, CurrentWorldId);
                }
                else
                {
                    try
                    {
                        var sscRes = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "load_ssc",
                            AccountName = player.Account.Name,
                            PlayerName = player.Name
                        }, timeoutMs: 1500).GetAwaiter().GetResult();
                        if (sscRes.Success && !string.IsNullOrWhiteSpace(sscRes.SscSnapshotJson))
                        {
                            snapshotJson = sscRes.SscSnapshotJson;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(snapshotJson))
                {
                    player.IgnoreSSCPackets = true;
                    _sscRestoreGuardUntil[player.Name] = DateTime.UtcNow.AddSeconds(3);
                    _pendingSscRestores[player.Name] = snapshotJson;
                    ApplyPendingSscRestore(player, clearPendingAndGuard: false);

                    int who = player.Index;
                    string restoringName = player.Name;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(350);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            var cur = TShock.Players[who];
                            if (cur != null && cur.Active && cur.RealPlayer &&
                                string.Equals(cur.Name, restoringName, StringComparison.OrdinalIgnoreCase))
                            {
                                ApplyPendingSscRestore(cur, clearPendingAndGuard: false);
                            }
                        });

                        await Task.Delay(850);
                        _mainThreadQueue.Enqueue(() =>
                        {
                            var cur = TShock.Players[who];
                            if (cur != null && cur.Active && cur.RealPlayer &&
                                string.Equals(cur.Name, restoringName, StringComparison.OrdinalIgnoreCase))
                            {
                                ApplyPendingSscRestore(cur, clearPendingAndGuard: true);
                            }
                        });
                    });
                }
            }
        }

        private static void OnPlayerLogout(PlayerLogoutEventArgs e)
        {
            var player = e.Player;
            if (player == null || !Main.ServerSideCharacter || player.TPlayer == null || string.IsNullOrWhiteSpace(player.Name)) return;
            SavePlayerSscEverywhere(player, synchronousIpc: true, forceLoggedIn: true, accountNameHint: player.Account?.Name, playerNameHint: player.Name);
        }

        private static void TryTakeoverPublicPortAndStartProxy()
        {
            if (IsChildProcess || !Config.Enabled || !Config.ProxyEnabled) return;

            if (_takenOverPublicPort > 0 && _effectiveMasterHubPort > 0)
            {
                if (Netplay.ListenPort == _takenOverPublicPort)
                {
                    Netplay.ListenPort = _effectiveMasterHubPort;
                }
                if (TShock.Config?.Settings != null && TShock.Config.Settings.ServerPort == _takenOverPublicPort)
                {
                    TShock.Config.Settings.ServerPort = _effectiveMasterHubPort;
                }
                return;
            }

            if (!Config.AutoTakeoverPublicPort) return;

            int requestedPort = ResolveRawRequestedPort(Config.MasterWorldPort);
            if (requestedPort != Config.ProxyPort) return;

            int internalHubPort = FindFreeInternalHubPort(Config.ProxyPort);
            if (internalHubPort <= 0)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Tidak menemukan port internal kosong untuk menggeser Master Hub dari port {Config.ProxyPort}.");
                return;
            }

            var candidateProxy = new MultiWorldProxy(Config.HubWorldId, internalHubPort);
            if (!candidateProxy.Start(Config.ProxyPort, Config.ProxyPort))
            {
                try { candidateProxy.Dispose(); } catch { }
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Auto-Takeover port {Config.ProxyPort} gagal (port sudah dipakai proses lain).");
                return;
            }

            Proxy = candidateProxy;
            _takenOverPublicPort = requestedPort;
            _effectiveMasterHubPort = internalHubPort;

            Netplay.ListenPort = internalHubPort;
            if (TShock.Config?.Settings != null)
            {
                TShock.Config.Settings.ServerPort = internalHubPort;
            }

            TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Auto-Takeover AKTIF: Gateway Proxy mengambil alih port publik {Proxy.ListenPort} -> Master Hub '{Config.HubWorldId}' digeser ke port internal {internalHubPort}!");
        }

        private static int FindFreeInternalHubPort(int publicProxyPort)
        {
            var reserved = new HashSet<int>
            {
                publicProxyPort,
                Config.ControlIpcPort,
                Config.FallbackProxyPort
            };

            foreach (var w in Config.Worlds)
            {
                if (!w.IsHub && w.Port > 0)
                {
                    reserved.Add(w.Port);
                }
            }

            var candidates = new List<int>();
            if (Config.MasterWorldPort > 0) candidates.Add(Config.MasterWorldPort);
            if (publicProxyPort + 1 <= 65500) candidates.Add(publicProxyPort + 1);
            for (int p = 7778; p <= 7900; p++) candidates.Add(p);

            foreach (int candidate in candidates.Distinct())
            {
                if (candidate < 1024 || candidate > 65500) continue;
                if (reserved.Contains(candidate)) continue;
                if (IsTcpPortAvailable(candidate)) return candidate;
            }

            return 0;
        }

        private static bool IsTcpPortAvailable(int port)
        {
            try
            {
                using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ResolveListenPort(int fallbackPort)
        {
            if (!IsChildProcess && _effectiveMasterHubPort > 0)
            {
                return _effectiveMasterHubPort;
            }
            return ResolveRawRequestedPort(fallbackPort);
        }

        private static int ResolveRawRequestedPort(int fallbackPort)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("-port", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[i + 1], out int cliPort) &&
                    cliPort > 0)
                {
                    return cliPort;
                }
            }

            if (Netplay.ListenPort > 0) return Netplay.ListenPort;
            if (TShock.Config?.Settings != null && TShock.Config.Settings.ServerPort > 0)
            {
                return TShock.Config.Settings.ServerPort;
            }
            return fallbackPort;
        }

        private static string ResolveMasterUniversDir(string localUniversDir)
        {
            string? masterSave = Environment.GetEnvironmentVariable("UNIVERS_MASTER_SAVEPATH");
            if (!string.IsNullOrWhiteSpace(masterSave))
            {
                try
                {
                    string resolved = Path.Combine(Path.GetFullPath(masterSave), "Univers");
                    if (Directory.Exists(resolved)) return resolved;
                }
                catch { }
            }

            string candidateRoot = Path.Combine(AppContext.BaseDirectory, "tshock", "Univers");
            if (IsChildProcess && Directory.Exists(candidateRoot))
            {
                return candidateRoot;
            }
            return localUniversDir;
        }

        private static void InitializeMasterMode()
        {
            CurrentWorldId = Config.HubWorldId;
            MasterIpcPort = Config.ControlIpcPort;

            int actualMasterPort = ResolveListenPort(Config.MasterWorldPort);

            ProcessManager = new MultiWorldProcessManager(Config, _configPath, _multiWorldRoot, actualMasterPort);
            IpcServer = new MultiWorldIpcServer(Config.ControlIpcPort, HandleIpcRequestAsync);
            IpcServer.Start();

            if (Config.ProxyEnabled)
            {
                if (Proxy != null && Proxy.IsListening)
                {
                    TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Gateway Proxy AKTIF di port {Proxy.ListenPort} -> Hub '{CurrentWorldId}' (port internal {actualMasterPort}). Konek via IP:{Proxy.ListenPort}!");
                }
                else
                {
                    Proxy = new MultiWorldProxy(Config.HubWorldId, actualMasterPort);

                    int preferred = actualMasterPort == Config.ProxyPort ? Config.FallbackProxyPort : Config.ProxyPort;
                    int fallback = Config.FallbackProxyPort == preferred ? Config.ProxyPort + 10 : Config.FallbackProxyPort;

                    if (Proxy.Start(preferred, fallback))
                    {
                        if (Proxy.ListenPort == Config.ProxyPort)
                        {
                            TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Gateway Proxy AKTIF di port {Proxy.ListenPort} -> Hub '{CurrentWorldId}' (port {actualMasterPort}). Konek via IP:{Proxy.ListenPort}!");
                        }
                        else
                        {
                            TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Hub berjalan di port {actualMasterPort}, Gateway Proxy dialihkan ke port {Proxy.ListenPort}.");
                            TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Untuk MultiWorld di localhost/VPS, jalankan Hub di port 7778 (atau aktifkan AutoTakeoverPublicPort=true) agar Proxy di port {Config.ProxyPort}!");
                        }
                    }
                    else
                    {
                        TShock.Log.ConsoleError($"[Univers.MultiWorld] Gagal menyalakan Gateway Proxy (port {preferred}/{fallback} terpakai).");
                    }
                }
            }

            _heartbeatTimer = new System.Timers.Timer(15_000);
            _heartbeatTimer.Elapsed += async (_, _) =>
            {
                try
                {
                    if (ProcessManager != null)
                    {
                        await ProcessManager.CheckAutoSleepTickAsync();
                    }
                }
                catch { }
            };
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Start();

            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                if (ProcessManager != null)
                {
                    await ProcessManager.BootAutoStartWorldsAsync();
                }
            });

            TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Master Hub '{CurrentWorldId}' siap ({Config.Worlds.Count} world, Auto-Sleep {Config.EmptyShutdownMinutes}m, CrossWorldChat={Config.CrossWorldChat}).");
        }

        private static void InitializeChildMode(string worldId)
        {
            CurrentWorldId = worldId;
            if (int.TryParse(Environment.GetEnvironmentVariable("UNIVERS_MASTER_IPC_PORT"), out int ipcPort) && ipcPort > 0)
            {
                MasterIpcPort = ipcPort;
            }

            int childPort = ResolveListenPort(
                int.TryParse(Environment.GetEnvironmentVariable("UNIVERS_CHILD_PORT"), out int cp) ? cp : 7779);

            ChildPushServer = new MultiWorldIpcServer(0, HandleChildPushRequestAsync);
            ChildPushServer.Start(logStartup: false);
            int callbackPort = ChildPushServer.BoundPort;

            int pid = Environment.ProcessId;

            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                var names = GetLocalActivePlayerNames();
                await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                {
                    Action = "child_register",
                    WorldId = CurrentWorldId,
                    Port = childPort,
                    CallbackPort = callbackPort,
                    Pid = pid,
                    PlayerCount = names.Count,
                    OnlinePlayerNames = names
                });
            });

            _heartbeatTimer = new System.Timers.Timer(8_000);
            _heartbeatTimer.Elapsed += async (_, _) =>
            {
                try
                {
                    var names = GetLocalActivePlayerNames();
                    await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                    {
                        Action = "child_heartbeat",
                        WorldId = CurrentWorldId,
                        Port = childPort,
                        CallbackPort = callbackPort,
                        Pid = pid,
                        PlayerCount = names.Count,
                        OnlinePlayerNames = names
                    });
                }
                catch { }
            };
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Start();

            TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Child World '{CurrentWorldId}' aktif (Port {childPort}, PushIPC {callbackPort}).");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            SaveAllActivePlayersSscSync();
            _initialized = false;

            try { _heartbeatTimer?.Stop(); _heartbeatTimer?.Dispose(); } catch { }

            ServerApi.Hooks.GameInitialize.Deregister(UniversPlugin.Instance, OnGameInitialize);
            ServerApi.Hooks.ServerJoin.Deregister(UniversPlugin.Instance, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnNetGreetPlayer);
            ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnNetGetData);
            ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.GamePostInitialize.Deregister(UniversPlugin.Instance, OnGamePostInitialize);
            ServerApi.Hooks.ServerCommand.Deregister(UniversPlugin.Instance, OnServerCommand);
            PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
            PlayerHooks.PlayerLogout -= OnPlayerLogout;
            PlayerHooks.PlayerCommand -= OnPlayerCommand;

            _effectiveMasterHubPort = 0;
            _takenOverPublicPort = 0;

            try { Proxy?.Dispose(); } catch { }
            try { ChildPushServer?.Dispose(); } catch { }
            try { ProcessManager?.Dispose(); } catch { }
            try { IpcServer?.Dispose(); } catch { }
            try { TicketStore?.Dispose(); } catch { }
            try { SscStore?.Dispose(); } catch { }
        }

        public static void Reload()
        {
            if (string.IsNullOrEmpty(_configPath)) return;
            Config = MultiWorldConfig.Load(_configPath);
        }

        public static List<string> GetLocalActivePlayerNames()
        {
            var list = new List<string>();
            foreach (var p in TShock.Players)
            {
                if (p != null && p.Active && p.RealPlayer && !string.IsNullOrWhiteSpace(p.Name))
                {
                    list.Add(p.Name);
                }
            }
            return list;
        }

        public static string FormatWorldChatBadge(string worldId)
        {
            var def = Config.GetWorld(worldId);
            string tag = !string.IsNullOrWhiteSpace(def?.ChatTag) ? def.ChatTag : worldId.ToUpperInvariant();
            string hex = !string.IsNullOrWhiteSpace(def?.ChatColorHex) ? def.ChatColorHex : "facc15";
            return $"[c/{hex}:[][c/{hex}:{tag}][c/{hex}:]]";
        }

        #region Cross-World Chat & Presence Indicators

     public static void HandleLocalFormattedChat(TSPlayer sender, string formattedChat)
     {
         BroadcastLocalFormatted(formattedChat, Color.White);

         // Jika di Master Hub (lobby), cetak chat bersih ke console & teruskan ke Discord
         if (!IsChildProcess)
         {
             TShock.Log.ConsoleInfo($"[Chat:{CurrentWorldId}] {StripTerrariaColorTags(formattedChat)}");
             DiscordModule.NotifyInGameChat(CurrentWorldId, sender.Name, formattedChat);
         }

         if (!Config.CrossWorldChat) return;

         string badge = FormatWorldChatBadge(CurrentWorldId);
         string crossWorldMessage = $"{badge} {formattedChat}";

         if (IsChildProcess)
         {
             _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
             {
                 Action = "relay_chat",
                 WorldId = CurrentWorldId,
                 PlayerName = sender.Name,
                 FormattedMessage = crossWorldMessage
             });
         }
         else
         {
             _ = PushMessageToOtherChildrenAsync(CurrentWorldId, "push_chat", crossWorldMessage, 255, 255, 255);
         }
     }

     public static void BroadcastExternalGlobalChat(string message, byte r = 255, byte g = 255, byte b = 255)
     {
         if (string.IsNullOrWhiteSpace(message)) return;
         var c = new Color(r, g, b);

         if (HasActiveRealPlayers())
         {
             _mainThreadQueue.Enqueue(() => BroadcastLocalFormatted(message, c, skipJustJoined: false));
         }

         if (!IsChildProcess)
         {
             _ = PushMessageToOtherChildrenAsync(null, "push_chat", message, r, g, b);
         }
     }

     public static void SendDirectMessageToGlobalPlayer(string nameOrAccount, string message, byte r = 125, byte g = 211, byte b = 252)
     {
         if (string.IsNullOrWhiteSpace(nameOrAccount) || string.IsNullOrWhiteSpace(message)) return;
         var c = new Color(r, g, b);

         _mainThreadQueue.Enqueue(() =>
         {
             var localTarget = TShock.Players.FirstOrDefault(p =>
                 p != null && p.Active && p.RealPlayer &&
                 (p.Name.Equals(nameOrAccount, StringComparison.OrdinalIgnoreCase) ||
                  (p.IsLoggedIn && p.Account != null && p.Account.Name.Equals(nameOrAccount, StringComparison.OrdinalIgnoreCase))));
             if (localTarget != null)
             {
                 localTarget.SendMessage(message, c);
             }
         });

         if (!IsChildProcess && ProcessManager != null)
         {
             var targets = ProcessManager.GetRunningCallbackTargets(null);
             foreach (var t in targets)
             {
                 _ = MultiWorldIpcClient.SendAsync(t.CallbackPort, new IpcRequest
                 {
                     Action = "push_player_notice",
                     PlayerName = nameOrAccount,
                     FormattedMessage = message,
                     ColorR = r,
                     ColorG = g,
                     ColorB = b
                 }, timeoutMs: 2000);
             }
         }
     }

     public static Task<IpcResponse> SendDiscordRpcToMasterAsync(IpcRequest req)
     {
         return MultiWorldIpcClient.SendAsync(MasterIpcPort, req, timeoutMs: 4000);
     }

     public static Task<IpcResponse> SendChildEventToDiscordAsync(string worldId, string message)
     {
         return MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
         {
             Action = "discord_event",
             WorldId = worldId,
             FormattedMessage = message
         }, timeoutMs: 2500);
     }

             private static string StripTerrariaColorTags(string text)
     {
         if (string.IsNullOrEmpty(text)) return string.Empty;
         return System.Text.RegularExpressions.Regex.Replace(text, @"\[c/[0-9a-fA-F]{6}:([^\]]*)\]", "$1");
     }

        private static bool HasActiveRealPlayers()
        {
            foreach (var p in TShock.Players)
            {
                if (p != null && p.Active && p.RealPlayer) return true;
            }
            return false;
        }

        private static void BroadcastLocalFormatted(string message, Color color, bool skipJustJoined = false)
        {
            DateTime now = DateTime.UtcNow;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active || !p.RealPlayer) continue;
                if (skipJustJoined &&
                    !string.IsNullOrWhiteSpace(p.Name) &&
                    _playerJoinedUtc.TryGetValue(p.Name, out var joinedAt) &&
                    (now - joinedAt).TotalSeconds < 1.5)
                {
                    continue;
                }
                p.SendMessage(message, color);
            }
        }

        private static async Task PushMessageToOtherChildrenAsync(string? excludeWorldId, string action, string message, byte r, byte g, byte b)
        {
            if (ProcessManager == null) return;
            var targets = ProcessManager.GetRunningCallbackTargets(excludeWorldId);
            if (targets.Count == 0) return;

            var tasks = targets.Select(t => MultiWorldIpcClient.SendAsync(t.CallbackPort, new IpcRequest
            {
                Action = action,
                WorldId = excludeWorldId ?? string.Empty,
                FormattedMessage = message,
                ColorR = r,
                ColorG = g,
                ColorB = b
            }, timeoutMs: 2000));

            await Task.WhenAll(tasks);
        }

        private static Task<IpcResponse> HandleChildPushRequestAsync(IpcRequest req)
        {
            if (req.Action == "push_chat" || req.Action == "push_notice" || req.Action == "push_broadcast")
            {
                if (!HasActiveRealPlayers())
                {
                    return Task.FromResult(new IpcResponse { Success = true });
                }

                var c = new Color(req.ColorR, req.ColorG, req.ColorB);
                string msg = req.FormattedMessage;
                DateTime expiresUtc = DateTime.UtcNow.AddSeconds(3);
                _mainThreadQueue.Enqueue(() =>
                {
                    if (DateTime.UtcNow <= expiresUtc)
                    {
                        BroadcastLocalFormatted(msg, c, skipJustJoined: true);
                    }
                });
                return Task.FromResult(new IpcResponse { Success = true });
            }
            else if (req.Action == "push_player_notice")
            {
                string targetName = req.PlayerName;
                string msg = req.FormattedMessage;
                var c = new Color(req.ColorR, req.ColorG, req.ColorB);
                _mainThreadQueue.Enqueue(() =>
                {
                    var localTarget = TShock.Players.FirstOrDefault(p =>
                        p != null && p.Active && p.RealPlayer &&
                        (p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                         (p.IsLoggedIn && p.Account != null && p.Account.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))));
                    localTarget?.SendMessage(msg, c);
                });
                return Task.FromResult(new IpcResponse { Success = true });
            }
            else if (req.Action == "remote_give")
            {
                var target = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(req.PlayerName, StringComparison.OrdinalIgnoreCase));
                if (target == null || !target.Active)
                {
                    return Task.FromResult(new IpcResponse { Success = false, Message = $"Player '{req.PlayerName}' tidak ditemukan di world '{CurrentWorldId}'." });
                }
                var items = FindItems(req.AdminSubAction);
                if (items.Count == 0)
                {
                    return Task.FromResult(new IpcResponse { Success = false, Message = $"Item '{req.AdminSubAction}' tidak ditemukan." });
                }
                var item = items[0];
                int stack = req.Port > 0 ? Math.Min(req.Port, item.maxStack) : item.maxStack;
                _mainThreadQueue.Enqueue(() =>
                {
                    target.GiveItem(item.type, stack);
                    target.SendSuccessMessage($"[c/38bdf8:[SERVER]] Kamu menerima {stack}x {item.Name}!");
                });
                return Task.FromResult(new IpcResponse { Success = true, Message = $"Berhasil memberikan {stack}x {item.Name} kepada {target.Name} di '{CurrentWorldId}'." });
            }
            else if (req.Action == "remote_exec")
            {
                string command = EnsureCommandSpecifier(req.FormattedMessage);
                _mainThreadQueue.Enqueue(() =>
                {
                    _suppressRemoteRouting = true;
                    try
                    {
                        Commands.HandleCommand(TSPlayer.Server, command);
                    }
                    finally
                    {
                        _suppressRemoteRouting = false;
                    }
                });
                return Task.FromResult(new IpcResponse { Success = true, Message = $"Perintah '{req.FormattedMessage}' dieksekusi di '{CurrentWorldId}'." });
            }
            return Task.FromResult(new IpcResponse { Success = true });
        }

        private static string EnsureCommandSpecifier(string? command)
        {
            string trimmed = (command ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return string.Empty;
            string spec = string.IsNullOrEmpty(TShock.Config?.Settings?.CommandSpecifier) ? "/" : TShock.Config.Settings.CommandSpecifier;
            string silent = string.IsNullOrEmpty(TShock.Config?.Settings?.CommandSilentSpecifier) ? "." : TShock.Config.Settings.CommandSilentSpecifier;
            if (trimmed.StartsWith(spec, StringComparison.Ordinal) || trimmed.StartsWith(silent, StringComparison.Ordinal))
            {
                return trimmed;
            }
            return spec + trimmed;
        }

        private static void NotifyMasterPresenceEvent(string eventKind, string playerName, string worldId)
        {
            if (!Config.CrossWorldPresenceNotices || string.IsNullOrWhiteSpace(playerName)) return;

            if (IsChildProcess)
            {
                _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                {
                    Action = "presence_event",
                    AdminSubAction = eventKind,
                    PlayerName = playerName,
                    WorldId = worldId
                });
                return;
            }

            _ = HandleMasterPresenceEventAsync(eventKind, playerName, worldId);
        }

        private static async Task HandleMasterPresenceEventAsync(string eventKind, string playerName, string worldId)
        {
            if (!Config.CrossWorldPresenceNotices || string.IsNullOrWhiteSpace(playerName)) return;

            var def = Config.GetWorld(worldId);
            string display = def?.DisplayName ?? worldId;
            string badge = FormatWorldChatBadge(worldId);

            if (eventKind == "server_join")
            {
                string msg = $"[MultiWorld] {playerName} bergabung ke server ({badge}).";
                BroadcastLocalFormatted(msg, Color.LightSlateGray);
                await PushMessageToOtherChildrenAsync(worldId, "push_notice", msg, 148, 163, 184);
                DiscordModule.NotifyPresenceEvent("server_join", worldId, playerName);
            }
            else if (eventKind == "server_leave")
            {
                string msg = $"[MultiWorld] {playerName} keluar dari server.";
                BroadcastLocalFormatted(msg, Color.LightSlateGray);
                await PushMessageToOtherChildrenAsync(worldId, "push_notice", msg, 148, 163, 184);
                DiscordModule.NotifyPresenceEvent("server_leave", worldId, playerName);
            }
            else if (eventKind == "world_switch")
            {
                string msg = $"[MultiWorld] {playerName} pindah ke {display} ({badge}).";
                BroadcastLocalFormatted(msg, Color.LightSlateGray);
                await PushMessageToOtherChildrenAsync(null, "push_notice", msg, 148, 163, 184);

                // Relay perpindahan world ke Discord (mention role world) + sync role world segera.
                DiscordModule.NotifyPresenceEvent("world_switch", worldId, playerName);
                DiscordModule.RequestImmediateRoleSync(playerName, worldId);
            }
        }

        #endregion

        #region Hooks & Seamless Cross-World Auth + Entity Cleanup

        private static void OnServerJoin(JoinEventArgs args)
        {
            _checkedPreGreetTicket.TryRemove(args.Who, out _);
            _dirtySscSlots.TryRemove(args.Who, out _);
            _preSyncedJoinSlots.TryRemove(args.Who, out _);
            var player = TShock.Players[args.Who];
            if (player == null) return;

            TrackSlotPlayer(player);
            MultiWorldCommands.ClearPendingTeleport(player.Name);
            ClearTransferLock(player.Name);
            if (!string.IsNullOrWhiteSpace(player.Name))
            {
                _checkedPreGreetTicket[args.Who] = 1;
                if (!TryApplyTransferTicket(player, markForCleanup: true, consumeFromMaster: true) &&
                    !_claimedTickets.ContainsKey(player.Name))
                {
                    _recentTransfers.TryRemove(player.Name, out _);
                }
            }
        }

        public static bool IsPlayerTransferring(TSPlayer? player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.Name)) return false;
            return (_recentTransfers.TryGetValue(player.Name, out var untilUtc) && DateTime.UtcNow <= untilUtc) ||
                   _claimedTickets.ContainsKey(player.Name);
        }

        private static void OnNetGreetPlayer(GreetPlayerEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || !player.Active) return;

            TrackSlotPlayer(player);

            if (!string.IsNullOrWhiteSpace(player.Name))
            {
                _playerJoinedUtc[player.Name] = DateTime.UtcNow;
            }

            MultiWorldCommands.ClearPendingTeleport(player.Name);
            ClearTransferLock(player.Name);

            bool transferred = TryApplyTransferTicket(player, markForCleanup: true, consumeFromMaster: true);
            bool hasPendingClear = _pendingEntityClearPlayers.TryRemove(player.Name, out _);
            _claimedTickets.TryRemove(player.Name, out _);
            if (!transferred && !hasPendingClear && !string.IsNullOrWhiteSpace(player.Name))
            {
                _recentTransfers.TryRemove(player.Name, out _);
            }

            if (Main.ServerSideCharacter && player.RealPlayer &&
                (_pendingSscRestores.ContainsKey(player.Name) || IsSscRestoreGuarded(player.Name)))
            {
                player.IgnoreSSCPackets = true;
            }

            if (transferred || hasPendingClear)
            {
                MultiWorldCommands.MarkArrivalCooldown(player.Name, player.HasPermission("univers.mw.admin") ? 3 : 10);

                int who = player.Index;
                _mainThreadQueue.Enqueue(() =>
                {
                    ClearInactiveEntitiesForPlayer(who);
                    SendSpawnSectionsAndTeleport(who);
                });

                var worldDef = Config.GetWorld(CurrentWorldId);
                string title = worldDef?.DisplayName ?? CurrentWorldId;
                player.SendMessage($"[MultiWorld] Kamu memasuki world '{title}' ({CurrentWorldId}). Ketik /lobby untuk kembali ke Hub.", Color.Aquamarine);

                if (transferred && Config.CrossWorldPresenceNotices)
                {
                    string srcId = _recentTransferSources.TryGetValue(player.Name, out var sTuple) ? sTuple.SourceWorldId : string.Empty;
                    var srcDef = Config.GetWorld(srcId);
                    string srcDisplay = srcDef?.DisplayName ?? (!string.IsNullOrWhiteSpace(srcId) ? srcId : "world lain");
                    string arriveMsg = $"[MultiWorld] {player.Name} telah tiba dari {srcDisplay}.";
                    BroadcastLocalFormatted(arriveMsg, Color.LightSlateGray);
                    _ = PushMessageToOtherChildrenAsync(CurrentWorldId, "push_notice", arriveMsg, 148, 163, 184);
                }
            }
            else
            {
                // Fresh login ke server (Master atau Child) -> beritahu world lain
                if (!IsChildProcess)
                {
                    var proxySession = Proxy?.FindSession(player.Name, player.UUID, player.Index, CurrentWorldId);
                    if (proxySession != null)
                    {
                        TShock.Log.ConsoleInfo($"[Univers.MultiWorld] '{player.Name}' terhubung via Proxy IP: {proxySession.ClientRemoteIp}");
                    }
                }

                NotifyMasterPresenceEvent("server_join", player.Name, CurrentWorldId);

                if (!IsChildProcess && Proxy != null && Proxy.IsListening && Proxy.FindSession(player.Name, player.UUID, player.Index, CurrentWorldId) == null)
                {
                    int hubPort = ResolveListenPort(Config.MasterWorldPort);
                    player.SendWarningMessage($"[MultiWorld] Kamu masuk lewat port langsung ({hubPort}). Konek ke port {Proxy.ListenPort} agar bisa /mw tp pindah world!");
                }
            }

            // Kirim indikator pemain online di world lain agar tidak terasa sepi
            _ = SendCrossWorldRosterGreetingAsync(player);
        }

        private static void SendSpawnSectionsAndTeleport(int who)
        {
            var p = TShock.Players[who];
            if (p == null || !p.Active || !p.RealPlayer) return;

            int spawnX = Main.spawnTileX;
            int spawnY = Main.spawnTileY;
            while (spawnY > 20 && Main.tile[spawnX, spawnY - 1] != null &&
                   Main.tile[spawnX, spawnY - 1].active() && Main.tileSolid[Main.tile[spawnX, spawnY - 1].type])
            {
                spawnY--;
            }

            float spawnPx = spawnX * 16f;
            float spawnPy = Math.Max(10, spawnY - 3) * 16f;

            void SyncClientToSpawn(TSPlayer targetPlayer)
            {
                try
                {
                    int maxSecX = Math.Max(1, Main.maxTilesX / 200);
                    int maxSecY = Math.Max(1, Main.maxTilesY / 150);
                    int centerSx = Math.Clamp(spawnX / 200, 0, maxSecX - 1);
                    int centerSy = Math.Clamp(spawnY / 150, 0, maxSecY - 1);
                    int minSx = Math.Max(0, centerSx - 1);
                    int maxSx = Math.Min(maxSecX - 1, centerSx + 1);
                    int minSy = Math.Max(0, centerSy - 1);
                    int maxSy = Math.Min(maxSecY - 1, centerSy + 1);

                    for (int sx = minSx; sx <= maxSx; sx++)
                    {
                        for (int sy = minSy; sy <= maxSy; sy++)
                        {
                            NetMessage.SendSection(targetPlayer.Index, sx, sy);
                        }
                    }
                    NetMessage.SendData((int)PacketTypes.TileFrameSection, targetPlayer.Index, -1, null, minSx, minSy, maxSx, maxSy);
                }
                catch { }

                targetPlayer.TPlayer.SpawnX = spawnX;
                targetPlayer.TPlayer.SpawnY = spawnY;
                targetPlayer.TPlayer.position = new Vector2(spawnPx, spawnPy);
                targetPlayer.TPlayer.velocity = Vector2.Zero;

                // Kirim Spawn & Teleport resmi SETELAH client in-game (CompleteConnection selesai)
                NetMessage.SendData((int)PacketTypes.PlayerSpawn, -1, -1, null, targetPlayer.Index, (int)PlayerSpawnContext.SpawningIntoWorld);
                targetPlayer.Teleport(spawnPx, spawnPy);
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                _mainThreadQueue.Enqueue(() =>
                {
                    var cur = TShock.Players[who];
                    if (cur != null && cur.Active && cur.RealPlayer)
                    {
                        SyncClientToSpawn(cur);
                        ApplyPendingSscRestore(cur, clearPendingAndGuard: false);
                    }
                });

                await Task.Delay(600);
                _mainThreadQueue.Enqueue(() =>
                {
                    var cur = TShock.Players[who];
                    if (cur != null && cur.Active && cur.RealPlayer)
                    {
                        SyncClientToSpawn(cur);
                        ApplyPendingSscRestore(cur, clearPendingAndGuard: true);
                    }
                });
            });
        }
                                                   
        private static async Task SendCrossWorldRosterGreetingAsync(TSPlayer player)
        {
            try
            {
                await Task.Delay(500);
                if (player == null || !player.Active || !player.RealPlayer) return;

                var worlds = await FetchAllWorldSnapshotsAsync();
                int totalOnline = worlds.Sum(w => w.PlayerCount);
                if (totalOnline <= 1) return;

                var parts = new List<string>();
                foreach (var w in worlds.Where(x => x.PlayerCount > 0))
                {
                    string names = w.OnlinePlayerNames.Count > 0 ? string.Join(", ", w.OnlinePlayerNames.Take(5)) : $"{w.PlayerCount} player";
                    if (w.OnlinePlayerNames.Count > 5) names += ", ...";
                    parts.Add($"{w.Id} ({names})");
                }

                if (parts.Count > 0)
                {
                    player.SendMessage($"[Online Server: {totalOnline}] {string.Join(" | ", parts)}", Color.LightSkyBlue);
                    if (Config.CrossWorldChat)
                    {
                        player.SendMessage("[Info] Chat langsung terhubung ke semua world. Ketik /mw online untuk daftar lengkap.", Color.LightGray);
                    }
                }
            }
            catch { }
        }

        private static bool IsSscRestoreGuarded(string? playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return false;
            if (_sscRestoreGuardUntil.TryGetValue(playerName, out var until))
            {
                if (DateTime.UtcNow <= until) return true;
                _sscRestoreGuardUntil.TryRemove(playerName, out _);
            }
            return false;
        }

        private static void OnServerLeave(LeaveEventArgs args)
        {
            _checkedPreGreetTicket.TryRemove(args.Who, out _);
            _dirtySscSlots.TryRemove(args.Who, out _);
            _preSyncedJoinSlots.TryRemove(args.Who, out _);
            _trackedSlotPlayers.TryRemove(args.Who, out var tracked);

            // UniversPlugin berjalan pada Order=1 (setelah TShock Order=0 yang me-null-kan TShock.Players[args.Who]),
            // sehingga fallback ke cached TSPlayer dari slot agar save SSC saat leave selalu dieksekusi.
            var player = TShock.Players[args.Who] ?? tracked?.Player;
            string pName = player != null && !string.IsNullOrWhiteSpace(player.Name)
                ? player.Name
                : (tracked?.PlayerName ?? string.Empty);

            if (string.IsNullOrWhiteSpace(pName)) return;

            _pendingSscRestores.TryRemove(pName, out _);
            _sscRestoreGuardUntil.TryRemove(pName, out _);

            bool wasLoggedIn = (player?.IsLoggedIn ?? false) || (tracked?.WasLoggedIn ?? false);
            string? accountHint = player?.Account != null && !string.IsNullOrWhiteSpace(player.Account.Name)
                ? player.Account.Name
                : (tracked != null && !string.IsNullOrWhiteSpace(tracked.AccountName) ? tracked.AccountName : null);

            if (player != null)
            {
                SavePlayerSscEverywhere(player, synchronousIpc: true, forceLoggedIn: wasLoggedIn, accountNameHint: accountHint, playerNameHint: pName);
            }

            MultiWorldCommands.ClearPendingTeleport(pName);
            ClearTransferLock(pName);
            _pendingEntityClearPlayers.TryRemove(pName, out _);
            _claimedTickets.TryRemove(pName, out _);
            _playerJoinedUtc.TryRemove(pName, out _);
            if (!string.IsNullOrWhiteSpace(accountHint))
            {
                _inFlightMasterSyncs.TryRemove(accountHint, out _);
            }

            bool wasTransfer = _recentTransfers.TryGetValue(pName, out var untilUtc) && DateTime.UtcNow <= untilUtc;
            if (!wasTransfer)
            {
                NotifyMasterPresenceEvent("server_leave", pName, CurrentWorldId);
            }

            if (IsChildProcess)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    var names = GetLocalActivePlayerNames();
                    await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                    {
                        Action = "child_heartbeat",
                        WorldId = CurrentWorldId,
                        PlayerCount = names.Count,
                        OnlinePlayerNames = names
                    });
                });
            }
        }

        private static void OnNetGetData(GetDataEventArgs args)
        {
            if (args.Handled) return;
            var player = TShock.Players[args.Msg.whoAmI];
            if (player == null) return;

            TrackSlotPlayer(player);

            int msgNum = (int)args.MsgID;
            if ((msgNum == 4 || msgNum == 6 || msgNum == 68 || args.MsgID == PacketTypes.PlayerSpawn) &&
                !string.IsNullOrWhiteSpace(player.Name) &&
                _checkedPreGreetTicket.TryAdd(args.Msg.whoAmI, 1))
            {
                if (!TryApplyTransferTicket(player, markForCleanup: true, consumeFromMaster: true) &&
                    !_claimedTickets.ContainsKey(player.Name))
                {
                    _recentTransfers.TryRemove(player.Name, out _);
                }
            }

            // Pada fresh join ke Master Hub (Lobby), sinkronkan Master tshock.sqlite dari SscStore/Survival SQLite
            // SEBELUM TShock OnGreetPlayer (UUID auto-login) memanggil RestoreCharacter!
            if (!IsChildProcess &&
                Main.ServerSideCharacter &&
                (msgNum == 68 || args.MsgID == PacketTypes.PlayerSpawn) &&
                !string.IsNullOrWhiteSpace(player.Name) &&
                !IsPlayerTransferring(player) &&
                !_pendingSscRestores.ContainsKey(player.Name) &&
                _preSyncedJoinSlots.TryAdd(args.Msg.whoAmI, 1))
            {
                EnsureMasterCharacterDbSyncedForJoin(player.Name, player.UUID);
            }

            // Blokir paket SSC lokal dari client sebelum login atau selama restore berlangsung agar slot lama & baru tidak pernah bercampur!
            if (Main.ServerSideCharacter && player.TPlayer != null &&
                (args.MsgID == PacketTypes.PlayerSlot ||
                 args.MsgID == PacketTypes.PlayerHp ||
                 args.MsgID == PacketTypes.PlayerMana ||
                 args.MsgID == PacketTypes.PlayerBuff))
            {
                bool blockPreLogin = !player.IsLoggedIn;
                bool blockGuarded = player.IgnoreSSCPackets ||
                                    (!string.IsNullOrWhiteSpace(player.Name) &&
                                     (_pendingSscRestores.ContainsKey(player.Name) || IsSscRestoreGuarded(player.Name)));

                if (blockPreLogin || blockGuarded)
                {
                    args.Handled = true;
                    return;
                }

                if (IsChildProcess && player.IsLoggedIn)
                {
                    _dirtySscSlots[player.Index] = 1;
                }
            }

            if (!IsTransferLocked(player.Name)) return;

            if (args.MsgID == PacketTypes.ItemDrop ||
                args.MsgID == PacketTypes.ChestGetContents ||
                args.MsgID == PacketTypes.ChestItem ||
                args.MsgID == PacketTypes.PlayerSlot)
            {
                args.Handled = true;
            }
        }

        private static DateTime _lastSscAutoSave = DateTime.UtcNow;
        private static DateTime _lastDirtySscFlush = DateTime.UtcNow;

        private static void OnGameUpdate(EventArgs args)
        {
            int processed = 0;
            while (processed < 32 && _mainThreadQueue.TryDequeue(out var action))
            {
                processed++;
                try { action(); }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleWarn($"[Univers.MultiWorld] MainThreadQueue error: {ex.Message}");
                }
            }

            DateTime now = DateTime.UtcNow;
            if (IsChildProcess && Main.ServerSideCharacter && !_dirtySscSlots.IsEmpty && (now - _lastDirtySscFlush).TotalMilliseconds >= 400)
            {
                _lastDirtySscFlush = now;
                foreach (var idx in _dirtySscSlots.Keys.ToArray())
                {
                    _dirtySscSlots.TryRemove(idx, out _);
                    if (idx < 0 || idx >= TShock.Players.Length) continue;
                    var p = TShock.Players[idx];
                    if (p != null && p.Active && p.IsLoggedIn && p.TPlayer != null)
                    {
                        TrackSlotPlayer(p);
                        SavePlayerSscEverywhere(p, synchronousIpc: false);
                    }
                }
            }

            if ((now - _lastSscAutoSave).TotalSeconds >= 2.0)
            {
                _lastSscAutoSave = now;
                if (Main.ServerSideCharacter)
                {
                    foreach (var p in TShock.Players)
                    {
                        if (p != null && p.Active && p.IsLoggedIn && p.TPlayer != null)
                        {
                            if (p.IgnoreSSCPackets && !string.IsNullOrWhiteSpace(p.Name) && !IsSscRestoreGuarded(p.Name))
                            {
                                if (_pendingSscRestores.ContainsKey(p.Name))
                                {
                                    ApplyPendingSscRestore(p, clearPendingAndGuard: true);
                                }
                                else
                                {
                                    _pendingSscRestores.TryRemove(p.Name, out _);
                                    p.IgnoreSSCPackets = false;
                                }
                            }
                            TrackSlotPlayer(p);
                            SavePlayerSscEverywhere(p, synchronousIpc: false);
                        }
                    }
                }
            }
        }

        private static bool TryApplyTransferTicket(TSPlayer player, bool markForCleanup, bool consumeFromMaster)
        {
            if (string.IsNullOrWhiteSpace(player.Name)) return false;

            WorldTransferTicket? ticket = null;
            bool freshlyClaimed = false;

            if (_claimedTickets.TryGetValue(player.Name, out var cached) &&
                DateTime.UtcNow <= cached.ExpiresUtc &&
                cached.Ticket.TargetWorldId.Equals(CurrentWorldId, StringComparison.OrdinalIgnoreCase))
            {
                ticket = cached.Ticket;
            }
            else if (consumeFromMaster)
            {
                if (IsChildProcess)
                {
                    try
                    {
                        var res = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "claim_ticket",
                            WorldId = CurrentWorldId,
                            PlayerName = player.Name
                        }, timeoutMs: 2500).GetAwaiter().GetResult();

                        if (res.Success)
                        {
                            ticket = new WorldTransferTicket
                            {
                                PlayerName = player.Name,
                                AccountName = res.AccountName,
                                GroupName = res.GroupName,
                                ClientUuid = res.ClientUuid,
                                HasWildcardPermission = res.HasWildcardPermission,
                                GroupPermissions = res.GroupPermissions,
                                SscSnapshotJson = res.SscSnapshotJson,
                                TargetWorldId = CurrentWorldId
                            };
                        }
                    }
                    catch { }
                }

                ticket ??= TicketStore?.ConsumeTicket(player.Name, CurrentWorldId);
                if (ticket != null)
                {
                    freshlyClaimed = true;
                    _claimedTickets[player.Name] = (ticket, DateTime.UtcNow.AddSeconds(10));
                }
            }

            if (ticket == null) return false;

            if (markForCleanup)
            {
                _pendingEntityClearPlayers[player.Name] = 1;
            }

            try
            {
                string accName = string.IsNullOrWhiteSpace(ticket.AccountName) ? player.Name : ticket.AccountName;
                string grpName = string.IsNullOrWhiteSpace(ticket.GroupName) ? "default" : ticket.GroupName;

                bool isOwnerOrSuper = ticket.HasWildcardPermission ||
                                      grpName.Equals("superadmin", StringComparison.OrdinalIgnoreCase);
                var group = EnsureSyncedGroup(grpName, isOwnerOrSuper, ticket.GroupPermissions);
                string resolvedGroupName = group?.Name ?? grpName;

                var account = TShock.UserAccounts.GetUserAccountByName(accName);
                if (account == null)
                {
                    account = new UserAccount
                    {
                        Name = accName,
                        UUID = !string.IsNullOrWhiteSpace(ticket.ClientUuid) ? ticket.ClientUuid : player.UUID,
                        Group = resolvedGroupName,
                        Registered = DateTime.UtcNow.ToString("s"),
                        LastAccessed = DateTime.UtcNow.ToString("s"),
                        KnownIps = "[]"
                    };

                    TryInsertMirrorAccount(account);
                    account = TShock.UserAccounts.GetUserAccountByName(accName) ?? account;
                }
                else if (!string.IsNullOrWhiteSpace(resolvedGroupName) &&
                         !string.Equals(account.Group, resolvedGroupName, StringComparison.OrdinalIgnoreCase))
                {
                    TrySetUserGroup(account, resolvedGroupName);
                    account.Group = resolvedGroupName;
                }

                if (group != null)
                {
                    player.Group = group;
                }

                player.Account = account;
                player.IsLoggedIn = true;
                TrackSlotPlayer(player);

                string effectiveSscJson = string.Empty;
                bool sscDbApplied = false;

                // Lindungi SSC dari paket handshake replay & pre-apply sebelum TShock.OnGreetPlayer berjalan
                if (Main.ServerSideCharacter && player.TPlayer != null)
                {
                    player.IgnoreSSCPackets = true;
                    _sscRestoreGuardUntil[player.Name] = DateTime.UtcNow.AddSeconds(4);

                    effectiveSscJson = ticket.SscSnapshotJson ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(effectiveSscJson))
                    {
                        if (!IsChildProcess)
                        {
                            effectiveSscJson = SscStore?.Get(accName)?.SnapshotJson ?? string.Empty;
                        }
                        else
                        {
                            try
                            {
                                var sscRes = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                                {
                                    Action = "load_ssc",
                                    AccountName = accName,
                                    PlayerName = player.Name
                                }, timeoutMs: 1500).GetAwaiter().GetResult();
                                if (sscRes.Success && !string.IsNullOrWhiteSpace(sscRes.SscSnapshotJson))
                                {
                                    effectiveSscJson = sscRes.SscSnapshotJson;
                                }
                            }
                            catch { }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(effectiveSscJson) || !_pendingSscRestores.ContainsKey(player.Name))
                    {
                        _pendingSscRestores[player.Name] = effectiveSscJson;
                    }

                    // Pre-apply ke TPlayer + local CharacterDB sebelum OnNetGreetPlayer TShock berjalan!
                    if (!string.IsNullOrWhiteSpace(effectiveSscJson))
                    {
                        ApplySscJsonToMemoryAndLocalDb(player, effectiveSscJson, out sscDbApplied);
                    }
                }

                if (freshlyClaimed)
                {
                    TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Auto-Auth '{player.Name}' ({CurrentWorldId}) -> Group '{player.Group?.Name ?? resolvedGroupName}', AccId={player.Account?.ID ?? 0}, SSC={(string.IsNullOrEmpty(effectiveSscJson) ? "0B" : $"{effectiveSscJson.Length}B")}, DbSaved={sscDbApplied}.");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal menerapkan auto-auth ticket untuk '{player.Name}': {ex.Message}");
            }

            return true;
        }

        private class TransferSscItem
        {
            public int Type { get; set; }
            public int Stack { get; set; }
            public byte Prefix { get; set; }
        }

        private class TransferSscSnapshot
        {
            public int Health { get; set; }
            public int MaxHealth { get; set; }
            public int Mana { get; set; }
            public int MaxMana { get; set; }
            public bool ExtraAccessory { get; set; }
            public bool UnlockedBiomeTorches { get; set; }
            public int AnglerQuestsFinished { get; set; }
            public TransferSscItem? TrashItem { get; set; }
            public List<TransferSscItem> Inventory { get; set; } = new();
            public List<TransferSscItem> Armor { get; set; } = new();
            public List<TransferSscItem> Dye { get; set; } = new();
            public List<TransferSscItem> MiscEquips { get; set; } = new();
            public List<TransferSscItem> MiscDyes { get; set; } = new();
            public List<TransferSscItem> Bank { get; set; } = new();
            public List<TransferSscItem> Bank2 { get; set; } = new();
            public List<TransferSscItem> Bank3 { get; set; } = new();
            public List<TransferSscItem> Bank4 { get; set; } = new();
        }

        private static TransferSscItem CaptureSingleItem(Item? it)
        {
            if (it != null && !it.IsAir && it.type > 0 && it.stack > 0)
            {
                return new TransferSscItem { Type = it.type, Stack = it.stack, Prefix = it.prefix };
            }
            return new TransferSscItem();
        }

        private static void RestoreSingleItem(Item? target, TransferSscItem? source)
        {
            if (target == null) return;
            if (source != null && source.Type > 0 && source.Stack > 0)
            {
                target.SetDefaults(source.Type);
                target.stack = source.Stack;
                if (source.Prefix > 0) target.Prefix(source.Prefix);
            }
            else
            {
                target.SetDefaults(0);
                target.stack = 0;
                target.prefix = 0;
            }
        }

        private static List<TransferSscItem> CaptureItemArray(Item[]? items)
        {
            var list = new List<TransferSscItem>();
            if (items == null) return list;
            for (int i = 0; i < items.Length; i++)
            {
                list.Add(CaptureSingleItem(items[i]));
            }
            return list;
        }

        private static void RestoreItemArray(Item[]? target, List<TransferSscItem>? source)
        {
            if (target == null || source == null) return;
            for (int i = 0; i < target.Length; i++)
            {
                if (target[i] == null) target[i] = new Item();
                RestoreSingleItem(target[i], i < source.Count ? source[i] : null);
            }
        }

        private static string SerializePlayerSscSnapshot(TSPlayer? player, bool forceLoggedIn = false, string? accountNameHint = null, string? playerNameHint = null)
        {
            if (player == null || player.TPlayer == null || !Main.ServerSideCharacter)
            {
                return string.Empty;
            }

            string effectiveName = !string.IsNullOrWhiteSpace(player.Name) ? player.Name : (playerNameHint ?? string.Empty);
            if (string.IsNullOrWhiteSpace(effectiveName))
            {
                return string.Empty;
            }

            bool isLoggedIn = player.IsLoggedIn || forceLoggedIn;
            if (!isLoggedIn)
            {
                return string.Empty;
            }

            if (player.Account == null)
            {
                string lookupAcc = !string.IsNullOrWhiteSpace(accountNameHint) ? accountNameHint : effectiveName;
                player.Account = TShock.UserAccounts.GetUserAccountByName(lookupAcc)
                    ?? (!lookupAcc.Equals(effectiveName, StringComparison.OrdinalIgnoreCase)
                        ? TShock.UserAccounts.GetUserAccountByName(effectiveName)
                        : null);
            }
            if (player.Account == null)
            {
                return string.Empty;
            }

            try
            {
                var tp = player.TPlayer;
                var snap = new TransferSscSnapshot
                {
                    Health = Math.Max(1, tp.statLife),
                    MaxHealth = Math.Max(100, tp.statLifeMax),
                    Mana = Math.Max(0, tp.statMana),
                    MaxMana = Math.Max(20, tp.statManaMax),
                    ExtraAccessory = tp.extraAccessory,
                    UnlockedBiomeTorches = tp.unlockedBiomeTorches,
                    AnglerQuestsFinished = tp.anglerQuestsFinished,
                    TrashItem = CaptureSingleItem(tp.trashItem),
                    Inventory = CaptureItemArray(tp.inventory),
                    Armor = CaptureItemArray(tp.armor),
                    Dye = CaptureItemArray(tp.dye),
                    MiscEquips = CaptureItemArray(tp.miscEquips),
                    MiscDyes = CaptureItemArray(tp.miscDyes),
                    Bank = CaptureItemArray(tp.bank?.item),
                    Bank2 = CaptureItemArray(tp.bank2?.item),
                    Bank3 = CaptureItemArray(tp.bank3?.item),
                    Bank4 = CaptureItemArray(tp.bank4?.item)
                };

                return JsonConvert.SerializeObject(snap);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal serialize SSC '{effectiveName}': {ex.Message}");
                return string.Empty;
            }
        }

        private static string CapturePlayerSscJson(TSPlayer? player)
        {
            if (player == null || player.TPlayer == null || !Main.ServerSideCharacter || string.IsNullOrWhiteSpace(player.Name))
            {
                return string.Empty;
            }

            if (!player.IsLoggedIn)
            {
                return string.Empty;
            }

            player.Account ??= TShock.UserAccounts.GetUserAccountByName(player.Name);
            if (player.Account == null)
            {
                return string.Empty;
            }

            string json = SerializePlayerSscSnapshot(player);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    if (player.Account != null && player.Account.ID > 0)
                    {
                        player.PlayerData ??= GetPlayerDataWithRetry(player, player.Account.ID);
                        if (player.PlayerData != null)
                        {
                            player.PlayerData.CopyCharacter(player);
                            ForceWriteLocalCharacterDb(player, bypassPermissionGate: true);
                        }
                    }

                    string accName = player.Account?.Name ?? player.Name;
                    _lastSavedSscJson[accName] = json;
                    if (!IsChildProcess)
                    {
                        SscStore?.Save(accName, player.Name, CurrentWorldId, json);
                    }
                }
                catch { }
            }
            return json;
        }

        private static void SavePlayerSscEverywhere(TSPlayer? player, bool synchronousIpc, bool forceLoggedIn = false, string? accountNameHint = null, string? playerNameHint = null)
        {
            if (player == null || player.TPlayer == null || !Main.ServerSideCharacter)
            {
                return;
            }

            string effectiveName = !string.IsNullOrWhiteSpace(player.Name) ? player.Name : (playerNameHint ?? string.Empty);
            if (string.IsNullOrWhiteSpace(effectiveName))
            {
                return;
            }
            if (!synchronousIpc && (_pendingSscRestores.ContainsKey(effectiveName) || IsSscRestoreGuarded(effectiveName)))
            {
                return;
            }

            bool isLoggedIn = player.IsLoggedIn || forceLoggedIn;
            if (!isLoggedIn)
            {
                return;
            }

            if (player.Account == null)
            {
                string lookupAcc = !string.IsNullOrWhiteSpace(accountNameHint) ? accountNameHint : effectiveName;
                player.Account = TShock.UserAccounts.GetUserAccountByName(lookupAcc)
                    ?? (!lookupAcc.Equals(effectiveName, StringComparison.OrdinalIgnoreCase)
                        ? TShock.UserAccounts.GetUserAccountByName(effectiveName)
                        : null);
            }
            if (player.Account == null)
            {
                if (synchronousIpc)
                {
                    TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Skip save SSC on leave untuk '{effectiveName}' ({CurrentWorldId}): Account null.");
                }
                return;
            }

            string json = SerializePlayerSscSnapshot(player, forceLoggedIn: isLoggedIn, accountNameHint: accountNameHint, playerNameHint: effectiveName);
            if (string.IsNullOrWhiteSpace(json)) return;

            string accName = player.Account?.Name ?? (!string.IsNullOrWhiteSpace(accountNameHint) ? accountNameHint : effectiveName);
            bool localChanged = !_lastSavedSscJson.TryGetValue(accName, out var prevJson) ||
                                !string.Equals(prevJson, json, StringComparison.Ordinal);
            bool ipcNeeded = IsChildProcess &&
                             (!_lastMasterSyncedSscJson.TryGetValue(accName, out var prevSynced) ||
                              !string.Equals(prevSynced, json, StringComparison.Ordinal));

            if (!synchronousIpc && !localChanged && !ipcNeeded)
            {
                return;
            }

            if (synchronousIpc || localChanged)
            {
                _lastSavedSscJson[accName] = json;

                try
                {
                    if (player.Account != null && player.Account.ID > 0)
                    {
                        player.PlayerData ??= GetPlayerDataWithRetry(player, player.Account.ID);
                        if (player.PlayerData != null)
                        {
                            player.PlayerData.CopyCharacter(player);
                            ForceWriteLocalCharacterDb(player, bypassPermissionGate: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal save local DB '{effectiveName}': {ex.Message}");
                }
            }

            if (!IsChildProcess)
            {
                if (synchronousIpc || localChanged)
                {
                    SscStore?.Save(accName, effectiveName, CurrentWorldId, json, markLeaveSynced: synchronousIpc);
                }
            }
            else
            {
                var req = new IpcRequest
                {
                    Action = "save_ssc",
                    AccountName = accName,
                    PlayerName = effectiveName,
                    WorldId = CurrentWorldId,
                    AdminSubAction = synchronousIpc ? "leave_sync" : string.Empty,
                    SscSnapshotJson = json
                };

                if (synchronousIpc)
                {
                    bool ok = false;
                    string lastErr = string.Empty;
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            var res = MultiWorldIpcClient.SendAsync(MasterIpcPort, req, timeoutMs: 3500).GetAwaiter().GetResult();
                            if (res.Success)
                            {
                                ok = true;
                                _lastMasterSyncedSscJson[accName] = json;
                                break;
                            }
                            lastErr = res.Message;
                        }
                        catch (Exception ex)
                        {
                            lastErr = ex.Message;
                        }

                        if (attempt < 3)
                        {
                            System.Threading.Thread.Sleep(150);
                        }
                    }

                    if (ok)
                    {
                        TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Synced SSC '{effectiveName}' ({CurrentWorldId}) -> Master/Lobby on leave (len={json.Length}).");
                    }
                    else
                    {
                        TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal sync IPC save_ssc on leave '{effectiveName}' ({CurrentWorldId}): {lastErr}");
                    }
                }
                else if (ipcNeeded)
                {
                    if (_inFlightMasterSyncs.TryGetValue(accName, out var sendingJson) &&
                        string.Equals(sendingJson, json, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _inFlightMasterSyncs[accName] = json;
                    _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, req, timeoutMs: 2500).ContinueWith(t =>
                    {
                        _inFlightMasterSyncs.TryRemove(accName, out _);
                        if (t.IsCompletedSuccessfully && t.Result.Success)
                        {
                            _lastMasterSyncedSscJson[accName] = json;
                        }
                    }, TaskScheduler.Default);
                }
            }
        }

        private static void SaveAllActivePlayersSscSync()
        {
            if (!Main.ServerSideCharacter) return;
            try
            {
                var handledNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in TShock.Players)
                {
                    if (p != null && p.TPlayer != null && !string.IsNullOrWhiteSpace(p.Name) && (p.IsLoggedIn || p.Account != null))
                    {
                        handledNames.Add(p.Name);
                        SavePlayerSscEverywhere(p, synchronousIpc: true, forceLoggedIn: p.IsLoggedIn, accountNameHint: p.Account?.Name, playerNameHint: p.Name);
                    }
                }

                foreach (var kv in _trackedSlotPlayers.ToArray())
                {
                    var tracked = kv.Value;
                    if (tracked?.Player == null || tracked.Player.TPlayer == null) continue;
                    string pName = !string.IsNullOrWhiteSpace(tracked.Player.Name) ? tracked.Player.Name : tracked.PlayerName;
                    if (string.IsNullOrWhiteSpace(pName) || !handledNames.Add(pName)) continue;
                    if (!tracked.WasLoggedIn && !tracked.Player.IsLoggedIn) continue;

                    SavePlayerSscEverywhere(
                        tracked.Player,
                        synchronousIpc: true,
                        forceLoggedIn: tracked.WasLoggedIn,
                        accountNameHint: !string.IsNullOrWhiteSpace(tracked.AccountName) ? tracked.AccountName : null,
                        playerNameHint: pName);
                }
            }
            catch { }
        }

        private static void ApplySnapshotToTerrariaPlayer(Terraria.Player tp, TransferSscSnapshot snap)
        {
            tp.statLifeMax = Math.Max(100, snap.MaxHealth);
            tp.statLife = Math.Clamp(snap.Health, 1, tp.statLifeMax);
            tp.statManaMax = Math.Max(20, snap.MaxMana);
            tp.statMana = Math.Clamp(snap.Mana, 0, tp.statManaMax);
            tp.extraAccessory = snap.ExtraAccessory;
            tp.unlockedBiomeTorches = snap.UnlockedBiomeTorches;
            tp.anglerQuestsFinished = snap.AnglerQuestsFinished;

            if (snap.TrashItem != null)
            {
                tp.trashItem ??= new Item();
                RestoreSingleItem(tp.trashItem, snap.TrashItem);
            }

            RestoreItemArray(tp.inventory, snap.Inventory);
            RestoreItemArray(tp.armor, snap.Armor);
            RestoreItemArray(tp.dye, snap.Dye);
            RestoreItemArray(tp.miscEquips, snap.MiscEquips);
            RestoreItemArray(tp.miscDyes, snap.MiscDyes);
            RestoreItemArray(tp.bank?.item, snap.Bank);
            RestoreItemArray(tp.bank2?.item, snap.Bank2);
            RestoreItemArray(tp.bank3?.item, snap.Bank3);
            RestoreItemArray(tp.bank4?.item, snap.Bank4);
        }

        private static bool ApplySscJsonToMemoryAndLocalDb(TSPlayer player, string sscJson)
        {
            return ApplySscJsonToMemoryAndLocalDb(player, sscJson, out _);
        }

        private static bool ApplySscJsonToMemoryAndLocalDb(TSPlayer player, string sscJson, out bool dbSaved)
        {
            dbSaved = false;
            if (player == null || player.TPlayer == null || string.IsNullOrWhiteSpace(sscJson)) return false;

            try
            {
                var snap = JsonConvert.DeserializeObject<TransferSscSnapshot>(sscJson);
                if (snap == null) return false;

                ApplySnapshotToTerrariaPlayer(player.TPlayer, snap);

                if (player.Account != null && player.Account.ID > 0)
                {
                    player.PlayerData ??= GetPlayerDataWithRetry(player, player.Account.ID);
                    if (player.PlayerData != null)
                    {
                        player.PlayerData.CopyCharacter(player);
                        dbSaved = ForceWriteLocalCharacterDb(player, bypassPermissionGate: true);
                    }
                    _lastSavedSscJson[player.Account.Name] = sscJson;
                    if (IsChildProcess)
                    {
                        _lastMasterSyncedSscJson[player.Account.Name] = sscJson;
                    }
                }
                else
                {
                    TShock.Log.ConsoleWarn($"[Univers.MultiWorld] SSC warning '{player.Name}' ({CurrentWorldId}): Account belum valid (ID={player.Account?.ID ?? -1}), skip tulis SQLite lokal.");
                }
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal parse SSC '{player.Name}': {ex.Message}");
                return false;
            }
        }

        private static string ResolveLatestMasterSscSnapshot(string accountName, string playerName)
        {
            if (IsChildProcess) return string.Empty;

            var rec = !string.IsNullOrWhiteSpace(accountName) ? SscStore?.Get(accountName) : null;
            if (rec == null && !string.IsNullOrWhiteSpace(playerName))
            {
                rec = SscStore?.Get(playerName);
            }

            if (rec != null &&
                !string.IsNullOrWhiteSpace(rec.ActiveWorldId) &&
                !rec.ActiveWorldId.Equals(Config.HubWorldId, StringComparison.OrdinalIgnoreCase) &&
                !rec.LeaveSynced)
            {
                string childJson = TryReadChildCharacterSnapshotFromSqlite(rec.ActiveWorldId, rec.AccountName, playerName);
                if (!string.IsNullOrWhiteSpace(childJson))
                {
                    SscStore?.Save(rec.AccountName, playerName, rec.ActiveWorldId, childJson, markLeaveSynced: true);
                    TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Recovered latest SQLite SSC '{rec.AccountName}' dari '{rec.ActiveWorldId}' -> Lobby.");
                    return childJson;
                }
            }

            return rec?.SnapshotJson ?? string.Empty;
        }

        private static string TryReadChildCharacterSnapshotFromSqlite(string worldId, string accountName, string playerName)
        {
            if (IsChildProcess || string.IsNullOrWhiteSpace(worldId) || TShock.DB == null) return string.Empty;

            try
            {
                string childDbPath = Path.Combine(_multiWorldRoot, "Instances", worldId, "config", "tshock.sqlite");
                if (!File.Exists(childDbPath)) return string.Empty;

                using var conn = Activator.CreateInstance(TShock.DB.GetType(), $"Data Source={childDbPath}") as IDbConnection;
                if (conn == null) return string.Empty;
                conn.Open();

                int childAccId = 0;
                foreach (var lookup in new[] { accountName, playerName }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    using var uCmd = conn.CreateCommand();
                    uCmd.CommandText = "SELECT ID FROM Users WHERE LOWER(Username) = LOWER(@uname) LIMIT 1";
                    var p = uCmd.CreateParameter();
                    p.ParameterName = "@uname";
                    p.Value = lookup.Trim();
                    uCmd.Parameters.Add(p);

                    object? res = uCmd.ExecuteScalar();
                    if (res != null && res != DBNull.Value && int.TryParse(res.ToString(), out int idVal) && idVal > 0)
                    {
                        childAccId = idVal;
                        break;
                    }
                }

                if (childAccId <= 0) return string.Empty;

                using var cCmd = conn.CreateCommand();
                cCmd.CommandText = "SELECT Health, MaxHealth, Mana, MaxMana, Inventory, extraSlot, unlockedBiomeTorches, questsCompleted FROM tsCharacter WHERE Account = @accId LIMIT 1";
                var accParam = cCmd.CreateParameter();
                accParam.ParameterName = "@accId";
                accParam.Value = childAccId;
                cCmd.Parameters.Add(accParam);

                using var reader = cCmd.ExecuteReader();
                if (!reader.Read()) return string.Empty;

                int health = reader.IsDBNull(0) ? 100 : Convert.ToInt32(reader.GetValue(0));
                int maxHealth = reader.IsDBNull(1) ? 100 : Convert.ToInt32(reader.GetValue(1));
                int mana = reader.IsDBNull(2) ? 20 : Convert.ToInt32(reader.GetValue(2));
                int maxMana = reader.IsDBNull(3) ? 20 : Convert.ToInt32(reader.GetValue(3));
                string invRaw = reader.IsDBNull(4) ? string.Empty : (reader.GetString(4) ?? string.Empty);
                bool extraAcc = !reader.IsDBNull(5) && Convert.ToInt32(reader.GetValue(5)) != 0;
                bool unlockedTorches = !reader.IsDBNull(6) && Convert.ToInt32(reader.GetValue(6)) != 0;
                int quests = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7));

                if (string.IsNullOrWhiteSpace(invRaw)) return string.Empty;

                var parsed = new List<TransferSscItem>(260);
                foreach (string token in invRaw.Split('~'))
                {
                    var parts = token.Split(',');
                    if (parts.Length >= 3 &&
                        int.TryParse(parts[0], out int t) &&
                        int.TryParse(parts[1], out int s) &&
                        byte.TryParse(parts[2], out byte pref) &&
                        t > 0 && s > 0)
                    {
                        parsed.Add(new TransferSscItem { Type = t, Stack = s, Prefix = pref });
                    }
                    else
                    {
                        parsed.Add(new TransferSscItem());
                    }
                }

                List<TransferSscItem> Slice(int start, int count)
                {
                    var list = new List<TransferSscItem>(count);
                    for (int i = 0; i < count; i++)
                    {
                        int idx = start + i;
                        list.Add(idx < parsed.Count ? parsed[idx] : new TransferSscItem());
                    }
                    return list;
                }

                var snap = new TransferSscSnapshot
                {
                    Health = Math.Max(1, health),
                    MaxHealth = Math.Max(100, maxHealth),
                    Mana = Math.Max(0, mana),
                    MaxMana = Math.Max(20, maxMana),
                    ExtraAccessory = extraAcc,
                    UnlockedBiomeTorches = unlockedTorches,
                    AnglerQuestsFinished = quests,
                    Inventory = Slice(0, 59),
                    Armor = Slice(59, 20),
                    Dye = Slice(79, 10),
                    MiscEquips = Slice(89, 5),
                    MiscDyes = Slice(94, 5),
                    Bank = Slice(99, 40),
                    Bank2 = Slice(139, 40),
                    TrashItem = 179 < parsed.Count ? parsed[179] : new TransferSscItem(),
                    Bank3 = Slice(180, 40),
                    Bank4 = Slice(220, 40)
                };

                return JsonConvert.SerializeObject(snap);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal baca child SQLite '{worldId}' untuk '{accountName}': {ex.Message}");
                return string.Empty;
            }
        }

        private static void EnsureMasterCharacterDbSyncedForJoin(string playerName, string? clientUuid)
        {
            if (IsChildProcess || !Main.ServerSideCharacter || string.IsNullOrWhiteSpace(playerName)) return;

            try
            {
                var acc = TShock.UserAccounts.GetUserAccountByName(playerName);
                if (acc == null && !string.IsNullOrWhiteSpace(clientUuid) && TShock.DB != null)
                {
                    try
                    {
                        using var uReader = TShock.DB.QueryReader("SELECT Username FROM Users WHERE UUID = @0 LIMIT 1", clientUuid);
                        if (uReader.Read())
                        {
                            string uName = uReader.Get<string>("Username");
                            if (!string.IsNullOrWhiteSpace(uName))
                            {
                                acc = TShock.UserAccounts.GetUserAccountByName(uName);
                            }
                        }
                    }
                    catch { }
                }
                if (acc == null || acc.ID <= 0) return;

                string latestJson = ResolveLatestMasterSscSnapshot(acc.Name, playerName);
                if (string.IsNullOrWhiteSpace(latestJson)) return;

                SyncSscJsonToMasterCharacterDbImmediate(acc.Name, playerName, latestJson);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] EnsureMasterCharacterDbSyncedForJoin '{playerName}' error: {ex.Message}");
            }
        }

        private static bool SyncSscJsonToMasterCharacterDbImmediate(string accountName, string playerName, string sscJson, bool forceOnlineRefresh = false)
        {
            if (IsChildProcess || !Main.ServerSideCharacter || string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(sscJson))
            {
                return false;
            }

            try
            {
                var online = TShock.Players.FirstOrDefault(p =>
                    p != null && p.Active && p.RealPlayer && p.IsLoggedIn &&
                    string.Equals(p.Account?.Name ?? p.Name, accountName, StringComparison.OrdinalIgnoreCase));
                if (online != null)
                {
                    bool justJoined = !string.IsNullOrWhiteSpace(online.Name) &&
                                      _playerJoinedUtc.TryGetValue(online.Name, out var joinedAt) &&
                                      (DateTime.UtcNow - joinedAt).TotalSeconds <= 5.0;
                    if (forceOnlineRefresh && justJoined)
                    {
                        online.IgnoreSSCPackets = true;
                        _sscRestoreGuardUntil[online.Name] = DateTime.UtcNow.AddSeconds(2);
                        _pendingSscRestores[online.Name] = sscJson;
                        ApplyPendingSscRestore(online, clearPendingAndGuard: true);
                        return true;
                    }
                    if (!_pendingSscRestores.ContainsKey(online.Name) && !IsSscRestoreGuarded(online.Name))
                    {
                        return ApplySscJsonToMemoryAndLocalDb(online, sscJson, out _);
                    }
                    return false;
                }

                var acc = TShock.UserAccounts.GetUserAccountByName(accountName);
                if (acc == null && !string.IsNullOrWhiteSpace(playerName))
                {
                    acc = TShock.UserAccounts.GetUserAccountByName(playerName);
                }
                if (acc == null || acc.ID <= 0) return false;

                var snap = JsonConvert.DeserializeObject<TransferSscSnapshot>(sscJson);
                if (snap == null) return false;

                _scratchSyncPlayer ??= new Terraria.Player();
                _scratchSyncPlayer.name = string.IsNullOrWhiteSpace(playerName) ? acc.Name : playerName;
                ApplySnapshotToTerrariaPlayer(_scratchSyncPlayer, snap);

                var prevSlot255 = Main.player[255];
                try
                {
                    Main.player[255] = _scratchSyncPlayer;
                    var writerTs = new TSPlayer(255)
                    {
                        Account = acc,
                        IsLoggedIn = true,
                        Group = new Group("ssc_sync_writer")
                    };
                    writerTs.PlayerData = GetPlayerDataWithRetry(writerTs, acc.ID);
                    if (writerTs.PlayerData != null)
                    {
                        writerTs.PlayerData.CopyCharacter(writerTs);
                        bool ok = ForceWriteLocalCharacterDb(writerTs, bypassPermissionGate: true);
                        if (ok)
                        {
                            _lastSavedSscJson[acc.Name] = sscJson;
                        }
                        return ok;
                    }
                    return false;
                }
                finally
                {
                    Main.player[255] = prevSlot255;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Sync SSC ke SQLite utama gagal untuk '{accountName}': {ex.Message}");
                return false;
            }
        }

        private static Task<bool> SyncSscJsonToMasterCharacterDbAsync(string accountName, string playerName, string sscJson, bool forceOnlineRefresh = false)
        {
            if (IsChildProcess || !Main.ServerSideCharacter || string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(sscJson))
            {
                return Task.FromResult(false);
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _mainThreadQueue.Enqueue(() =>
            {
                bool ok = false;
                try
                {
                    ok = SyncSscJsonToMasterCharacterDbImmediate(accountName, playerName, sscJson, forceOnlineRefresh);
                }
                finally
                {
                    tcs.TrySetResult(ok);
                }
            });

            return tcs.Task;
        }

        private static void SyncSscJsonToMasterCharacterDb(string accountName, string playerName, string sscJson)
        {
            _ = SyncSscJsonToMasterCharacterDbAsync(accountName, playerName, sscJson, forceOnlineRefresh: false);
        }

        private static void ApplyPendingSscRestore(TSPlayer player, bool clearPendingAndGuard = true)
        {
            if (player == null || !player.Active || player.TPlayer == null) return;

            string? sscJson;
            bool hadPending = clearPendingAndGuard
                ? _pendingSscRestores.TryRemove(player.Name, out sscJson)
                : _pendingSscRestores.TryGetValue(player.Name, out sscJson);

            if (!Main.ServerSideCharacter)
            {
                player.IgnoreSSCPackets = false;
                _sscRestoreGuardUntil.TryRemove(player.Name, out _);
                return;
            }

            try
            {
                if (player.Account != null && player.Account.ID > 0 && player.PlayerData == null)
                {
                    player.PlayerData = GetPlayerDataWithRetry(player, player.Account.ID);
                }

                bool restoredOk = false;
                bool dbSaved = false;

                if (hadPending && !string.IsNullOrWhiteSpace(sscJson) && ApplySscJsonToMemoryAndLocalDb(player, sscJson, out dbSaved))
                {
                    restoredOk = true;
                    if (player.PlayerData != null)
                    {
                        try
                        {
                            player.PlayerData.CopyCharacter(player);
                            player.PlayerData.RestoreCharacter(player);
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleWarn($"[Univers.MultiWorld] RestoreCharacter fallback '{player.Name}' ({CurrentWorldId}): {ex.Message}");
                        }
                        if (!dbSaved)
                        {
                            dbSaved = ForceWriteLocalCharacterDb(player, bypassPermissionGate: true);
                        }
                    }

                    if (clearPendingAndGuard)
                    {
                        TShock.Log.ConsoleInfo($"[Univers.MultiWorld] SSC Restore '{player.Name}' ({CurrentWorldId}): len={sscJson.Length}, dbSaved={dbSaved}.");
                    }
                }
                else if (player.Account != null && player.Account.ID > 0)
                {
                    var latestData = GetPlayerDataWithRetry(player, player.Account.ID);
                    if (latestData != null && latestData.exists)
                    {
                        player.PlayerData = latestData;
                        try
                        {
                            latestData.RestoreCharacter(player);
                            restoredOk = true;
                            if (clearPendingAndGuard)
                            {
                                TShock.Log.ConsoleInfo($"[Univers.MultiWorld] SSC DB Fallback Restore '{player.Name}' ({CurrentWorldId}) OK.");
                            }
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleWarn($"[Univers.MultiWorld] RestoreCharacter DB fallback '{player.Name}' ({CurrentWorldId}): {ex.Message}");
                        }
                    }
                }

                if (restoredOk)
                {
                    try
                    {
                        NetMessage.SendData((int)PacketTypes.PlayerInfo, -1, -1, null, player.Index);
                        NetMessage.SendData((int)PacketTypes.PlayerHp, -1, -1, null, player.Index);
                        NetMessage.SendData((int)PacketTypes.PlayerMana, -1, -1, null, player.Index);
                    }
                    catch { }

                    for (int s = 0; s < 260; s++)
                    {
                        try
                        {
                            player.SendData(PacketTypes.PlayerSlot, "", player.Index, s);
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (player.TPlayer != null && player.TPlayer.selectedItem >= 0 && player.TPlayer.selectedItem < 59)
                    {
                        try
                        {
                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, player.TPlayer.selectedItem);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal restore SSC '{player.Name}': {ex}");
            }
            finally
            {
                if (clearPendingAndGuard)
                {
                    _pendingSscRestores.TryRemove(player.Name, out _);
                    _sscRestoreGuardUntil.TryRemove(player.Name, out _);
                    player.IgnoreSSCPackets = false;
                }
                else
                {
                    player.IgnoreSSCPackets = true;
                }
            }
        }

        private static PlayerData? GetPlayerDataWithRetry(TSPlayer player, int accountId)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    return TShock.CharacterDB.GetPlayerData(player, accountId);
                }
                catch
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
            return null;
        }

        private static bool ForceWriteLocalCharacterDb(TSPlayer player, bool bypassPermissionGate = false)
        {
            if (player == null || player.Account == null || player.Account.ID <= 0 || player.PlayerData == null)
            {
                return false;
            }

            int idx = player.Index;
            bool hasNetClient = idx >= 0 && idx < Netplay.Clients.Length && Netplay.Clients[idx] != null;
            bool createdTempClient = false;
            if (!hasNetClient && idx >= 0 && idx < Netplay.Clients.Length)
            {
                Netplay.Clients[idx] = new Terraria.RemoteClient();
                hasNetClient = true;
                createdTempClient = true;
            }
            int prevState = hasNetClient ? Netplay.Clients[idx].State : 0;
            Group? prevGroup = player.Group;
            bool prevLoggedIn = player.IsLoggedIn;

            try
            {
                player.IsLoggedIn = true;
                if (hasNetClient && prevState < 10)
                {
                    Netplay.Clients[idx].State = 10;
                }
                if (bypassPermissionGate)
                {
                    player.Group = new Group("univers_ssc_writer");
                }

                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        if (TShock.CharacterDB.InsertPlayerData(player))
                        {
                            player.PlayerData.exists = true;
                            return true;
                        }
                    }
                    catch
                    {
                        // Refresh exists flag jika row ternyata sudah ada di SQLite
                        var fresh = GetPlayerDataWithRetry(player, player.Account.ID);
                        if (fresh != null)
                        {
                            fresh.CopyCharacter(player);
                            player.PlayerData = fresh;
                        }
                    }
                    System.Threading.Thread.Sleep(25);
                }

                // Fallback SQL langsung jika TShock.CharacterDB.InsertPlayerData mengembalikan false
                if (TShock.DB != null && player.PlayerData.inventory != null && player.TPlayer != null)
                {
                    try
                    {
                        string invStr = string.Join("~", player.PlayerData.inventory.Select(it => $"{it.NetId},{it.Stack},{it.PrefixId}"));
                        var tp = player.TPlayer;
                        int updated = TShock.DB.Query(
                            "UPDATE tsCharacter SET Health = @0, MaxHealth = @1, Mana = @2, MaxMana = @3, Inventory = @4, extraSlot = @5, unlockedBiomeTorches = @6, questsCompleted = @7 WHERE Account = @8",
                            Math.Max(1, tp.statLife),
                            Math.Max(100, tp.statLifeMax),
                            Math.Max(0, tp.statMana),
                            Math.Max(20, tp.statManaMax),
                            invStr,
                            tp.extraAccessory ? 1 : 0,
                            tp.unlockedBiomeTorches ? 1 : 0,
                            tp.anglerQuestsFinished,
                            player.Account.ID);
                        if (updated > 0)
                        {
                            return true;
                        }
                    }
                    catch (Exception sqlEx)
                    {
                        TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Direct tsCharacter UPDATE fallback gagal ({CurrentWorldId}) untuk '{player.Name}': {sqlEx.Message}");
                    }
                }
            }
            finally
            {
                player.IsLoggedIn = prevLoggedIn;
                if (bypassPermissionGate)
                {
                    player.Group = prevGroup;
                }
                if (createdTempClient)
                {
                    Netplay.Clients[idx] = null!;
                }
                else if (hasNetClient && prevState < 10)
                {
                    Netplay.Clients[idx].State = prevState;
                }
            }

            TShock.Log.ConsoleWarn($"[Univers.MultiWorld] InsertPlayerData gagal menulis ke SQLite ({CurrentWorldId}) untuk '{player.Name}' (AccId={player.Account.ID}, exists={player.PlayerData.exists}).");
            return false;
        }

        public static bool ResetOnlinePlayerSsc(TSPlayer target, out string message)
        {
            if (target == null || !target.Active || !target.RealPlayer || target.TPlayer == null)
            {
                message = "Player harus online di dalam game.";
                return false;
            }
            if (!Main.ServerSideCharacter)
            {
                message = "ServerSideCharacter sedang nonaktif di sscconfig.json.";
                return false;
            }
            if (!target.IsLoggedIn || target.Account == null)
            {
                message = $"Player '{target.Name}' belum login ke akun TShock.";
                return false;
            }

            try
            {
                var tp = target.TPlayer;
                tp.statLifeMax = 100;
                tp.statLife = 100;
                tp.statManaMax = 20;
                tp.statMana = 20;
                tp.extraAccessory = false;
                tp.unlockedBiomeTorches = false;
                tp.anglerQuestsFinished = 0;

                static void ClearItems(Item[]? arr)
                {
                    if (arr == null) return;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i] == null) arr[i] = new Item();
                        arr[i].SetDefaults(0);
                        arr[i].stack = 0;
                        arr[i].prefix = 0;
                    }
                }

                ClearItems(tp.inventory);
                ClearItems(tp.armor);
                ClearItems(tp.dye);
                ClearItems(tp.miscEquips);
                ClearItems(tp.miscDyes);
                ClearItems(tp.bank?.item);
                ClearItems(tp.bank2?.item);
                ClearItems(tp.bank3?.item);
                ClearItems(tp.bank4?.item);

                if (tp.inventory != null && tp.inventory.Length >= 3)
                {
                    tp.inventory[0].SetDefaults(ItemID.CopperShortsword);
                    tp.inventory[0].stack = 1;
                    tp.inventory[1].SetDefaults(ItemID.CopperPickaxe);
                    tp.inventory[1].stack = 1;
                    tp.inventory[2].SetDefaults(ItemID.CopperAxe);
                    tp.inventory[2].stack = 1;
                }

                target.PlayerData ??= GetPlayerDataWithRetry(target, target.Account.ID);
                if (target.PlayerData != null)
                {
                    target.PlayerData.CopyCharacter(target);
                    target.PlayerData.RestoreCharacter(target);
                    ForceWriteLocalCharacterDb(target, bypassPermissionGate: true);
                }
                else
                {
                    NetMessage.SendData((int)PacketTypes.PlayerHp, -1, -1, null, target.Index);
                    NetMessage.SendData((int)PacketTypes.PlayerMana, -1, -1, null, target.Index);
                    for (int i = 0; i < 59; i++)
                    {
                        NetMessage.SendData((int)PacketTypes.PlayerSlot, target.Index, -1, null, target.Index, i);
                    }
                }

                string resetSnap = SerializePlayerSscSnapshot(target);
                if (!string.IsNullOrWhiteSpace(resetSnap))
                {
                    string accName = target.Account.Name;
                    _lastSavedSscJson[accName] = resetSnap;
                    if (!IsChildProcess)
                    {
                        SscStore?.Save(accName, target.Name, CurrentWorldId, resetSnap);
                    }
                    else
                    {
                        _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "save_ssc",
                            AccountName = accName,
                            PlayerName = target.Name,
                            WorldId = CurrentWorldId,
                            SscSnapshotJson = resetSnap
                        });
                    }
                }

                message = $"SSC '{target.Name}' berhasil di-reset ke karakter awal (100 HP, 20 Mana, Copper Tools) & disimpan ke DB.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Gagal reset SSC '{target.Name}': {ex.Message}";
                return false;
            }
        }

        public static async Task ExecuteResetSscCrossWorldAsync(TSPlayer executor, string playerName)
        {
            var worlds = await FetchAllWorldSnapshotsAsync();
            var targetWorld = worlds.FirstOrDefault(w =>
                w.OnlinePlayerNames.Any(n => n.Equals(playerName, StringComparison.OrdinalIgnoreCase)));
            if (targetWorld == null)
            {
                executor.SendErrorMessage($"[MultiWorld] Player '{playerName}' tidak ditemukan online di world manapun.");
                return;
            }

            await ExecuteRemoteCommandAsync(executor, targetWorld.Id, $"mw resetssc \"{playerName}\"");
        }

        private static Group? EnsureSyncedGroup(string groupName, bool hasWildcard, string permissionsCsv)
        {
            var group = TShock.Groups.GetGroupByName(groupName);
            if (group == null && !groupName.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                string permsToSeed = hasWildcard ? "*" : (permissionsCsv ?? string.Empty);
                try
                {
                    MethodInfo? addGrp = TShock.Groups.GetType().GetMethod("AddGroup", new[] { typeof(string), typeof(string), typeof(string), typeof(string) });
                    addGrp?.Invoke(TShock.Groups, new object?[] { groupName, null, permsToSeed, Group.defaultChatColor });
                    group = TShock.Groups.GetGroupByName(groupName);
                }
                catch
                {
                }
            }

            if (group == null && hasWildcard)
            {
                group = TShock.Groups.GetGroupByName("superadmin") ?? new SuperAdminGroup();
            }

            if (group != null && hasWildcard && !group.HasPermission("*"))
            {
                try
                {
                    TShock.Groups.AddPermissions(group.Name, new List<string> { "*" });
                    group = TShock.Groups.GetGroupByName(group.Name) ?? new SuperAdminGroup();
                }
                catch
                {
                    group = new SuperAdminGroup();
                }
            }
            else if (group != null && !string.IsNullOrWhiteSpace(permissionsCsv))
            {
                try
                {
                    var missing = permissionsCsv
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(perm => !group.HasPermission(perm))
                        .ToList();
                    if (missing.Count > 0)
                    {
                        TShock.Groups.AddPermissions(group.Name, missing);
                    }
                }
                catch { }
            }

            return group ?? TShock.Groups.GetGroupByName("default");
        }

        private static void TrySetUserGroup(UserAccount account, string groupName)
        {
            try
            {
                var mgr = TShock.UserAccounts;
                MethodInfo? m = mgr.GetType().GetMethod("SetUserGroup", new[] { typeof(UserAccount), typeof(string) });
                m?.Invoke(mgr, new object[] { account, groupName });
            }
            catch
            {
            }
        }

        private static void TryInsertMirrorAccount(UserAccount account)
        {
            try
            {
                MethodInfo? hashMethod = typeof(UserAccount).GetMethod("CreateBCryptHash", new[] { typeof(string) });
                hashMethod?.Invoke(account, new object[] { Guid.NewGuid().ToString("N") });

                var mgr = TShock.UserAccounts;
                MethodInfo? addMethod = mgr.GetType().GetMethod("AddUserAccount", new[] { typeof(UserAccount) })
                    ?? mgr.GetType().GetMethod("InsertNewAccount", new[] { typeof(UserAccount) });

                addMethod?.Invoke(mgr, new object[] { account });
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Mirror account '{account.Name}' warning: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private static void ClearInactiveEntitiesForPlayer(int playerIndex)
        {
            var player = TShock.Players[playerIndex];
            if (player == null || !player.Active || !player.RealPlayer) return;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (!Main.npc[i].active)
                {
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, playerIndex, -1, null, i);
                }
            }

            for (int i = 0; i < Main.maxItems; i++)
            {
                if (!Main.item[i].active)
                {
                    NetMessage.SendData((int)PacketTypes.ItemDrop, playerIndex, -1, null, i);
                }
            }
        }

        #endregion

        #region Master Coordinator Actions & IPC Handler

        private static void ExtractGroupPermissions(string groupName, TSPlayer? playerHint, out bool hasWildcard, out string permsCsv)
        {
            Group? g = playerHint?.Group ?? TShock.Groups.GetGroupByName(groupName);
            hasWildcard = g?.HasPermission("*") ?? false;
            if (!hasWildcard && string.Equals(groupName, "superadmin", StringComparison.OrdinalIgnoreCase))
            {
                hasWildcard = true;
            }
            permsCsv = g != null ? string.Join(",", g.Permissions) : string.Empty;
        }

        public static async Task<(bool Ok, string Message)> TransferPlayerAsync(
            TSPlayer? requester,
            string playerName,
            string accountName,
            string groupName,
            string uuid,
            string targetWorldId)
        {
            string sscSnapshotJson = CapturePlayerSscJson(requester);
            if (string.IsNullOrWhiteSpace(sscSnapshotJson))
            {
                string accKey = !string.IsNullOrWhiteSpace(accountName) ? accountName : playerName;
                if (!IsChildProcess)
                {
                    var rec = SscStore?.Get(accKey) ?? (!string.IsNullOrWhiteSpace(playerName) ? SscStore?.Get(playerName) : null);
                    sscSnapshotJson = rec?.SnapshotJson ?? string.Empty;
                }
                else
                {
                    try
                    {
                        var sscRes = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "load_ssc",
                            AccountName = accKey,
                            PlayerName = playerName
                        }, timeoutMs: 1500).GetAwaiter().GetResult();
                        if (sscRes.Success && !string.IsNullOrWhiteSpace(sscRes.SscSnapshotJson))
                        {
                            sscSnapshotJson = sscRes.SscSnapshotJson;
                        }
                    }
                    catch { }
                }
            }
            ExtractGroupPermissions(groupName, requester, out bool hasWildcard, out string groupPerms);

            if (IsChildProcess)
            {
                _recentTransfers[playerName] = DateTime.UtcNow.AddSeconds(12);
                return await RelayTransferToMasterAsync(playerName, accountName, groupName, uuid, hasWildcard, groupPerms, sscSnapshotJson, targetWorldId);
            }

            var target = Config.GetWorld(targetWorldId);
            if (target == null)
            {
                return (false, $"World '{targetWorldId}' tidak ditemukan. Ketik /mw list untuk melihat daftar world.");
            }

            if (Proxy == null || !Proxy.IsListening)
            {
                return (false, "Gateway Proxy MultiWorld tidak aktif.");
            }

            var hubPlayer = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
            int? hubSlot = hubPlayer?.Index;
            string currentWorldGuess = hubPlayer != null ? CurrentWorldId : string.Empty;

            var session = Proxy.FindSession(playerName, uuid, hubSlot, currentWorldGuess);
            if (session == null)
            {
                int hubPort = ResolveListenPort(Config.MasterWorldPort);
                return (false, $"Kamu terhubung langsung ke port {hubPort} (bukan Gateway Proxy)! Konek ulang di Terraria pakai port {Proxy.ListenPort} agar bisa pindah world.");
            }

            if (session.CurrentWorldId.Equals(target.Id, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Kamu sudah berada di world '{target.DisplayName}'.");
            }

            if (!string.IsNullOrWhiteSpace(target.PermissionRequired) && requester != null && !requester.HasPermission(target.PermissionRequired))
            {
                return (false, $"Kamu tidak memiliki izin '{target.PermissionRequired}' untuk masuk ke world '{target.DisplayName}'.");
            }

            if (!target.IsHub && ProcessManager != null)
            {
                var (bootOk, bootMsg) = await ProcessManager.EnsureWorldRunningAsync(target.Id, progress =>
                {
                    requester?.SendInfoMessage($"[MultiWorld] {progress}");
                });

                if (!bootOk)
                {
                    return (false, bootMsg);
                }
            }

            int targetPort = target.IsHub
                ? ResolveListenPort(Config.MasterWorldPort)
                : target.Port;

            _recentTransfers[playerName] = DateTime.UtcNow.AddSeconds(12);
            _recentTransferSources[playerName] = (session.CurrentWorldId, DateTime.UtcNow.AddSeconds(12));
            string transferAcc = !string.IsNullOrWhiteSpace(accountName) ? accountName : playerName;
            if (!string.IsNullOrWhiteSpace(sscSnapshotJson))
            {
                SscStore?.Save(transferAcc, playerName, session.CurrentWorldId, sscSnapshotJson, markLeaveSynced: true);
            }
            SscStore?.MarkEnteredWorld(transferAcc, playerName, target.Id);
            TicketStore.IssueTicket(playerName, accountName, groupName, uuid, session.CurrentWorldId, target.Id, hasWildcard, groupPerms, sscSnapshotJson);
            if (!target.IsHub)
            {
                ProcessManager?.MarkWorldPlayerArriving(target.Id, 90);
            }

            var result = await Proxy.SwitchPlayerWorldAsync(playerName, target.Id, targetPort, uuid, hubSlot, currentWorldGuess);
            if (result.Ok)
            {
                _ = HandleMasterPresenceEventAsync("world_switch", playerName, target.Id);
            }
            else if (!target.IsHub && ProcessManager != null)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Log terakhir '{target.Id}': {ProcessManager.GetRecentLogsSummary(target.Id, 12)}");
            }
            return result;
        }

        private static async Task<(bool Ok, string Message)> RelayTransferToMasterAsync(
            string playerName,
            string accountName,
            string groupName,
            string uuid,
            bool hasWildcard,
            string groupPermissions,
            string sscSnapshotJson,
            string targetWorldId)
        {
            var res = await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
            {
                Action = "request_transfer",
                WorldId = CurrentWorldId,
                PlayerName = playerName,
                AccountName = accountName,
                GroupName = groupName,
                ClientUuid = uuid,
                HasWildcardPermission = hasWildcard,
                GroupPermissions = groupPermissions,
                SscSnapshotJson = sscSnapshotJson,
                TargetWorldId = targetWorldId
            }, timeoutMs: 90_000);

            return (res.Success, res.Message);
        }

        private static async Task<IpcResponse> HandleIpcRequestAsync(IpcRequest req)
        {
            try
            {
                switch (req.Action)
                {
                    case "child_register":
                        ProcessManager?.OnChildRegistered(req.WorldId, req.Port, req.CallbackPort, req.Pid, req.PlayerCount, req.OnlinePlayerNames);
                        return new IpcResponse { Success = true, Message = "Registered" };

                    case "child_heartbeat":
                        ProcessManager?.OnChildHeartbeat(req.WorldId, req.CallbackPort, req.PlayerCount, req.OnlinePlayerNames);
                        return new IpcResponse { Success = true, Message = "OK" };

                    case "get_worlds":
                        return new IpcResponse
                        {
                            Success = true,
                            Worlds = ProcessManager?.GetWorldSnapshots(GetLocalActivePlayerNames()) ?? new List<WorldStatusSnapshot>()
                        };

                 case "relay_chat":
                     if (Config.CrossWorldChat && !string.IsNullOrWhiteSpace(req.FormattedMessage))
                     {
                         string msg = req.FormattedMessage;
                         string cleanConsole = StripTerrariaColorTags(msg);
                         string prefixToStrip = $"[{req.WorldId.ToUpperInvariant()}] ";
                         if (cleanConsole.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
                         {
                             cleanConsole = cleanConsole.Substring(prefixToStrip.Length);
                         }
                         TShock.Log.ConsoleInfo($"[Chat:{req.WorldId}] {cleanConsole}");
                         DiscordModule.NotifyInGameChat(req.WorldId, req.PlayerName, req.FormattedMessage);

                         if (HasActiveRealPlayers())
                         {
                             DateTime expiresUtc = DateTime.UtcNow.AddSeconds(3);
                             _mainThreadQueue.Enqueue(() =>
                             {
                                 if (DateTime.UtcNow <= expiresUtc)
                                 {
                                     BroadcastLocalFormatted(msg, Color.White, skipJustJoined: true);
                                 }
                             });
                         }

                         await PushMessageToOtherChildrenAsync(req.WorldId, "push_chat", msg, 255, 255, 255);
                     }
                     return new IpcResponse { Success = true };

                 case "relay_broadcast":
                     if (!string.IsNullOrWhiteSpace(req.FormattedMessage))
                     {
                         string msg = req.FormattedMessage;
                         string cleanConsole = StripTerrariaColorTags(msg);
                         TShock.Log.ConsoleInfo(cleanConsole);

                         if (HasActiveRealPlayers())
                         {
                             DateTime expiresUtc = DateTime.UtcNow.AddSeconds(3);
                             _mainThreadQueue.Enqueue(() =>
                             {
                                 if (DateTime.UtcNow <= expiresUtc)
                                 {
                                     BroadcastLocalFormatted(msg, new Color(250, 204, 21), skipJustJoined: true);
                                 }
                             });
                         }

                         await PushMessageToOtherChildrenAsync(req.WorldId, "push_broadcast", msg, 250, 204, 21);
                     }
                     return new IpcResponse { Success = true };

                 case "save_ssc":
                     if (!string.IsNullOrWhiteSpace(req.AccountName) && !string.IsNullOrWhiteSpace(req.SscSnapshotJson))
                     {
                         bool isLeaveSync = string.Equals(req.AdminSubAction, "leave_sync", StringComparison.OrdinalIgnoreCase);
                         var existingRec = SscStore?.Get(req.AccountName);
                         var activeSession = !string.IsNullOrWhiteSpace(req.PlayerName)
                             ? Proxy?.FindSessionByPlayerName(req.PlayerName)
                             : null;
                         bool transferredOutFromThisWorld =
                             !string.IsNullOrWhiteSpace(req.PlayerName) &&
                             _recentTransferSources.TryGetValue(req.PlayerName, out var srcInfo) &&
                             DateTime.UtcNow <= srcInfo.ExpiresUtc &&
                             string.Equals(srcInfo.SourceWorldId, req.WorldId, StringComparison.OrdinalIgnoreCase);

                         bool alreadyInOtherWorld =
                             activeSession != null &&
                             !activeSession.IsClosed &&
                             !string.IsNullOrWhiteSpace(req.WorldId) &&
                             !string.IsNullOrWhiteSpace(activeSession.CurrentWorldId) &&
                             !activeSession.CurrentWorldId.Equals(req.WorldId, StringComparison.OrdinalIgnoreCase) &&
                             existingRec != null &&
                             string.Equals(existingRec.ActiveWorldId, activeSession.CurrentWorldId, StringComparison.OrdinalIgnoreCase);

                         if (transferredOutFromThisWorld || (!isLeaveSync && alreadyInOtherWorld))
                         {
                             // Player pindah via /mw tp atau ini auto-save telat dari world lama; jangan timpa world baru
                             return new IpcResponse { Success = true };
                         }

                         SscStore?.Save(req.AccountName, req.PlayerName, req.WorldId, req.SscSnapshotJson, markLeaveSynced: isLeaveSync);
                         var syncTask = SyncSscJsonToMasterCharacterDbAsync(req.AccountName, req.PlayerName, req.SscSnapshotJson, forceOnlineRefresh: isLeaveSync);
                         if (isLeaveSync && _initialized)
                         {
                             await Task.WhenAny(syncTask, Task.Delay(2000));
                         }
                     }
                     return new IpcResponse { Success = true };

                 case "load_ssc":
                     {
                         string resolvedJson = ResolveLatestMasterSscSnapshot(req.AccountName, req.PlayerName);
                         return new IpcResponse
                         {
                             Success = !string.IsNullOrWhiteSpace(resolvedJson),
                             SscSnapshotJson = resolvedJson
                         };
                     }

                 case "remote_give":
                     {
                         var targetDef = Config.GetWorld(req.WorldId);
                         if (targetDef != null && targetDef.IsHub)
                         {
                             var target = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(req.PlayerName, StringComparison.OrdinalIgnoreCase));
                             if (target == null || !target.Active)
                             {
                                 return new IpcResponse { Success = false, Message = $"Player '{req.PlayerName}' tidak ditemukan di Hub." };
                             }
                             var items = FindItems(req.AdminSubAction);
                             if (items.Count == 0)
                             {
                                 return new IpcResponse { Success = false, Message = $"Item '{req.AdminSubAction}' tidak ditemukan." };
                             }
                             var item = items[0];
                             int stack = req.Port > 0 ? Math.Min(req.Port, item.maxStack) : item.maxStack;
                             _mainThreadQueue.Enqueue(() =>
                             {
                                 target.GiveItem(item.type, stack);
                                 target.SendSuccessMessage($"[c/38bdf8:[SERVER]] Kamu menerima {stack}x {item.Name}!");
                             });
                             return new IpcResponse { Success = true, Message = $"Berhasil memberikan {stack}x {item.Name} kepada {target.Name} di Hub." };
                         }

                         var targets = ProcessManager?.GetRunningCallbackTargets(null) ?? new List<(string, int)>();
                         var child = targets.FirstOrDefault(t => t.WorldId.Equals(req.WorldId, StringComparison.OrdinalIgnoreCase));
                         if (child.CallbackPort <= 0)
                         {
                             return new IpcResponse { Success = false, Message = $"World '{req.WorldId}' tidak sedang online." };
                         }
                         return await MultiWorldIpcClient.SendAsync(child.CallbackPort, req, 4000);
                     }

                 case "remote_exec":
                     {
                         string cmd = EnsureCommandSpecifier(req.FormattedMessage);
                         if (string.Equals(req.WorldId, "all", StringComparison.OrdinalIgnoreCase))
                         {
                             _mainThreadQueue.Enqueue(() =>
                             {
                                 _suppressRemoteRouting = true;
                                 try { Commands.HandleCommand(TSPlayer.Server, cmd); }
                                 finally { _suppressRemoteRouting = false; }
                             });
                             var targets = ProcessManager?.GetRunningCallbackTargets(null) ?? new List<(string, int)>();
                             foreach (var t in targets)
                             {
                                 _ = MultiWorldIpcClient.SendAsync(t.CallbackPort, req, 3000);
                             }
                             return new IpcResponse { Success = true, Message = $"Perintah '{req.FormattedMessage}' disebarkan ke seluruh world." };
                         }
                         else if (string.Equals(req.WorldId, Config.HubWorldId, StringComparison.OrdinalIgnoreCase))
                         {
                             _mainThreadQueue.Enqueue(() =>
                             {
                                 _suppressRemoteRouting = true;
                                 try { Commands.HandleCommand(TSPlayer.Server, cmd); }
                                 finally { _suppressRemoteRouting = false; }
                             });
                             return new IpcResponse { Success = true, Message = $"Perintah '{req.FormattedMessage}' dieksekusi di Hub." };
                         }
                         else
                         {
                             var targets = ProcessManager?.GetRunningCallbackTargets(null) ?? new List<(string, int)>();
                             var child = targets.FirstOrDefault(t => t.WorldId.Equals(req.WorldId, StringComparison.OrdinalIgnoreCase));
                             if (child.CallbackPort <= 0)
                             {
                                 return new IpcResponse { Success = false, Message = $"World '{req.WorldId}' tidak sedang online." };
                             }
                             return await MultiWorldIpcClient.SendAsync(child.CallbackPort, req, 4000);
                         }
                     }

                 case "route_player_exec":
                     {
                         var worlds = ProcessManager?.GetWorldSnapshots(GetLocalActivePlayerNames()) ?? new List<WorldStatusSnapshot>();
                         var matchWorld = FindWorldWithPlayer(worlds, req.PlayerName, req.WorldId);
                         if (matchWorld == null)
                         {
                             return new IpcResponse { Success = false, Message = $"Player '{req.PlayerName}' tidak online di world manapun." };
                         }
                         string cmd = req.FormattedMessage;
                         if (matchWorld.IsHub)
                         {
                             string hubCmd = EnsureCommandSpecifier(cmd);
                             _mainThreadQueue.Enqueue(() =>
                             {
                                 _suppressRemoteRouting = true;
                                 try { Commands.HandleCommand(TSPlayer.Server, hubCmd); }
                                 finally { _suppressRemoteRouting = false; }
                             });
                             return new IpcResponse { Success = true, Message = $"Perintah '{cmd}' dieksekusi di Hub ({matchWorld.DisplayName})." };
                         }
                         var targets = ProcessManager?.GetRunningCallbackTargets(null) ?? new List<(string, int)>();
                         var child = targets.FirstOrDefault(t => t.WorldId.Equals(matchWorld.Id, StringComparison.OrdinalIgnoreCase));
                         if (child.CallbackPort <= 0)
                         {
                             return new IpcResponse { Success = false, Message = $"World '{matchWorld.Id}' tidak online." };
                         }
                         return await MultiWorldIpcClient.SendAsync(child.CallbackPort, new IpcRequest
                         {
                             Action = "remote_exec",
                             WorldId = matchWorld.Id,
                             FormattedMessage = cmd
                         }, 4000);
                     }

                    case "presence_event":
                        await HandleMasterPresenceEventAsync(req.AdminSubAction, req.PlayerName, req.WorldId);
                        return new IpcResponse { Success = true };

                    case "discord_rpc":
                        return await DiscordModule.HandleMasterDiscordRpcAsync(
                            req.AdminSubAction,
                            req.AccountName,
                            req.PlayerName,
                            req.FormattedMessage);

                    case "discord_event":
                        DiscordModule.NotifyExternalEventFromChild(req.WorldId, req.FormattedMessage);
                        return new IpcResponse { Success = true };

                    case "claim_ticket":
                        {
                            var claimed = TicketStore?.ConsumeTicket(req.PlayerName, req.WorldId);
                            if (claimed == null)
                            {
                                return new IpcResponse { Success = false, Message = "Ticket tidak ditemukan." };
                            }

                            return new IpcResponse
                            {
                                Success = true,
                                AccountName = claimed.AccountName,
                                GroupName = claimed.GroupName,
                                ClientUuid = claimed.ClientUuid,
                                HasWildcardPermission = claimed.HasWildcardPermission,
                                GroupPermissions = claimed.GroupPermissions,
                                SscSnapshotJson = claimed.SscSnapshotJson
                            };
                        }

                    case "request_transfer":
                        {
                            var target = Config.GetWorld(req.TargetWorldId);
                            if (target == null)
                            {
                                return new IpcResponse { Success = false, Message = $"World '{req.TargetWorldId}' tidak ditemukan." };
                            }

                            if (!target.IsHub && ProcessManager != null)
                            {
                                var (okBoot, msgBoot) = await ProcessManager.EnsureWorldRunningAsync(target.Id);
                                if (!okBoot)
                                {
                                    return new IpcResponse { Success = false, Message = msgBoot };
                                }
                            }

                            if (Proxy == null || !Proxy.IsListening)
                            {
                                return new IpcResponse { Success = false, Message = "Gateway Proxy tidak aktif di Master." };
                            }

                            var session = Proxy.FindSessionByPlayerName(req.PlayerName);
                            if (session == null)
                            {
                                return new IpcResponse { Success = false, Message = $"Sesi proxy '{req.PlayerName}' tidak ditemukan. Pastikan login lewat port Proxy {Proxy.ListenPort}." };
                            }

                            int targetPort = target.IsHub
                                ? ResolveListenPort(Config.MasterWorldPort)
                                : target.Port;

                            ExtractGroupPermissions(req.GroupName, null, out bool masterWildcard, out string masterPerms);
                            bool finalWildcard = req.HasWildcardPermission || masterWildcard;
                            string finalPerms = !string.IsNullOrWhiteSpace(masterPerms) ? masterPerms : req.GroupPermissions;

                            _recentTransfers[req.PlayerName] = DateTime.UtcNow.AddSeconds(12);
                            _recentTransferSources[req.PlayerName] = (session.CurrentWorldId, DateTime.UtcNow.AddSeconds(12));
                            string reqAccKey = !string.IsNullOrWhiteSpace(req.AccountName) ? req.AccountName : req.PlayerName;
                            if (!string.IsNullOrWhiteSpace(req.SscSnapshotJson))
                            {
                                SscStore?.Save(reqAccKey, req.PlayerName, session.CurrentWorldId, req.SscSnapshotJson, markLeaveSynced: true);
                                SyncSscJsonToMasterCharacterDb(reqAccKey, req.PlayerName, req.SscSnapshotJson);
                            }
                            SscStore?.MarkEnteredWorld(reqAccKey, req.PlayerName, target.Id);
                            TicketStore.IssueTicket(req.PlayerName, req.AccountName, req.GroupName, req.ClientUuid, session.CurrentWorldId, target.Id, finalWildcard, finalPerms, req.SscSnapshotJson);
                            if (!target.IsHub)
                            {
                                ProcessManager?.MarkWorldPlayerArriving(target.Id, 90);
                            }
                            var (okSw, msgSw) = await Proxy.SwitchPlayerWorldAsync(req.PlayerName, target.Id, targetPort);
                            if (okSw)
                            {
                                _ = HandleMasterPresenceEventAsync("world_switch", req.PlayerName, target.Id);
                            }
                            else if (!target.IsHub && ProcessManager != null)
                            {
                                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Log terakhir '{target.Id}': {ProcessManager.GetRecentLogsSummary(target.Id, 12)}");
                            }
                            return new IpcResponse { Success = okSw, Message = msgSw };
                        }

                    case "admin_action":
                        return await HandleMasterAdminActionAsync(req.AdminSubAction, req.Args ?? new List<string>());

                    default:
                        return new IpcResponse { Success = false, Message = $"Unknown IPC action: {req.Action}" };
                }
            }
            catch (Exception ex)
            {
                return new IpcResponse { Success = false, Message = $"IPC error: {ex.Message}" };
            }
        }

        public static async Task<IpcResponse> ExecuteAdminActionAsync(string subAction, List<string> args)
        {
            if (IsChildProcess)
            {
                return await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                {
                    Action = "admin_action",
                    WorldId = CurrentWorldId,
                    AdminSubAction = subAction,
                    Args = args
                }, timeoutMs: 320_000);
            }

            return await HandleMasterAdminActionAsync(subAction, args);
        }

        private static async Task<IpcResponse> HandleMasterAdminActionAsync(string subAction, List<string> args)
        {
            if (ProcessManager == null)
            {
                return new IpcResponse { Success = false, Message = "ProcessManager belum aktif." };
            }

            switch (subAction.ToLowerInvariant())
            {
                case "create":
                {
                    if (args.Count < 1)
                    {
                        return new IpcResponse { Success = false, Message = "Format: /mw create <id> [small|medium|large] [classic|expert|master|journey] [random|corrupt|crimson] [seed]" };
                    }
                    string id = args[0];
                    string size = args.Count > 1 ? args[1] : "medium";
                    string diff = args.Count > 2 ? args[2] : "classic";
                    string evil = args.Count > 3 ? args[3] : "random";
                    string seed = args.Count > 4 ? string.Join(" ", args.Skip(4)) : "";

                    var (ok, msg) = await ProcessManager.CreateWorldAsync(id, size, diff, evil, seed, bootImmediately: true);
                    return new IpcResponse { Success = ok, Message = msg };
                }

                case "delete":
                {
                    if (args.Count < 1)
                    {
                        return new IpcResponse { Success = false, Message = "Format: /mw delete <id> confirm" };
                    }
                    string id = args[0];
                    var targetDef = Config.GetWorld(id);
                    if (targetDef == null)
                    {
                        return new IpcResponse { Success = false, Message = $"World '{id}' tidak ditemukan." };
                    }
                    if (targetDef.IsHub)
                    {
                        return new IpcResponse { Success = false, Message = "Hub utama tidak bisa dihapus." };
                    }

                    if (Proxy != null)
                    {
                        int hubPort = ResolveListenPort(Config.MasterWorldPort);
                        foreach (var p in TShock.Players.Where(x => x != null && x.Active).ToList())
                        {
                            var ses = Proxy.FindSessionByPlayerName(p.Name);
                            if (ses != null && ses.CurrentWorldId.Equals(targetDef.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                ExtractGroupPermissions(p.Group?.Name ?? "default", p, out bool delWildcard, out string delPerms);
                                _recentTransfers[p.Name] = DateTime.UtcNow.AddSeconds(12);
                                TicketStore.IssueTicket(p.Name, p.Account?.Name ?? p.Name, p.Group?.Name ?? "default", p.UUID, targetDef.Id, Config.HubWorldId, delWildcard, delPerms);
                                await Proxy.SwitchPlayerWorldAsync(p.Name, Config.HubWorldId, hubPort);
                            }
                        }
                    }

                    var (ok, msg) = await ProcessManager.DeleteWorldAsync(targetDef.Id, deleteWorldFiles: true);
                    return new IpcResponse { Success = ok, Message = msg };
                }

                case "start":
                {
                    if (args.Count < 1) return new IpcResponse { Success = false, Message = "Format: /mw start <id>" };
                    var (ok, msg) = await ProcessManager.EnsureWorldRunningAsync(args[0]);
                    return new IpcResponse { Success = ok, Message = msg };
                }

                case "stop":
                case "sleep":
                {
                    if (args.Count < 1) return new IpcResponse { Success = false, Message = "Format: /mw sleep <id>" };
                    var (ok, msg) = await ProcessManager.StopWorldAsync(args[0]);
                    return new IpcResponse { Success = ok, Message = msg };
                }

                case "autosleep":
                {
                    if (args.Count < 2)
                    {
                        return new IpcResponse { Success = false, Message = "Format: /mw autosleep <id> <on|off|menit>" };
                    }
                    string id = args[0];
                    string val = args[1].Trim().ToLowerInvariant();
                    if (val == "on" || val == "true")
                    {
                        var (ok, msg) = await ProcessManager.SetAutoSleepAsync(id, true, null);
                        return new IpcResponse { Success = ok, Message = msg };
                    }
                    if (val == "off" || val == "false" || val == "0")
                    {
                        var (ok, msg) = await ProcessManager.SetAutoSleepAsync(id, false, null);
                        return new IpcResponse { Success = ok, Message = msg };
                    }
                    if (int.TryParse(val, out int mins) && mins > 0)
                    {
                        var (ok, msg) = await ProcessManager.SetAutoSleepAsync(id, true, mins);
                        return new IpcResponse { Success = ok, Message = msg };
                    }
                    return new IpcResponse { Success = false, Message = "Nilai harus 'on', 'off', atau angka menit (misal: 5)." };
                }

                case "perm":
                case "setperm":
                {
                    if (args.Count < 2)
                    {
                        return new IpcResponse { Success = false, Message = "Format: /mw perm <id> <permission|none>" };
                    }
                    var (ok, msg) = await ProcessManager.SetPermissionAsync(args[0], args[1]);
                    return new IpcResponse { Success = ok, Message = msg };
                }

                default:
                    return new IpcResponse { Success = false, Message = $"Sub-perintah '{subAction}' tidak dikenal." };
            }
        }

        public static async Task<List<WorldStatusSnapshot>> FetchAllWorldSnapshotsAsync()
        {
            if (IsChildProcess)
            {
                var res = await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest { Action = "get_worlds" });
                return res.Success ? res.Worlds : new List<WorldStatusSnapshot>();
            }

            return ProcessManager?.GetWorldSnapshots(GetLocalActivePlayerNames()) ?? new List<WorldStatusSnapshot>();
        }

        #region Cross-World Console & Command Integration

        public static List<Item> FindItems(string query)
        {
            if (int.TryParse(query, out int id) && id > 0 && id < Terraria.ID.ItemID.Count)
            {
                var item = TShock.Utils.GetItemById(id);
                return item != null ? new List<Item> { item } : new List<Item>();
            }
            return TShock.Utils.GetItemByName(query);
        }

        private static void OnServerCommand(CommandEventArgs args)
        {
            if (args.Handled || string.IsNullOrWhiteSpace(args.Command)) return;
            string line = args.Command.Trim();
            string cmdName = line;
            string remainder = string.Empty;
            int spaceIdx = line.IndexOf(' ');
            if (spaceIdx > 0)
            {
                cmdName = line.Substring(0, spaceIdx);
                remainder = line.Substring(spaceIdx + 1).Trim();
            }
            if (cmdName.StartsWith("/")) cmdName = cmdName.Substring(1);
            cmdName = cmdName.ToLowerInvariant();

            if (cmdName == "broadcast" || cmdName == "bc")
            {
                if (!string.IsNullOrEmpty(remainder))
                {
                    string bcMsg = $"[c/facc15:[BROADCAST]] {remainder}";
                    BroadcastLocalFormatted(bcMsg, new Color(250, 204, 21));
                    TShock.Log.ConsoleInfo(StripTerrariaColorTags(bcMsg));
                    if (!IsChildProcess)
                    {
                        _ = PushMessageToOtherChildrenAsync(null, "push_broadcast", bcMsg, 250, 204, 21);
                    }
                    else
                    {
                        _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "relay_broadcast",
                            WorldId = CurrentWorldId,
                            FormattedMessage = bcMsg
                        });
                    }
                    args.Handled = true;
                }
            }
            else if (cmdName == "say")
            {
                if (!string.IsNullOrEmpty(remainder))
                {
                    string sayMsg = $"[c/38bdf8:[SERVER]] {remainder}";
                    BroadcastLocalFormatted(sayMsg, new Color(56, 189, 248));
                    TShock.Log.ConsoleInfo(StripTerrariaColorTags(sayMsg));
                    if (!IsChildProcess)
                    {
                        _ = PushMessageToOtherChildrenAsync(null, "push_chat", sayMsg, 56, 189, 248);
                    }
                    else
                    {
                        _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "relay_chat",
                            WorldId = CurrentWorldId,
                            FormattedMessage = sayMsg
                        });
                    }
                    args.Handled = true;
                }
            }
            else if (cmdName == "give" || cmdName == "g")
            {
                var parts = ParseCommandArguments(remainder);
                if (parts.Count >= 2)
                {
                    if (TryInterceptCrossWorldGive(TSPlayer.Server, parts))
                    {
                        args.Handled = true;
                    }
                }
            }
            else if (cmdName == "mwexec")
            {
                var parts = ParseCommandArguments(remainder);
                if (parts.Count >= 2)
                {
                    string targetWorld = parts[0];
                    string cmdToRun = string.Join(" ", parts.Skip(1));
                    _ = ExecuteRemoteCommandAsync(TSPlayer.Server, targetWorld, cmdToRun);
                    args.Handled = true;
                }
                else
                {
                    TShock.Log.ConsoleInfo("Format: mwexec <world_id|all> <perintah...>");
                    args.Handled = true;
                }
            }
            else if (!_suppressRemoteRouting)
            {
                var parts = ParseCommandArguments(remainder);
                if (TryInterceptCrossWorldPlayerCommand(TSPlayer.Server, line, cmdName, parts))
                {
                    args.Handled = true;
                }
            }
        }

        private static void OnPlayerCommand(PlayerCommandEventArgs args)
        {
            if (args.Handled || args.Player == null || !args.Player.RealPlayer) return;
            string cmd = args.CommandName?.ToLowerInvariant() ?? string.Empty;

            if (cmd == "broadcast" || cmd == "bc")
            {
                if (args.Player.HasPermission("tshock.admin.broadcast") && args.Parameters.Count > 0)
                {
                    string msgText = string.Join(" ", args.Parameters);
                    string bcMsg = $"[c/facc15:[BROADCAST]] {msgText}";
                    BroadcastLocalFormatted(bcMsg, new Color(250, 204, 21));
                    if (!IsChildProcess)
                    {
                        TShock.Log.ConsoleInfo(StripTerrariaColorTags(bcMsg));
                        _ = PushMessageToOtherChildrenAsync(null, "push_broadcast", bcMsg, 250, 204, 21);
                    }
                    else
                    {
                        _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "relay_broadcast",
                            WorldId = CurrentWorldId,
                            FormattedMessage = bcMsg
                        });
                    }
                    args.Handled = true;
                }
            }
            else if (cmd == "say")
            {
                if (args.Player.HasPermission("tshock.cfg.say") && args.Parameters.Count > 0)
                {
                    string msgText = string.Join(" ", args.Parameters);
                    string sayMsg = $"[c/38bdf8:[SERVER {args.Player.Name}]] {msgText}";
                    BroadcastLocalFormatted(sayMsg, new Color(56, 189, 248));
                    if (!IsChildProcess)
                    {
                        TShock.Log.ConsoleInfo(StripTerrariaColorTags(sayMsg));
                        _ = PushMessageToOtherChildrenAsync(null, "push_chat", sayMsg, 56, 189, 248);
                    }
                    else
                    {
                        _ = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                        {
                            Action = "relay_chat",
                            WorldId = CurrentWorldId,
                            FormattedMessage = sayMsg
                        });
                    }
                    args.Handled = true;
                }
            }
            else if (cmd == "give" || cmd == "g")
            {
                if (args.Player.HasPermission(Permissions.give) && args.Parameters.Count >= 2)
                {
                    if (TryInterceptCrossWorldGive(args.Player, args.Parameters))
                    {
                        args.Handled = true;
                    }
                }
            }
            else if (!_suppressRemoteRouting)
            {
                var chatCmd = Commands.ChatCommands.FirstOrDefault(c => c.HasAlias(cmd));
                if (chatCmd != null && chatCmd.CanRun(args.Player))
                {
                    string reconstructedCmd = args.Parameters.Count == 0
                        ? cmd
                        : $"{cmd} {string.Join(" ", args.Parameters.Select(p => p.Contains(' ') ? $"\"{p}\"" : p))}";
                    if (TryInterceptCrossWorldPlayerCommand(args.Player, reconstructedCmd, cmd, args.Parameters))
                    {
                        args.Handled = true;
                    }
                }
            }
        }

        private static readonly HashSet<string> _param0PlayerCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "setrank", "giveboss", "addboss", "setjobstatus", "setelemstats",
            "awakenelement", "pilihelemen", "setelement", "resetelement", "setelementlevel",
            "heal", "slay", "kill", "kick", "mute", "unmute", "godmode", "god",
            "freeze", "buff", "gbuff", "rocket", "firework", "slap", "annoy", "confuse"
        };

        private static int ResolvePlayerTargetArgIndex(string cmdName, List<string> parts)
        {
            if (parts.Count == 0) return -1;

            if (_param0PlayerCommands.Contains(cmdName))
            {
                return 0;
            }

            if (cmdName == "eco" || cmdName == "economy" || cmdName == "expadmin" || cmdName == "leveladmin")
            {
                return parts.Count >= 2 ? 1 : -1;
            }

            if (cmdName == "status" || cmdName == "survstat")
            {
                return parts.Count >= 2 && (parts[0].Equals("reset", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("set", StringComparison.OrdinalIgnoreCase)) ? 1 : -1;
            }

            if (cmdName == "mw" || cmdName == "multiworld")
            {
                return parts.Count >= 2 && parts[0].Equals("resetssc", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            }

            if (cmdName == "user" && parts.Count >= 3 && parts[0].Equals("group", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (cmdName == "ban" && parts.Count >= 2 && parts[0].Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return -1;
        }

        private static WorldStatusSnapshot? FindWorldWithPlayer(List<WorldStatusSnapshot> worlds, string playerName, string? excludeWorldId)
        {
            var exact = worlds.FirstOrDefault(w =>
                (string.IsNullOrWhiteSpace(excludeWorldId) || !w.Id.Equals(excludeWorldId, StringComparison.OrdinalIgnoreCase)) &&
                w.OnlinePlayerNames.Any(n => n.Equals(playerName, StringComparison.OrdinalIgnoreCase)));
            if (exact != null) return exact;

            var prefixMatches = worlds.Where(w =>
                (string.IsNullOrWhiteSpace(excludeWorldId) || !w.Id.Equals(excludeWorldId, StringComparison.OrdinalIgnoreCase)) &&
                w.OnlinePlayerNames.Any(n => n.StartsWith(playerName, StringComparison.OrdinalIgnoreCase))).ToList();
            return prefixMatches.Count == 1 ? prefixMatches[0] : null;
        }

        private static bool TryInterceptCrossWorldPlayerCommand(TSPlayer executor, string rawCommandLine, string cmdName, List<string> parts)
        {
            int argIdx = ResolvePlayerTargetArgIndex(cmdName, parts);
            if (argIdx < 0 || argIdx >= parts.Count) return false;

            string candidateName = parts[argIdx];
            if (string.IsNullOrWhiteSpace(candidateName)) return false;

            bool isOnlineLocally = TShock.Players.Any(p =>
                p != null && p.Active && p.RealPlayer &&
                (p.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase) ||
                 p.Name.StartsWith(candidateName, StringComparison.OrdinalIgnoreCase)));
            if (isOnlineLocally) return false;

            string cleanCmd = rawCommandLine.TrimStart('/').Trim();
            if (string.IsNullOrWhiteSpace(cleanCmd)) return false;

            if (!IsChildProcess)
            {
                var worlds = ProcessManager?.GetWorldSnapshots(GetLocalActivePlayerNames()) ?? new List<WorldStatusSnapshot>();
                var targetWorld = FindWorldWithPlayer(worlds, candidateName, CurrentWorldId);
                if (targetWorld == null) return false;

                string matchedPlayerName = targetWorld.OnlinePlayerNames.FirstOrDefault(n =>
                    n.Equals(candidateName, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(candidateName, StringComparison.OrdinalIgnoreCase)) ?? candidateName;

                _ = Task.Run(async () =>
                {
                    await ExecuteRemoteCommandAsync(executor, targetWorld.Id, cleanCmd);
                    if (executor.RealPlayer)
                    {
                        executor.SendInfoMessage($"[MultiWorld] Target '{matchedPlayerName}' berada di world '{targetWorld.DisplayName}' ({targetWorld.Id}).");
                    }
                });
                return true;
            }

            // Jika dijalankan dari Child World, cek Master IPC apakah player online di world lain
            var res = MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
            {
                Action = "route_player_exec",
                WorldId = CurrentWorldId,
                PlayerName = candidateName,
                FormattedMessage = cleanCmd
            }, timeoutMs: 2500).GetAwaiter().GetResult();

            if (res.Success)
            {
                executor.SendSuccessMessage($"[MultiWorld] {res.Message}");
                return true;
            }

            return false;
        }

        private static bool TryInterceptCrossWorldGive(TSPlayer executor, List<string> parts)
        {
            if (parts.Count < 2) return false;

            var localA = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
            var localB = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            if ((localA != null && localA.Active) || (localB != null && localB.Active))
            {
                return false;
            }

            _ = Task.Run(async () =>
            {
                await ExecuteSmartCrossWorldGiveAsync(executor, parts);
            });

            return true;
        }

        public static async Task ExecuteSmartCrossWorldGiveAsync(TSPlayer executor, List<string> parts)
        {
            if (parts.Count < 2)
            {
                executor.SendErrorMessage("Penggunaan: /give <item> <player> [stack] atau /give <player> <item> [stack]");
                return;
            }

            var worlds = await FetchAllWorldSnapshotsAsync();
            string? candidatePlayer = null;
            string? candidateItem = null;
            WorldStatusSnapshot? targetWorld = null;

            string p1 = parts[1];
            var worldForP1 = worlds.FirstOrDefault(w => w.OnlinePlayerNames.Any(n => n.Equals(p1, StringComparison.OrdinalIgnoreCase)));
            if (worldForP1 != null)
            {
                candidatePlayer = p1;
                candidateItem = parts[0];
                targetWorld = worldForP1;
            }
            else
            {
                string p0 = parts[0];
                var worldForP0 = worlds.FirstOrDefault(w => w.OnlinePlayerNames.Any(n => n.Equals(p0, StringComparison.OrdinalIgnoreCase)));
                if (worldForP0 != null)
                {
                    candidatePlayer = p0;
                    candidateItem = parts[1];
                    targetWorld = worldForP0;
                }
            }

            if (candidatePlayer == null || candidateItem == null || targetWorld == null)
            {
                executor.SendErrorMessage($"Player '{parts[1]}' (atau '{parts[0]}') tidak ditemukan di world manapun.");
                return;
            }

            var items = FindItems(candidateItem);
            if (items.Count == 0)
            {
                executor.SendErrorMessage($"Item '{candidateItem}' tidak ditemukan.");
                return;
            }
            var item = items[0];
            int stack = parts.Count > 2 && int.TryParse(parts[2], out int s) && s > 0 ? Math.Min(s, item.maxStack) : item.maxStack;

            executor.SendInfoMessage($"[MultiWorld] Mengirim {stack}x {item.Name} ke {candidatePlayer} di world '{targetWorld.DisplayName}'...");

            IpcResponse res;
            if (IsChildProcess)
            {
                res = await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                {
                    Action = "remote_give",
                    WorldId = targetWorld.Id,
                    PlayerName = candidatePlayer,
                    AdminSubAction = item.type.ToString(),
                    Port = stack
                });
            }
            else
            {
                if (targetWorld.IsHub)
                {
                    var hubPlayer = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.Name.Equals(candidatePlayer, StringComparison.OrdinalIgnoreCase));
                    if (hubPlayer != null && hubPlayer.Active)
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            hubPlayer.GiveItem(item.type, stack);
                            hubPlayer.SendSuccessMessage($"[c/38bdf8:[SERVER]] Kamu menerima {stack}x {item.Name}!");
                        });
                        executor.SendSuccessMessage($"[MultiWorld] Berhasil memberikan {stack}x {item.Name} kepada {hubPlayer.Name} di Hub.");
                        return;
                    }
                    executor.SendErrorMessage("Player ada di Hub tapi tidak aktif.");
                    return;
                }

                var targets = ProcessManager?.GetRunningCallbackTargets(null) ?? new List<(string, int)>();
                var child = targets.FirstOrDefault(t => t.WorldId.Equals(targetWorld.Id, StringComparison.OrdinalIgnoreCase));
                if (child.CallbackPort <= 0)
                {
                    executor.SendErrorMessage($"World '{targetWorld.Id}' tidak online.");
                    return;
                }

                res = await MultiWorldIpcClient.SendAsync(child.CallbackPort, new IpcRequest
                {
                    Action = "remote_give",
                    WorldId = targetWorld.Id,
                    PlayerName = candidatePlayer,
                    AdminSubAction = item.type.ToString(),
                    Port = stack
                });
            }

            if (res.Success)
            {
                executor.SendSuccessMessage($"[MultiWorld] {res.Message}");
            }
            else
            {
                executor.SendErrorMessage($"[MultiWorld] Gagal give lintas-world: {res.Message}");
            }
        }

        public static async Task ExecuteRemoteCommandAsync(TSPlayer executor, string targetWorld, string command)
        {
            if (string.IsNullOrWhiteSpace(targetWorld) || string.IsNullOrWhiteSpace(command)) return;

            if (IsChildProcess)
            {
                var res = await MultiWorldIpcClient.SendAsync(MasterIpcPort, new IpcRequest
                {
                    Action = "remote_exec",
                    WorldId = targetWorld,
                    FormattedMessage = command
                });
                if (res.Success) executor.SendSuccessMessage($"[MultiWorld] {res.Message}");
                else executor.SendErrorMessage($"[MultiWorld] {res.Message}");
                return;
            }

            if (string.Equals(targetWorld, "all", StringComparison.OrdinalIgnoreCase))
            {
                string hubCmd = EnsureCommandSpecifier(command);
                _mainThreadQueue.Enqueue(() =>
                {
                    _suppressRemoteRouting = true;
                    try { Commands.HandleCommand(TSPlayer.Server, hubCmd); }
                    finally { _suppressRemoteRouting = false; }
                });
                var targets = ProcessManager?.GetRunningCallbackTargets(null) ?? new List<(string, int)>();
                foreach (var t in targets)
                {
                    _ = MultiWorldIpcClient.SendAsync(t.CallbackPort, new IpcRequest
                    {
                        Action = "remote_exec",
                        WorldId = targetWorld,
                        FormattedMessage = command
                    }, 3000);
                }
                executor.SendSuccessMessage($"[MultiWorld] Perintah '{command}' disebarkan ke Hub dan semua child world.");
                return;
            }

            if (string.Equals(targetWorld, Config.HubWorldId, StringComparison.OrdinalIgnoreCase))
            {
                string hubCmd = EnsureCommandSpecifier(command);
                _mainThreadQueue.Enqueue(() =>
                {
                    _suppressRemoteRouting = true;
                    try { Commands.HandleCommand(TSPlayer.Server, hubCmd); }
                    finally { _suppressRemoteRouting = false; }
                });
                executor.SendSuccessMessage($"[MultiWorld] Perintah '{command}' dijalankan di Hub.");
                return;
            }

            var childTargets = ProcessManager?.GetRunningCallbackTargets(null) ?? new List<(string, int)>();
            var child = childTargets.FirstOrDefault(t => t.WorldId.Equals(targetWorld, StringComparison.OrdinalIgnoreCase));
            if (child.CallbackPort <= 0)
            {
                executor.SendErrorMessage($"World '{targetWorld}' tidak sedang online.");
                return;
            }

            var resp = await MultiWorldIpcClient.SendAsync(child.CallbackPort, new IpcRequest
            {
                Action = "remote_exec",
                WorldId = targetWorld,
                FormattedMessage = command
            }, 3000);

            if (resp.Success) executor.SendSuccessMessage($"[MultiWorld] {resp.Message}");
            else executor.SendErrorMessage($"[MultiWorld] {resp.Message}");
        }

        private static List<string> ParseCommandArguments(string input)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return list;
            bool inQuotes = false;
            var current = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        list.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            if (current.Length > 0)
            {
                list.Add(current.ToString());
            }
            return list;
        }

        #endregion
        #endregion
    }
}
