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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

namespace Univers.MultiWorld
{
    public static class MultiWorldModule
    {
        public static bool IsChildProcess { get; private set; }
        public static string CurrentWorldId { get; private set; } = "lobby";
        public static int MasterIpcPort { get; private set; } = 7775;

        public static MultiWorldConfig Config { get; private set; } = new();
        public static MultiWorldTicketStore TicketStore { get; private set; } = null!;
        public static MultiWorldProcessManager? ProcessManager { get; private set; }
        public static MultiWorldIpcServer? IpcServer { get; private set; }
        public static MultiWorldIpcServer? ChildPushServer { get; private set; }
        public static MultiWorldProxy? Proxy { get; private set; }

        public static bool IsCrossWorldChatActive => _initialized && Config.Enabled && Config.CrossWorldChat;

        private static string _configPath = string.Empty;
        private static string _multiWorldRoot = string.Empty;
        private static System.Timers.Timer? _heartbeatTimer;
        private static bool _initialized = false;
        private static int _effectiveMasterHubPort = 0;
        private static int _takenOverPublicPort = 0;

        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly ConcurrentDictionary<string, byte> _pendingEntityClearPlayers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _recentTransfers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (WorldTransferTicket Ticket, DateTime ExpiresUtc)> _claimedTickets = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _transferLockedPlayers = new(StringComparer.OrdinalIgnoreCase);

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
            if (IsChildProcess)
            {
                InitializeChildMode(CurrentWorldId);
            }
            else
            {
                InitializeMasterMode();
            }
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
            _initialized = false;

            try { _heartbeatTimer?.Stop(); _heartbeatTimer?.Dispose(); } catch { }

