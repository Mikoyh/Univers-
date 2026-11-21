/*
 * =====================================================================
 *  MultiWorldProcessManager.cs - Manajemen Child Server, Auto-Create & Auto-Sleep
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TShockAPI;

namespace Univers.MultiWorld
{
    public enum WorldRuntimeState
    {
        Sleeping = 0,
        Booting = 1,
        Running = 2,
        Stopping = 3
    }

    public class ManagedChildProcess
    {
        public string WorldId { get; set; } = string.Empty;
        public int Port { get; set; }
        public int CallbackPort { get; set; }
        public Process? Process { get; set; }
        public WorldRuntimeState State { get; set; } = WorldRuntimeState.Sleeping;
        public int PlayerCount { get; set; } = 0;
        public List<string> OnlinePlayerNames { get; set; } = new();
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastNonEmptyUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
        public bool IpcRegistered { get; set; } = false;
        public readonly Queue<string> RecentLogs = new();
        public readonly object LogLock = new();
        public readonly SemaphoreSlim LifecycleLock = new(1, 1);
    }

    public class MultiWorldProcessManager : IDisposable
    {
        private readonly MultiWorldConfig _config;
        private readonly string _configPath;
        private readonly string _multiWorldRoot;
        private readonly string _worldsDir;
        private readonly string _instancesDir;
        private readonly int _actualMasterPort;
        private readonly ConcurrentDictionary<string, ManagedChildProcess> _children = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _configLock = new(1, 1);
        private bool _disposed = false;

        public MultiWorldProcessManager(MultiWorldConfig config, string configPath, string multiWorldRoot, int actualMasterPort)
        {
            _config = config;
            _configPath = configPath;
            _multiWorldRoot = multiWorldRoot;
            _worldsDir = Path.Combine(multiWorldRoot, "Worlds");
            _instancesDir = Path.Combine(multiWorldRoot, "Instances");
            _actualMasterPort = actualMasterPort;

            Directory.CreateDirectory(_worldsDir);
            Directory.CreateDirectory(_instancesDir);
        }

        public string GetWorldFilePath(WorldDefinition def)
        {
            string fileName = string.IsNullOrWhiteSpace(def.WorldFileName) ? $"{def.Id}.wld" : def.WorldFileName;
            return Path.IsPathRooted(fileName) ? fileName : Path.Combine(_worldsDir, fileName);
        }

        public ManagedChildProcess GetOrCreateRuntime(string worldId, int port)
        {
            return _children.GetOrAdd(worldId, id => new ManagedChildProcess
            {
                WorldId = id,
                Port = port,
                State = WorldRuntimeState.Sleeping,
                LastNonEmptyUtc = DateTime.UtcNow
            });
        }

        public void OnChildRegistered(string worldId, int port, int callbackPort, int pid, int playerCount, List<string>? playerNames)
        {
            var rt = GetOrCreateRuntime(worldId, port);
            rt.Port = port;
            if (callbackPort > 0) rt.CallbackPort = callbackPort;
            rt.IpcRegistered = true;
            rt.State = WorldRuntimeState.Running;
            rt.PlayerCount = playerCount;
            rt.OnlinePlayerNames = playerNames ?? new List<string>();
            rt.LastHeartbeatUtc = DateTime.UtcNow;
            if (playerCount > 0)
            {
                rt.LastNonEmptyUtc = DateTime.UtcNow;
            }
            TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Child '{worldId}' ONLINE (Port {port}, Callback {rt.CallbackPort}, PID {pid}).");
        }

        public void OnChildHeartbeat(string worldId, int callbackPort, int playerCount, List<string>? playerNames)
        {
            if (_children.TryGetValue(worldId, out var rt))
            {
                if (callbackPort > 0) rt.CallbackPort = callbackPort;
                rt.PlayerCount = Math.Max(0, playerCount);
                if (playerNames != null) rt.OnlinePlayerNames = playerNames;
                rt.LastHeartbeatUtc = DateTime.UtcNow;
                if (rt.PlayerCount > 0)
                {
                    rt.LastNonEmptyUtc = DateTime.UtcNow;
                }
            }
        }

        public List<(string WorldId, int CallbackPort)> GetRunningCallbackTargets(string? excludeWorldId = null)
        {
            var targets = new List<(string, int)>();
            foreach (var rt in _children.Values)
            {
                if (!string.IsNullOrEmpty(excludeWorldId) && rt.WorldId.Equals(excludeWorldId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (rt.Process != null && !rt.Process.HasExited && rt.State == WorldRuntimeState.Running && rt.CallbackPort > 0)
                {
                    targets.Add((rt.WorldId, rt.CallbackPort));
                }
            }
            return targets;
        }

        public List<WorldStatusSnapshot> GetWorldSnapshots(List<string> hubPlayerNames)
        {
            var list = new List<WorldStatusSnapshot>();
            DateTime now = DateTime.UtcNow;

            foreach (var def in _config.Worlds.ToList())
            {
                if (def.IsHub)
                {
                    list.Add(new WorldStatusSnapshot
                    {
                        Id = def.Id,
                        DisplayName = def.DisplayName,
                        Description = def.Description,
                        IsHub = true,
                        Port = _actualMasterPort,
                        Size = def.Size,
                        Difficulty = def.Difficulty,
                        Evil = def.Evil,
                        State = "Running",
                        PlayerCount = hubPlayerNames.Count,
                        OnlinePlayerNames = hubPlayerNames,
                        AutoSleep = false,
                        AutoSleepMinutes = 0,
                        IdleSeconds = 0,
                        PermissionRequired = def.PermissionRequired,
                        WorldFileExists = true
                    });
                    continue;
                }

                var rt = _children.TryGetValue(def.Id, out var found) ? found : null;
                bool alive = rt?.Process != null && !rt.Process.HasExited;
                string stateStr = rt == null
                    ? "Sleeping"
                    : (alive || rt.State == WorldRuntimeState.Booting || rt.State == WorldRuntimeState.Running)
                        ? rt.State.ToString()
                        : "Sleeping";

                int pCount = alive && rt != null ? rt.PlayerCount : 0;
                var pNames = alive && rt != null ? new List<string>(rt.OnlinePlayerNames) : new List<string>();
                int idleSec = alive && rt != null && pCount == 0 && rt.State == WorldRuntimeState.Running
                    ? Math.Max(0, (int)(now - rt.LastNonEmptyUtc).TotalSeconds)
                    : 0;

                list.Add(new WorldStatusSnapshot
                {
                    Id = def.Id,
                    DisplayName = def.DisplayName,
                    Description = def.Description,
                    IsHub = false,
                    Port = def.Port,
                    Size = def.Size,
                    Difficulty = def.Difficulty,
                    Evil = def.Evil,
                    State = stateStr,
                    PlayerCount = pCount,
                    OnlinePlayerNames = pNames,
                    AutoSleep = def.AutoSleep,
                    AutoSleepMinutes = _config.GetEffectiveAutoSleepMinutes(def),
                    IdleSeconds = idleSec,
                    PermissionRequired = def.PermissionRequired,
                    WorldFileExists = File.Exists(GetWorldFilePath(def))
                });
            }

            return list;
        }

        public async Task<(bool Ok, string Message)> EnsureWorldRunningAsync(string worldId, Action<string>? progressCallback = null)
        {
            var def = _config.GetWorld(worldId);
            if (def == null)
            {
                return (false, $"World '{worldId}' tidak ditemukan.");
            }
            if (def.IsHub)
            {
                return (true, $"Hub '{def.Id}' aktif di port {_actualMasterPort}.");
            }

            var rt = GetOrCreateRuntime(def.Id, def.Port);
            await rt.LifecycleLock.WaitAsync();
            try
            {
                if (rt.Process != null && !rt.Process.HasExited && rt.State == WorldRuntimeState.Running)
                {
                    rt.LastNonEmptyUtc = DateTime.UtcNow;
                    return (true, $"World '{def.Id}' sudah aktif di port {def.Port}.");
                }

                if (rt.Process != null && !rt.Process.HasExited && rt.State == WorldRuntimeState.Booting)
                {
                    // Tunggu proses boot yang sudah berjalan
                    return await WaitUntilReadyAsync(def, rt);
                }

                // Bersihkan port jika masih terpakai
                if (IsTcpPortListening(def.Port))
                {
                    def.Port = _config.AllocateNextFreePort();
                    await SaveConfigAsync();
                }

                var (exePath, baseArgs) = ResolveServerExecutable();
                if (string.IsNullOrEmpty(exePath))
                {
                    return (false, "TShock.Server.exe / TShock.Server.dll tidak ditemukan di folder server.");
                }

                string wldPath = GetWorldFilePath(def);
                bool willGenerate = !File.Exists(wldPath);

                string instDir = Path.Combine(_instancesDir, def.Id);
                string cfgDir = Path.Combine(instDir, "config");
                string logDir = Path.Combine(instDir, "logs");
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(logDir);
                PrepareChildTShockConfig(cfgDir, def.Port);

                var argList = new List<string>();
                if (!string.IsNullOrEmpty(baseArgs))
                {
                    argList.Add(baseArgs);
                }

                argList.Add($"-port {def.Port}");
                argList.Add($"-world \"{wldPath}\"");
                argList.Add($"-autocreate {def.GetSizeArg()}");
                argList.Add($"-worldname \"{SanitizeArgText(def.DisplayName)}\"");
                argList.Add($"-difficulty {def.GetDifficultyArg()}");
                argList.Add($"-worldevil {def.GetEvilArg()}");
                if (!string.IsNullOrWhiteSpace(def.Seed))
                {
                    argList.Add($"-seed \"{SanitizeArgText(def.Seed)}\"");
                }
                argList.Add($"-configpath \"{cfgDir}\"");
                argList.Add($"-logpath \"{logDir}\"");
                argList.Add($"-players {_config.MaxPlayersPerChild}");
                argList.Add("-noupnp");

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = string.Join(" ", argList),
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                psi.Environment["UNIVERS_CHILD_WORLD"] = def.Id;
                psi.Environment["UNIVERS_MASTER_IPC_PORT"] = _config.ControlIpcPort.ToString();
                psi.Environment["UNIVERS_MASTER_PORT"] = _actualMasterPort.ToString();
                psi.Environment["UNIVERS_CHILD_PORT"] = def.Port.ToString();

                rt.State = WorldRuntimeState.Booting;
                rt.IpcRegistered = false;
                rt.PlayerCount = 0;
                rt.Port = def.Port;
                rt.StartedUtc = DateTime.UtcNow;
                rt.LastNonEmptyUtc = DateTime.UtcNow;
                lock (rt.LogLock) rt.RecentLogs.Clear();

                progressCallback?.Invoke(willGenerate
                    ? $"Membuat & membangunkan world baru '{def.DisplayName}' ({def.Size}/{def.Difficulty})..."
                    : $"Membangunkan world '{def.DisplayName}'...");

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (_, e) => HandleChildOutput(rt, e.Data, false);
                proc.ErrorDataReceived += (_, e) => HandleChildOutput(rt, e.Data, true);
                proc.Exited += (_, _) =>
                {
                    rt.State = WorldRuntimeState.Sleeping;
                    rt.IpcRegistered = false;
                    rt.PlayerCount = 0;
                    TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Child '{def.Id}' berhenti (Sleep).");
                };

                if (!proc.Start())
                {
                    rt.State = WorldRuntimeState.Sleeping;
                    return (false, $"Gagal menyalakan proses child untuk '{def.Id}'.");
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                rt.Process = proc;

                TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Menyalakan child '{def.Id}' (Port {def.Port}, AutoCreate={willGenerate})...");
                return await WaitUntilReadyAsync(def, rt);
            }
            catch (Exception ex)
            {
                rt.State = WorldRuntimeState.Sleeping;
                return (false, $"Error menyalakan world '{def.Id}': {ex.Message}");
            }
            finally
            {
                rt.LifecycleLock.Release();
            }
        }

        private async Task<(bool Ok, string Message)> WaitUntilReadyAsync(WorldDefinition def, ManagedChildProcess rt)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(_config.WakeTimeoutSeconds);
            while (DateTime.UtcNow < deadline && !_disposed)
            {
                if (rt.Process == null || rt.Process.HasExited)
                {
                    string tail = GetRecentLogSummary(rt);
                    rt.State = WorldRuntimeState.Sleeping;
                    return (false, $"Child '{def.Id}' tertutup sebelum siap. Log: {tail}");
                }

                if (rt.IpcRegistered && IsTcpPortListening(def.Port))
                {
                    rt.State = WorldRuntimeState.Running;
                    rt.LastNonEmptyUtc = DateTime.UtcNow;
                    return (true, $"World '{def.Id}' aktif di port {def.Port}.");
                }

                await Task.Delay(350);
            }

            if (rt.IpcRegistered && IsTcpPortListening(def.Port))
            {
                rt.State = WorldRuntimeState.Running;
                return (true, $"World '{def.Id}' aktif di port {def.Port}.");
            }

            return (false, $"Timeout menunggu world '{def.Id}' bangun ({_config.WakeTimeoutSeconds} detik).");
        }

        public async Task<(bool Ok, string Message)> StopWorldAsync(string worldId)
        {
            var def = _config.GetWorld(worldId);
            if (def == null) return (false, $"World '{worldId}' tidak ditemukan.");
            if (def.IsHub) return (false, "Hub utama tidak bisa dimatikan dengan perintah sleep/stop.");

            if (!_children.TryGetValue(def.Id, out var rt) || rt.Process == null || rt.Process.HasExited)
            {
                if (rt != null) rt.State = WorldRuntimeState.Sleeping;
                return (true, $"World '{def.Id}' sudah dalam status Sleeping.");
            }

            await rt.LifecycleLock.WaitAsync();
            try
            {
                if (rt.Process == null || rt.Process.HasExited)
                {
                    rt.State = WorldRuntimeState.Sleeping;
                    return (true, $"World '{def.Id}' sudah dalam status Sleeping.");
                }

                rt.State = WorldRuntimeState.Stopping;
                try
                {
                    await rt.Process.StandardInput.WriteLineAsync("exit");
                    await rt.Process.StandardInput.FlushAsync();
                }
                catch { }

                using var cts = new CancellationTokenSource(12000);
                try
                {
                    await rt.Process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { rt.Process.Kill(entireProcessTree: true); } catch { }
                }

                rt.State = WorldRuntimeState.Sleeping;
                rt.IpcRegistered = false;
                rt.PlayerCount = 0;
                return (true, $"World '{def.Id}' telah disimpan & dimatikan (Sleeping).");
            }
            finally
            {
                rt.LifecycleLock.Release();
            }
        }

        public async Task<(bool Ok, string Message)> CreateWorldAsync(
            string rawId,
            string size = "medium",
            string difficulty = "classic",
            string evil = "random",
            string seed = "",
            bool bootImmediately = true)
        {
            string id = MultiWorldConfig.SanitizeWorldId(rawId);
            if (string.IsNullOrWhiteSpace(id) || id.Length < 2)
            {
                return (false, "ID world minimal 2 karakter (huruf/angka/_/-). Contoh: /mw create event1 small expert");
            }

            if (_config.GetWorld(id) != null)
            {
                return (false, $"World dengan ID '{id}' sudah ada. Gunakan /mw tp {id} untuk masuk.");
            }

            WorldDefinition def;
            await _configLock.WaitAsync();
            try
            {
                int port = _config.AllocateNextFreePort();
                def = new WorldDefinition
                {
                    Id = id,
                    DisplayName = char.ToUpperInvariant(id[0]) + id.Substring(1),
                    Description = $"World '{id}' ({MultiWorldConfig.NormalizeSize(size)} / {MultiWorldConfig.NormalizeDifficulty(difficulty)})",
                    IsHub = false,
                    Port = port,
                    WorldFileName = $"{id}.wld",
                    Size = MultiWorldConfig.NormalizeSize(size),
                    Difficulty = MultiWorldConfig.NormalizeDifficulty(difficulty),
                    Evil = MultiWorldConfig.NormalizeEvil(evil),
                    Seed = (seed ?? "").Trim(),
                    AutoStart = false,
                    AutoSleep = true,
                    EmptyShutdownMinutes = -1,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _config.Worlds.Add(def);
                _config.Save(_configPath);
            }
            finally
            {
                _configLock.Release();
            }

            if (!bootImmediately)
            {
                return (true, $"World '{def.Id}' terdaftar (Port {def.Port}, {def.Size}/{def.Difficulty}).");
            }

            var (ok, msg) = await EnsureWorldRunningAsync(def.Id);
            if (!ok)
            {
                return (false, $"World terdaftar, tapi gagal auto-create saat boot: {msg}");
            }

            return (true, $"World '{def.Id}' SIAP DIMAINKAN! ({def.Size}, {def.Difficulty}, Port {def.Port}). Ketik /mw tp {def.Id} untuk masuk.");
        }

        public async Task<(bool Ok, string Message)> DeleteWorldAsync(string worldId, bool deleteWorldFiles = true)
        {
            var def = _config.GetWorld(worldId);
            if (def == null) return (false, $"World '{worldId}' tidak ditemukan.");
            if (def.IsHub) return (false, "Hub utama tidak bisa dihapus!");

            await StopWorldAsync(def.Id);

            await _configLock.WaitAsync();
            try
            {
                _config.Worlds.RemoveAll(w => w.Id.Equals(def.Id, StringComparison.OrdinalIgnoreCase));
                _config.Save(_configPath);
            }
            finally
            {
                _configLock.Release();
            }

            _children.TryRemove(def.Id, out _);

            if (deleteWorldFiles)
            {
                try
                {
                    string wld = GetWorldFilePath(def);
                    if (File.Exists(wld)) File.Delete(wld);
                    if (File.Exists(wld + ".bak")) File.Delete(wld + ".bak");
                    if (File.Exists(wld + ".bak2")) File.Delete(wld + ".bak2");

                    string instDir = Path.Combine(_instancesDir, def.Id);
                    if (Directory.Exists(instDir)) Directory.Delete(instDir, true);
                }
                catch (Exception ex)
                {
                    return (true, $"World '{def.Id}' dihapus dari daftar, tapi sebagian file masih terkunci: {ex.Message}");
                }
            }

            return (true, $"World '{def.Id}' dan seluruh filenya berhasil dihapus permanen.");
        }

        public async Task<(bool Ok, string Message)> SetAutoSleepAsync(string worldId, bool enabled, int? minutesOverride = null)
        {
            var def = _config.GetWorld(worldId);
            if (def == null) return (false, $"World '{worldId}' tidak ditemukan.");
            if (def.IsHub) return (false, "Hub utama selalu aktif dan tidak memakai Auto-Sleep.");

            await _configLock.WaitAsync();
            try
            {
                def.AutoSleep = enabled;
                if (minutesOverride.HasValue)
                {
                    def.EmptyShutdownMinutes = Math.Clamp(minutesOverride.Value, 1, 1440);
                }
                _config.Save(_configPath);
            }
            finally
            {
                _configLock.Release();
            }

            int eff = _config.GetEffectiveAutoSleepMinutes(def);
            return (true, enabled
                ? $"Auto-Sleep world '{def.Id}' AKTIF ({eff} menit kosong -> otomatis save & mati)."
                : $"Auto-Sleep world '{def.Id}' NONAKTIF.");
        }

        public async Task<(bool Ok, string Message)> SetPermissionAsync(string worldId, string permission)
        {
            var def = _config.GetWorld(worldId);
            if (def == null) return (false, $"World '{worldId}' tidak ditemukan.");

            await _configLock.WaitAsync();
            try
            {
                def.PermissionRequired = (permission.Equals("none", StringComparison.OrdinalIgnoreCase) || permission == "-")
                    ? string.Empty
                    : permission.Trim();
                _config.Save(_configPath);
            }
            finally
            {
                _configLock.Release();
            }

            return (true, string.IsNullOrEmpty(def.PermissionRequired)
                ? $"Akses world '{def.Id}' dibuka untuk semua player."
                : $"Akses world '{def.Id}' dibatasi permission '{def.PermissionRequired}'.");
        }

        public async Task CheckAutoSleepTickAsync()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var def in _config.Worlds.ToList())
            {
                if (def.IsHub || !def.AutoSleep) continue;
                int timeoutMin = _config.GetEffectiveAutoSleepMinutes(def);
                if (timeoutMin <= 0) continue;

                if (!_children.TryGetValue(def.Id, out var rt)) continue;
                if (rt.Process == null || rt.Process.HasExited || rt.State != WorldRuntimeState.Running) continue;
                if (rt.PlayerCount > 0) continue;

                TimeSpan idle = now - rt.LastNonEmptyUtc;
                if (idle >= TimeSpan.FromMinutes(timeoutMin))
                {
                    TShock.Log.ConsoleInfo($"[Univers.MultiWorld] World '{def.Id}' kosong selama {timeoutMin} menit -> Auto-Sleep (save & shutdown)...");
                    await StopWorldAsync(def.Id);
                }
            }
        }

        public async Task BootAutoStartWorldsAsync()
        {
            foreach (var def in _config.Worlds.Where(w => !w.IsHub && w.AutoStart).ToList())
            {
                await EnsureWorldRunningAsync(def.Id);
            }
        }

        public void ShutdownAll()
        {
            if (_disposed) return;
            _disposed = true;

            var running = _children.Values
                .Where(c => c.Process != null && !c.Process.HasExited)
                .ToList();

            if (running.Count == 0) return;

            TShock.Log.ConsoleInfo($"[Univers.MultiWorld] Menutup {running.Count} child world dengan aman (save & exit)...");
            foreach (var child in running)
            {
                try
                {
                    child.Process?.StandardInput.WriteLine("exit");
                    child.Process?.StandardInput.Flush();
                }
                catch { }
            }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 7000)
            {
                if (running.All(c => c.Process == null || c.Process.HasExited)) break;
                Thread.Sleep(200);
            }

            foreach (var child in running)
            {
                try
                {
                    if (child.Process != null && !child.Process.HasExited)
                    {
                        child.Process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            ShutdownAll();
            try { _configLock.Dispose(); } catch { }
        }

        private async Task SaveConfigAsync()
        {
            await _configLock.WaitAsync();
            try { _config.Save(_configPath); }
            finally { _configLock.Release(); }
        }

        private void PrepareChildTShockConfig(string cfgDir, int childPort)
        {
            try
            {
                string targetCfgPath = Path.Combine(cfgDir, "config.json");
                if (File.Exists(targetCfgPath)) return;

                string masterCfgPath = Path.Combine(TShock.SavePath, "config.json");
                JObject root;
                if (File.Exists(masterCfgPath))
                {
                    root = JObject.Parse(File.ReadAllText(masterCfgPath));
                }
                else
                {
                    root = new JObject { ["Settings"] = new JObject() };
                }

                if (root["Settings"] is not JObject settings)
                {
                    settings = root;
                }

                settings["ServerPassword"] = "";
                settings["RestApiEnabled"] = false;
                settings["MaxSlots"] = _config.MaxPlayersPerChild;
                settings["ServerPort"] = childPort;
                settings["RequireLogin"] = false;
                settings["AutoSave"] = true;
                settings["DisableLoginBeforeJoin"] = true;

                File.WriteAllText(targetCfgPath, root.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Gagal menyiapkan config child: {ex.Message}");
            }
        }

        private static (string ExePath, string BaseArgs) ResolveServerExecutable()
        {
            string baseDir = AppContext.BaseDirectory;
            string exeWin = Path.Combine(baseDir, "TShock.Server.exe");
            if (File.Exists(exeWin)) return (exeWin, string.Empty);

            string exeVanilla = Path.Combine(baseDir, "TerrariaServer.exe");
            if (File.Exists(exeVanilla)) return (exeVanilla, string.Empty);

            string binLinux = Path.Combine(baseDir, "TShock.Server");
            if (File.Exists(binLinux)) return (binLinux, string.Empty);

            string dllPath = Path.Combine(baseDir, "TShock.Server.dll");
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath))
            {
                string procName = Path.GetFileNameWithoutExtension(procPath);
                if (procName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && File.Exists(dllPath))
                {
                    return (procPath, $"\"{dllPath}\"");
                }
                return (procPath, string.Empty);
            }

            return (string.Empty, string.Empty);
        }

        private static void HandleChildOutput(ManagedChildProcess rt, string? line, bool isError)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            lock (rt.LogLock)
            {
                rt.RecentLogs.Enqueue(line);
                while (rt.RecentLogs.Count > 25) rt.RecentLogs.Dequeue();
            }

            if (isError ||
                line.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Generating", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Listening on port", StringComparison.OrdinalIgnoreCase))
            {
                TShock.Log.ConsoleInfo($"[MW:{rt.WorldId}] {line}");
            }
        }

        private static string GetRecentLogSummary(ManagedChildProcess rt)
        {
            lock (rt.LogLock)
            {
                return rt.RecentLogs.Count == 0
                    ? "(tidak ada output)"
                    : string.Join(" | ", rt.RecentLogs.TakeLast(4));
            }
        }

        private static bool IsTcpPortListening(int port)
        {
            try
            {
                using var tcp = new TcpClient();
                var ar = tcp.BeginConnect(System.Net.IPAddress.Loopback, port, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(150);
                if (!ok) return false;
                tcp.EndConnect(ar);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SanitizeArgText(string raw)
        {
            return (raw ?? string.Empty).Replace("\"", "").Trim();
        }
    }
}
