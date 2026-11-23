/*
 * =====================================================================
 *  MultiWorldIpc.cs - Jembatan Kontrol TCP Localhost (Master <-> Child)
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 *  Mendukung sinkronisasi player roster, cross-world chat, dan push notice
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.MultiWorld
{
    public class WorldStatusSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsHub { get; set; }
        public int Port { get; set; }
        public string Size { get; set; } = "medium";
        public string Difficulty { get; set; } = "classic";
        public string Evil { get; set; } = "random";
        public string State { get; set; } = "Sleeping"; // Running, Booting, Sleeping
        public int PlayerCount { get; set; }
        public List<string> OnlinePlayerNames { get; set; } = new();
        public bool AutoSleep { get; set; }
        public int AutoSleepMinutes { get; set; }
        public int IdleSeconds { get; set; }
        public string PermissionRequired { get; set; } = string.Empty;
        public bool WorldFileExists { get; set; }
        public int ControlIpcPort { get; set; }
    }

    public class IpcRequest
    {
        public string Action { get; set; } = string.Empty;
        public string WorldId { get; set; } = string.Empty;
        public int Port { get; set; }
        public int CallbackPort { get; set; }
        public int Pid { get; set; }
        public int PlayerCount { get; set; }
        public List<string> OnlinePlayerNames { get; set; } = new();
        public string PlayerName { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string ClientUuid { get; set; } = string.Empty;
        public bool HasWildcardPermission { get; set; }
        public string GroupPermissions { get; set; } = string.Empty;
        public string SscSnapshotJson { get; set; } = string.Empty;
        public string TargetWorldId { get; set; } = string.Empty;
        public string FormattedMessage { get; set; } = string.Empty;
        public byte ColorR { get; set; } = 255;
        public byte ColorG { get; set; } = 255;
        public byte ColorB { get; set; } = 255;
        public string AdminSubAction { get; set; } = string.Empty;
        public List<string> Args { get; set; } = new();
    }

    public class IpcResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string ClientUuid { get; set; } = string.Empty;
        public bool HasWildcardPermission { get; set; }
        public string GroupPermissions { get; set; } = string.Empty;
        public string SscSnapshotJson { get; set; } = string.Empty;
        public List<WorldStatusSnapshot> Worlds { get; set; } = new();
    }

    public class MultiWorldIpcServer : IDisposable
    {
        private readonly int _port;
        private readonly Func<IpcRequest, Task<IpcResponse>> _handler;
        private readonly CancellationTokenSource _cts = new();
        private TcpListener? _listener;

        public int BoundPort { get; private set; }

        public MultiWorldIpcServer(int port, Func<IpcRequest, Task<IpcResponse>> handler)
        {
            _port = port;
            _handler = handler;
        }

        public bool Start(bool logStartup = true)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _ = AcceptLoopAsync(_cts.Token);
                if (logStartup)
                {
                    TShock.Log.ConsoleInfo($"[Univers.MultiWorld] IPC Coordinator aktif di 127.0.0.1:{BoundPort}");
                }
                return true;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.MultiWorld] Gagal membuka IPC port {_port}: {ex.Message}");
                return false;
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                    _ = HandleClientAsync(client, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        TShock.Log.ConsoleWarn($"[Univers.MultiWorld] IPC accept warning: {ex.Message}");
                    }
                    await Task.Delay(100, ct);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

                    string? line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrWhiteSpace(line)) return;

                    var req = JsonConvert.DeserializeObject<IpcRequest>(line);
                    IpcResponse res;
                    if (req == null)
                    {
                        res = new IpcResponse { Success = false, Message = "Invalid IPC payload." };
                    }
                    else
                    {
                        res = await _handler(req);
                    }

                    await writer.WriteLineAsync(JsonConvert.SerializeObject(res));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleWarn($"[Univers.MultiWorld] IPC handler error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _cts.Dispose(); } catch { }
        }
    }

    public static class MultiWorldIpcClient
    {
        public static async Task<IpcResponse> SendAsync(int targetIpcPort, IpcRequest req, int timeoutMs = 4000)
        {
            if (targetIpcPort <= 0)
            {
                return new IpcResponse { Success = false, Message = "Invalid IPC port." };
            }

            try
            {
                using var client = new TcpClient();
                client.NoDelay = true;
                using var cts = new CancellationTokenSource(timeoutMs);
                await client.ConnectAsync(IPAddress.Loopback, targetIpcPort, cts.Token);

                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);

                await writer.WriteLineAsync(JsonConvert.SerializeObject(req));
                string? line = await reader.ReadLineAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return new IpcResponse { Success = false, Message = "Master IPC tidak merespons." };
                }

                return JsonConvert.DeserializeObject<IpcResponse>(line)
                    ?? new IpcResponse { Success = false, Message = "Respons IPC tidak valid." };
            }
            catch (Exception ex)
            {
                return new IpcResponse { Success = false, Message = $"Koneksi IPC gagal: {ex.Message}" };
            }
        }
    }
}