            ServerApi.Hooks.GameInitialize.Deregister(UniversPlugin.Instance, OnGameInitialize);
            ServerApi.Hooks.ServerJoin.Deregister(UniversPlugin.Instance, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnNetGreetPlayer);
            ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave);
            ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnNetGetData);
            ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.GamePostInitialize.Deregister(UniversPlugin.Instance, OnGamePostInitialize);

            _effectiveMasterHubPort = 0;
            _takenOverPublicPort = 0;

            try { Proxy?.Dispose(); } catch { }
            try { ChildPushServer?.Dispose(); } catch { }
            try { IpcServer?.Dispose(); } catch { }
            try { ProcessManager?.Dispose(); } catch { }
            try { TicketStore?.Dispose(); } catch { }
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

         // Jika di Master Hub (lobby), cetak chat bersih ke console
         if (!IsChildProcess)
         {
             TShock.Log.ConsoleInfo($"[Chat:{CurrentWorldId}] {StripTerrariaColorTags(formattedChat)}");
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

             private static string StripTerrariaColorTags(string text)
     {
         if (string.IsNullOrEmpty(text)) return string.Empty;
         return System.Text.RegularExpressions.Regex.Replace(text, @"\[c/[0-9a-fA-F]{6}:([^\]]*)\]", "$1");
     }

        private static void BroadcastLocalFormatted(string message, Color color)
        {
            foreach (var p in TShock.Players)
            {
                if (p != null && p.Active)
                {
                    p.SendMessage(message, color);
                }
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
            if (req.Action == "push_chat" || req.Action == "push_notice")
            {
                var c = new Color(req.ColorR, req.ColorG, req.ColorB);
                string msg = req.FormattedMessage;
                _mainThreadQueue.Enqueue(() => BroadcastLocalFormatted(msg, c));
            }
            return Task.FromResult(new IpcResponse { Success = true });
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
                await PushMessageToOtherChildrenAsync(worldId, "push_notice", msg, 148, 163, 184);
            }
            else if (eventKind == "server_leave")
            {
                string msg = $"[MultiWorld] {playerName} keluar dari server.";
                BroadcastLocalFormatted(msg, Color.LightSlateGray);
                await PushMessageToOtherChildrenAsync(worldId, "push_notice", msg, 148, 163, 184);
            }
            else if (eventKind == "world_switch")
            {
                string msg = $"[MultiWorld] {playerName} pindah ke {display} ({badge}).";
                BroadcastLocalFormatted(msg, Color.LightSlateGray);
                await PushMessageToOtherChildrenAsync(null, "push_notice", msg, 148, 163, 184);
            }
        }

        #endregion

        #region Hooks & Seamless Cross-World Auth + Entity Cleanup

        private static void OnServerJoin(JoinEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null) return;

            TryApplyTransferTicket(player, markForCleanup: true, consumeFromMaster: true);
        }

        private static void OnNetGreetPlayer(GreetPlayerEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || !player.Active) return;

            bool transferred = TryApplyTransferTicket(player, markForCleanup: true, consumeFromMaster: true);
            bool hasPendingClear = _pendingEntityClearPlayers.TryRemove(player.Name, out _);
            _claimedTickets.TryRemove(player.Name, out _);

            if (transferred || hasPendingClear)
            {
                int who = player.Index;
                _mainThreadQueue.Enqueue(() =>
                {
                    ClearInactiveEntitiesForPlayer(who);
                    SendSpawnSectionsAndTeleport(who);
                });

                var worldDef = Config.GetWorld(CurrentWorldId);
                string title = worldDef?.DisplayName ?? CurrentWorldId;
                player.SendMessage($"[MultiWorld] Kamu memasuki world '{title}' ({CurrentWorldId}). Ketik /lobby untuk kembali ke Hub.", Color.Aquamarine);
            }
            else if (!IsChildProcess)
            {
                // Fresh login ke server Hub -> beritahu child worlds lain bahwa player online di server
                NotifyMasterPresenceEvent("server_join", player.Name, CurrentWorldId);

                if (Proxy != null && Proxy.IsListening &&
                    Proxy.FindSession(player.Name, player.UUID, player.Index, CurrentWorldId) == null)
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
                    if (cur != null && cur.Active && cur.RealPlayer) SyncClientToSpawn(cur);
                });

                await Task.Delay(600);
                _mainThreadQueue.Enqueue(() =>
                {
                    var cur = TShock.Players[who];
                    if (cur != null && cur.Active && cur.RealPlayer) SyncClientToSpawn(cur);
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

        private static void OnServerLeave(LeaveEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || string.IsNullOrWhiteSpace(player.Name)) return;

            string pName = player.Name;
            _pendingEntityClearPlayers.TryRemove(pName, out _);
            _claimedTickets.TryRemove(pName, out _);

            bool wasTransfer = _recentTransfers.TryGetValue(pName, out var untilUtc) && DateTime.UtcNow <= untilUtc;
            if (!wasTransfer && player.RealPlayer)
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
         if (player == null || !IsTransferLocked(player.Name)) return;

         if (args.MsgID == PacketTypes.ItemDrop ||
             args.MsgID == PacketTypes.ChestGetContents ||
             args.MsgID == PacketTypes.ChestItem ||
             args.MsgID == PacketTypes.PlayerSlot)
         {
             args.Handled = true;
         }
     }

        private static void OnGameUpdate(EventArgs args)
        {
            int processed = 0;
            while (processed < 12 && _mainThreadQueue.TryDequeue(out var action))
            {
                processed++;
                try { action(); } catch { }
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
                                      grpName.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
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

                // Muat ulang inventory terbaru dari database SSC agar sinkron lintas world
                if (Main.ServerSideCharacter && player.RealPlayer)
                {
                    try
                    {
                        var latestData = TShock.CharacterDB.GetPlayerData(player, account.ID);
                        if (latestData != null)
                        {
                            player.PlayerData = latestData;
                            latestData.RestoreCharacter(player);
                        }
                    }
                    catch { }
                }

                // Pastikan SSC & TShock mengenali player sudah terautentikasi penuh (bisa /i, /spawn, & blok jatuh)
                if (Main.ServerSideCharacter && player.PlayerData != null)
                {
                    player.PlayerData.CopyCharacter(player);
                    TShock.CharacterDB.InsertPlayerData(player);
                }

                if (freshlyClaimed)
                {
                    TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Auto-Auth '{player.Name}' -> Group '{player.Group?.Name ?? resolvedGroupName}' (Wildcard={ticket.HasWildcardPermission}).");
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal menerapkan auto-auth ticket untuk '{player.Name}': {ex.Message}");
            }

            return true;
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
            catch
            {
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
            // Paksa simpan inventory & status SSC terbaru sebelum pindah world agar tidak rollback
            if (requester != null && requester.RealPlayer && Main.ServerSideCharacter && requester.IsLoggedIn)
            {
                try
                {
                    requester.PlayerData?.CopyCharacter(requester);
                    TShock.CharacterDB.InsertPlayerData(requester);
                }
                catch { }
            }

            ExtractGroupPermissions(groupName, requester, out bool hasWildcard, out string groupPerms);

            if (IsChildProcess)
            {
                _recentTransfers[playerName] = DateTime.UtcNow.AddSeconds(12);
                return await RelayTransferToMasterAsync(playerName, accountName, groupName, uuid, hasWildcard, groupPerms, targetWorldId);
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
            TicketStore.IssueTicket(playerName, accountName, groupName, uuid, session.CurrentWorldId, target.Id, hasWildcard, groupPerms);
            if (!target.IsHub)
            {
                ProcessManager?.MarkWorldPlayerArriving(target.Id, 90);
            }

            var result = await Proxy.SwitchPlayerWorldAsync(playerName, target.Id, targetPort, uuid, hubSlot, currentWorldGuess);
            if (result.Ok)
            {
                _ = HandleMasterPresenceEventAsync("world_switch", playerName, target.Id);
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
                TargetWorldId = targetWorldId
            }, timeoutMs: 320_000);

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
                         _mainThreadQueue.Enqueue(() => BroadcastLocalFormatted(msg, Color.White));
                         await PushMessageToOtherChildrenAsync(req.WorldId, "push_chat", msg, 255, 255, 255);
                     }
                     return new IpcResponse { Success = true };


                    case "presence_event":
                        await HandleMasterPresenceEventAsync(req.AdminSubAction, req.PlayerName, req.WorldId);
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
                                GroupPermissions = claimed.GroupPermissions
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
                            TicketStore.IssueTicket(req.PlayerName, req.AccountName, req.GroupName, req.ClientUuid, session.CurrentWorldId, target.Id, finalWildcard, finalPerms);
                            if (!target.IsHub)
                            {
                                ProcessManager?.MarkWorldPlayerArriving(target.Id, 90);
                            }
                            var (okSw, msgSw) = await Proxy.SwitchPlayerWorldAsync(req.PlayerName, target.Id, targetPort);
                            if (okSw)
                            {
                                _ = HandleMasterPresenceEventAsync("world_switch", req.PlayerName, target.Id);
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

        #endregion
    }
}
