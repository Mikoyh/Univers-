/*
 * =====================================================================
 *  MultiWorldProxy.cs - Built-in Async TCP Gateway Proxy Terraria 1.4.5
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 *  Memungkinkan player pindah antar port/world pada satu alamat koneksi!
 * =====================================================================
 */

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TShockAPI;

namespace Univers.MultiWorld
{
    public class ProxySession : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public TcpClient Client { get; }
        public NetworkStream ClientStream { get; }
        public TcpClient? Backend { get; set; }
        public NetworkStream? BackendStream { get; set; }

        public string CurrentWorldId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string ClientUuid { get; set; } = string.Empty;

        public byte ClientSlot { get; set; } = 255;
        public byte BackendSlot { get; set; } = 255;
        public string ClientRemoteIp { get; } = "unknown";
        public volatile bool IsSwitching = false;
        public volatile bool IsClosed = false;

        public readonly SemaphoreSlim ClientWriteLock = new(1, 1);
        public readonly SemaphoreSlim BackendWriteLock = new(1, 1);
        public readonly SemaphoreSlim SwitchLock = new(1, 1);
        public readonly CancellationTokenSource Cts = new();

        // Cache paket penting untuk handshake replay ke Backend baru
        public byte[]? ConnectRequestPacket;
        public byte[]? ClientUuidPacket;
        public byte[]? PlayerInfoPacket;
        public byte[]? PlayerHpPacket;
        public byte[]? PlayerManaPacket;
        public byte[]? PlayerBuffPacket;
        public byte[]? PlayerSpawnPacket;
        public byte[]? RequestTilesPacket;
        public readonly Dictionary<short, byte[]> PlayerSlotPackets = new();
        public readonly object CacheLock = new();

        // Tracking entitas di backend saat ini untuk pembersihan saat pindah world
        public readonly HashSet<byte> ActiveOtherPlayers = new();
        public readonly HashSet<(short ProjId, byte Owner)> ActiveProjectiles = new();
        public readonly object EntityLock = new();

        public ProxySession(TcpClient client, string initialWorldId)
        {
            Client = client;
            Client.NoDelay = true;
            try
            {
                ClientRemoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
            }
            catch { }
            ClientStream = client.GetStream();
            CurrentWorldId = initialWorldId;
        }

        public void Dispose()
        {
            if (IsClosed) return;
            IsClosed = true;
            try { Cts.Cancel(); } catch { }
            try { BackendStream?.Dispose(); } catch { }
            try { Backend?.Dispose(); } catch { }
            try { ClientStream?.Dispose(); } catch { }
            try { Client?.Dispose(); } catch { }
            try { ClientWriteLock.Dispose(); } catch { }
            try { BackendWriteLock.Dispose(); } catch { }
            try { SwitchLock.Dispose(); } catch { }
            try { Cts.Dispose(); } catch { }
        }
    }

    public class MultiWorldProxy : IDisposable
    {
        private readonly string _hubWorldId;
        private readonly int _hubPort;
        private readonly ConcurrentDictionary<string, ProxySession> _sessions = new();
        private readonly CancellationTokenSource _cts = new();
        private TcpListener? _listener;

        public int ListenPort { get; private set; }
        public bool IsListening { get; private set; }

        public MultiWorldProxy(string hubWorldId, int hubPort)
        {
            _hubWorldId = hubWorldId;
            _hubPort = hubPort;
        }

        public bool Start(int preferredPort, int fallbackPort)
        {
            if (TryBind(preferredPort))
            {
                ListenPort = preferredPort;
                IsListening = true;
                _ = AcceptLoopAsync(_cts.Token);
                return true;
            }

            if (fallbackPort != preferredPort && TryBind(fallbackPort))
            {
                ListenPort = fallbackPort;
                IsListening = true;
                _ = AcceptLoopAsync(_cts.Token);
                return true;
            }

            IsListening = false;
            return false;
        }

        private bool TryBind(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start();
                _listener = l;
                return true;
            }
            catch
            {
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
                    var session = new ProxySession(client, _hubWorldId);
                    _sessions[session.Id] = session;
                    _ = RunSessionAsync(session);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch
                {
                    await Task.Delay(50, ct);
                }
            }
        }

        public ProxySession? FindSessionByPlayerName(string playerName)
        {
            return FindSession(playerName, null, null, null);
        }

        public ProxySession? FindSession(string playerName, string? clientUuid = null, int? backendSlot = null, string? currentWorldId = null)
        {
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                var byName = _sessions.Values.FirstOrDefault(s =>
                    !s.IsClosed &&
                    !string.IsNullOrWhiteSpace(s.PlayerName) &&
                    s.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;
            }

            if (!string.IsNullOrWhiteSpace(clientUuid))
            {
                var byUuid = _sessions.Values.FirstOrDefault(s =>
                    !s.IsClosed &&
                    !string.IsNullOrWhiteSpace(s.ClientUuid) &&
                    s.ClientUuid.Equals(clientUuid, StringComparison.OrdinalIgnoreCase));
                if (byUuid != null)
                {
                    if (!string.IsNullOrWhiteSpace(playerName) && string.IsNullOrWhiteSpace(byUuid.PlayerName))
                    {
                        byUuid.PlayerName = playerName;
                    }
                    return byUuid;
                }
            }

            if (backendSlot.HasValue && backendSlot.Value >= 0 && backendSlot.Value < 255)
            {
                byte slotByte = (byte)backendSlot.Value;
                var bySlot = _sessions.Values.FirstOrDefault(s =>
                    !s.IsClosed &&
                    s.BackendSlot == slotByte &&
                    (string.IsNullOrWhiteSpace(currentWorldId) ||
                     s.CurrentWorldId.Equals(currentWorldId, StringComparison.OrdinalIgnoreCase)));
                if (bySlot != null)
                {
                    if (!string.IsNullOrWhiteSpace(playerName) && string.IsNullOrWhiteSpace(bySlot.PlayerName))
                    {
                        bySlot.PlayerName = playerName;
                    }
                    return bySlot;
                }
            }

            return null;
        }

        private async Task RunSessionAsync(ProxySession session)
        {
            try
            {
                var backend = new TcpClient { NoDelay = true };
                await backend.ConnectAsync(IPAddress.Loopback, _hubPort, session.Cts.Token);
                session.Backend = backend;
                session.BackendStream = backend.GetStream();

                _ = PumpBackendToClientAsync(session, backend, session.BackendStream);
                await PumpClientToBackendAsync(session);
            }
            catch
            {
            }
            finally
            {
                _sessions.TryRemove(session.Id, out _);
                session.Dispose();
            }
        }

        private async Task PumpClientToBackendAsync(ProxySession session)
        {
            var ct = session.Cts.Token;
            while (!ct.IsCancellationRequested && !session.IsClosed)
            {
                byte[]? packet = await ReadPacketAsync(session.ClientStream, ct);
                if (packet == null) break;

                CacheClientPacket(session, packet);

                if (session.IsSwitching)
                {
                    // Selama masa switch, tunda/buang paket gameplay sementara agar tidak nyasar ke socket lama
                    continue;
                }

                var stream = session.BackendStream;
                if (stream == null) continue;

                byte[] outgoing = PrepareOutboundPacket(session, packet, session.ClientSlot, session.BackendSlot);
                await SendPacketAsync(stream, session.BackendWriteLock, outgoing, ct);
            }
        }

        private async Task PumpBackendToClientAsync(ProxySession session, TcpClient myBackend, NetworkStream myStream)
        {
            var ct = session.Cts.Token;
            while (!ct.IsCancellationRequested && !session.IsClosed)
            {
                byte[]? packet;
                try
                {
                    packet = await ReadPacketAsync(myStream, ct);
                }
                catch
                {
                    packet = null;
                }

                if (packet == null)
                {
                    // Jika socket ini diganti oleh SwitchWorldAsync, loop lama berhenti tanpa memutus Client
                    if (session.IsSwitching || !ReferenceEquals(session.Backend, myBackend))
                    {
                        return;
                    }

                    // Backend aktif terputus tiba-tiba -> tutup sesi client dengan bersih
                    session.Dispose();
                    return;
                }

                if (session.IsSwitching || !ReferenceEquals(session.Backend, myBackend))
                {
                    return;
                }

                byte msgId = packet[2];
                if (msgId == 3 && packet.Length >= 4) // ContinueConnecting
                {
                    session.ClientSlot = packet[3];
                    session.BackendSlot = packet[3];
                }
                else
                {
                    TrackBackendEntityPacket(session, packet);
                }

                byte[] toClient = PrepareOutboundPacket(session, packet, session.BackendSlot, session.ClientSlot);
                CacheServerSelfPacket(session, toClient);
                bool sent = await SendPacketAsync(session.ClientStream, session.ClientWriteLock, toClient, ct);
                if (!sent)
                {
                    session.Dispose();
                    return;
                }
            }
        }

        public async Task<(bool Ok, string Message)> SwitchPlayerWorldAsync(
            string playerName,
            string targetWorldId,
            int targetPort,
            string? clientUuid = null,
            int? backendSlot = null,
            string? currentWorldId = null)
        {
            var session = FindSession(playerName, clientUuid, backendSlot, currentWorldId);
            if (session == null)
            {
                return (false, $"Sesi proxy untuk '{playerName}' tidak ditemukan. Pastikan kamu terhubung melalui Gateway Proxy (Port {ListenPort}).");
            }

            if (session.CurrentWorldId.Equals(targetWorldId, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Kamu sudah berada di world '{targetWorldId}'.");
            }

            await session.SwitchLock.WaitAsync();
            TcpClient? newBackend = null;
            bool oldBackendClosed = false;
            try
            {
                if (session.CurrentWorldId.Equals(targetWorldId, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Kamu sudah berada di world '{targetWorldId}'.");
                }

                if (session.IsClosed || session.ConnectRequestPacket == null || session.PlayerInfoPacket == null)
                {
                    return (false, "Data handshake client belum siap untuk berpindah world.");
                }

                newBackend = new TcpClient { NoDelay = true };
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(session.Cts.Token);
                connectCts.CancelAfter(10_000);
                await newBackend.ConnectAsync(IPAddress.Loopback, targetPort, connectCts.Token);
                var newStream = newBackend.GetStream();

                // 1. Kirim ConnectRequest (1) ke Backend B
                await SendRawAsync(newStream, session.ConnectRequestPacket, connectCts.Token);

                // 2. Baca sampai Backend B memberi ContinueConnecting (3)
                byte newBackendSlot = 255;
                while (true)
                {
                    byte[]? hs = await ReadPacketAsync(newStream, connectCts.Token);
                    if (hs == null)
                    {
                        string msg = "Backend tujuan menutup koneksi saat handshake.";
                        TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Switch '{playerName}' -> '{targetWorldId}' gagal: {msg}");
                        return (false, msg);
                    }

                    byte id = hs[2];
                    if (id == 2) // Disconnect
                    {
                        string reason = TryReadNetworkText(hs, 3);
                        string msg = $"Ditolak oleh world '{targetWorldId}': {reason}";
                        TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Switch '{playerName}' -> '{targetWorldId}' gagal: {msg}");
                        return (false, msg);
                    }
                    if (id == 3 && hs.Length >= 4)
                    {
                        newBackendSlot = hs[3];
                        break;
                    }
                }

                // 3. Hentikan aliran Backend lama & bersihkan entitas lama di Client
                session.IsSwitching = true;
                var oldBackend = session.Backend;
                var oldStream = session.BackendStream;
                session.Backend = newBackend;
                session.BackendStream = newStream;
                oldBackendClosed = true;

                try { oldStream?.Close(); } catch { }
                try { oldBackend?.Close(); } catch { }

                await SendEntityClearsToClientAsync(session);
                session.BackendSlot = newBackendSlot;

                // 4. Replay handshake & live inventory ke Backend B
                await ReplayClientHandshakeToBackendAsync(session, newStream, newBackendSlot);

                // 5. Terima WorldInfo (7) -> kirim RequestTileData (8) -> teruskan Chunk & CompleteConnection (49)
                bool sentTileRequest = false;
                bool completedSpawn = false;
                var seenIds = new List<byte>();
                using var step5Cts = CancellationTokenSource.CreateLinkedTokenSource(session.Cts.Token);
                step5Cts.CancelAfter(TimeSpan.FromSeconds(45));

                while (!step5Cts.IsCancellationRequested && !session.IsClosed)
                {
                    byte[]? srvPkt = await ReadPacketAsync(newStream, step5Cts.Token);
                    if (srvPkt == null)
                    {
                        string cause = step5Cts.IsCancellationRequested
                            ? "Timeout 45d menunggu CompleteConnection (49)"
                            : "Child menutup koneksi sebelum selesai";
                        string pkts = seenIds.Count > 0 ? string.Join(",", seenIds.TakeLast(20)) : "-";
                        string msg = $"Koneksi ke '{targetWorldId}' terputus saat memuat peta.";
                        TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Switch '{playerName}' -> '{targetWorldId}' gagal ({cause} | Paket: [{pkts}]).");
                        await SendClientDisconnectAndDisposeAsync(session, msg);
                        return (false, msg);
                    }

                    byte srvId = srvPkt[2];
                    seenIds.Add(srvId);
                    if (srvId == 2)
                    {
                        string reason = TryReadNetworkText(srvPkt, 3);
                        string msg = $"World '{targetWorldId}' memutus koneksi: {reason}";
                        TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Switch '{playerName}' -> '{targetWorldId}' gagal: {msg}");
                        await SendClientDisconnectAndDisposeAsync(session, msg);
                        return (false, msg);
                    }

                    TrackBackendEntityPacket(session, srvPkt);

                    if (srvId == 49) // CompleteConnection
                    {
                        session.CurrentWorldId = targetWorldId;
                        session.IsSwitching = false;
                        byte[] mappedComplete = PrepareOutboundPacket(session, srvPkt, session.BackendSlot, session.ClientSlot);
                        await SendPacketAsync(session.ClientStream, session.ClientWriteLock, mappedComplete, session.Cts.Token);

                        // Kirim Packet 12 dengan koordinat spawn (-1, -1) agar server memakai titik spawn world BARU (bukan koordinat lobby!)
                        byte[]? cachedSpawn;
                        lock (session.CacheLock) cachedSpawn = session.PlayerSpawnPacket;
                        if (cachedSpawn != null && cachedSpawn.Length >= 8)
                        {
                            byte[] mappedSpawn12 = PrepareOutboundPacket(session, cachedSpawn, session.ClientSlot, newBackendSlot);
                            BinaryPrimitives.WriteInt16LittleEndian(mappedSpawn12.AsSpan(4, 2), -1);
                            BinaryPrimitives.WriteInt16LittleEndian(mappedSpawn12.AsSpan(6, 2), -1);
                            await SendPacketAsync(newStream, session.BackendWriteLock, mappedSpawn12, session.Cts.Token);
                        }

                        completedSpawn = true;
                        break;
                    }

                    byte[] mapped = PrepareOutboundPacket(session, srvPkt, session.BackendSlot, session.ClientSlot);
                    await SendPacketAsync(session.ClientStream, session.ClientWriteLock, mapped, session.Cts.Token);

                    if (srvId == 7 && !sentTileRequest) // WorldInfo -> baca koordinat spawn asli & minta section spawn!
                    {
                        sentTileRequest = true;
                        int targetSpawnX = -1;
                        int targetSpawnY = -1;
                        if (srvPkt.Length >= 23)
                        {
                            targetSpawnX = BinaryPrimitives.ReadInt32LittleEndian(srvPkt.AsSpan(15, 4));
                            targetSpawnY = BinaryPrimitives.ReadInt32LittleEndian(srvPkt.AsSpan(19, 4));
                        }
                        byte[] reqTiles = BuildRequestEssentialTilesPacket(session, targetSpawnX, targetSpawnY);
                        await SendPacketAsync(newStream, session.BackendWriteLock, reqTiles, session.Cts.Token);
                    }
                }

                if (!completedSpawn)
                {
                    string pkts = seenIds.Count > 0 ? string.Join(",", seenIds.TakeLast(20)) : "-";
                    string msg = $"Timeout memuat data world '{targetWorldId}'.";
                    TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Switch '{playerName}' -> '{targetWorldId}' gagal: {msg} | Paket: [{pkts}]");
                    await SendClientDisconnectAndDisposeAsync(session, msg);
                    return (false, msg);
                }

                // Jalankan pump loop baru untuk Backend B
                _ = PumpBackendToClientAsync(session, newBackend, newStream);
                TShock.Log.ConsoleInfo($"[Univers.MultiWorld] '{playerName}' ({session.ClientRemoteIp}) pindah ke '{targetWorldId}' (Port {targetPort}).");
                return (true, $"Berhasil berpindah ke world '{targetWorldId}'.");
            }
            catch (Exception ex)
            {
                try { newBackend?.Close(); } catch { }
                session.IsSwitching = false;
                TShock.Log.ConsoleWarn($"[Univers.MultiWorld] Switch '{playerName}' -> '{targetWorldId}' error: {ex.Message}");
                if (oldBackendClosed)
                {
                    await SendClientDisconnectAndDisposeAsync(session, $"Gagal pindah ke '{targetWorldId}': {ex.Message}");
                }
                return (false, $"Gagal switch world: {ex.Message}");
            }
            finally
            {
                session.SwitchLock.Release();
            }
        }

        private async Task SendEntityClearsToClientAsync(ProxySession session)
        {
            List<byte> playersToClear;
            List<(short ProjId, byte Owner)> projsToClear;
            lock (session.EntityLock)
            {
                playersToClear = session.ActiveOtherPlayers
                    .Where(p => p != session.ClientSlot)
                    .ToList();
                projsToClear = session.ActiveProjectiles
                    .ToList();
                session.ActiveOtherPlayers.Clear();
                session.ActiveProjectiles.Clear();
            }

            foreach (byte pid in playersToClear)
            {
                byte[] pkt = new byte[] { 5, 0, 14, pid, 0 };
                await SendPacketAsync(session.ClientStream, session.ClientWriteLock, pkt, session.Cts.Token);
            }

            foreach (var (projId, owner) in projsToClear)
            {
                byte[] pkt = new byte[6];
                pkt[0] = 6;
                pkt[1] = 0;
                pkt[2] = 29; // KillProjectile
                BinaryPrimitives.WriteInt16LittleEndian(pkt.AsSpan(3, 2), projId);
                pkt[5] = owner;
                await SendPacketAsync(session.ClientStream, session.ClientWriteLock, pkt, session.Cts.Token);
            }
        }

        private async Task ReplayClientHandshakeToBackendAsync(ProxySession session, NetworkStream newStream, byte targetSlot)
        {
            byte[]? uuidPkt;
            byte[]? infoPkt;
            byte[]? hpPkt;
            byte[]? manaPkt;
            byte[]? buffPkt;
            List<byte[]> slotPkts;

            lock (session.CacheLock)
            {
                uuidPkt = session.ClientUuidPacket;
                infoPkt = session.PlayerInfoPacket;
                hpPkt = session.PlayerHpPacket;
                manaPkt = session.PlayerManaPacket;
                buffPkt = session.PlayerBuffPacket;
                slotPkts = session.PlayerSlotPackets
                    .OrderBy(k => k.Key)
                    .Select(k => k.Value)
                    .ToList();
            }

            if (infoPkt != null)
            {
                await newStream.WriteAsync(PrepareOutboundPacket(session, infoPkt, session.ClientSlot, targetSlot), session.Cts.Token);
            }

            if (uuidPkt != null)
            {
                await newStream.WriteAsync(uuidPkt, session.Cts.Token);
            }

            if (hpPkt != null)
            {
                await newStream.WriteAsync(PrepareOutboundPacket(session, hpPkt, session.ClientSlot, targetSlot), session.Cts.Token);
            }

            if (manaPkt != null)
            {
                await newStream.WriteAsync(PrepareOutboundPacket(session, manaPkt, session.ClientSlot, targetSlot), session.Cts.Token);
            }

            if (buffPkt != null)
            {
                await newStream.WriteAsync(PrepareOutboundPacket(session, buffPkt, session.ClientSlot, targetSlot), session.Cts.Token);
            }

            foreach (var sp in slotPkts)
            {
                await newStream.WriteAsync(PrepareOutboundPacket(session, sp, session.ClientSlot, targetSlot), session.Cts.Token);
            }

            // Packet 6: RequestWorldData
            byte[] reqWorld = new byte[] { 3, 0, 6 };
            await newStream.WriteAsync(reqWorld, session.Cts.Token);
            await newStream.FlushAsync(session.Cts.Token);
        }

        private static void CacheClientPacket(ProxySession session, byte[] packet)
        {
            if (packet.Length < 3) return;
            byte msgId = packet[2];

            lock (session.CacheLock)
            {
                switch (msgId)
                {
                    case 1:
                        session.ConnectRequestPacket = (byte[])packet.Clone();
                        break;
                    case 68:
                        session.ClientUuidPacket = (byte[])packet.Clone();
                        session.ClientUuid = TryReadPacketString(packet, 3);
                        break;
                    case 4:
                        session.PlayerInfoPacket = (byte[])packet.Clone();
                        string parsedName = TryReadPlayerNameFromPacket4(packet);
                        if (!string.IsNullOrWhiteSpace(parsedName))
                        {
                            session.PlayerName = parsedName;
                        }
                        break;
                    case 16:
                        session.PlayerHpPacket = (byte[])packet.Clone();
                        break;
                    case 42:
                        session.PlayerManaPacket = (byte[])packet.Clone();
                        break;
                    case 50:
                        session.PlayerBuffPacket = (byte[])packet.Clone();
                        break;
                    case 5:
                        if (packet.Length >= 6)
                        {
                            short slot = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4, 2));
                            session.PlayerSlotPackets[slot] = (byte[])packet.Clone();
                        }
                        break;
                    case 12:
                        session.PlayerSpawnPacket = (byte[])packet.Clone();
                        break;
                    case 8:
                        if (session.RequestTilesPacket == null && packet.Length >= 11)
                        {
                            session.RequestTilesPacket = (byte[])packet.Clone();
                        }
                        break;
                }
            }
        }

        private static void CacheServerSelfPacket(ProxySession session, byte[] toClient)
        {
            if (toClient.Length < 4 || session.ClientSlot == 255) return;
            byte msgId = toClient[2];
            if (toClient[3] != session.ClientSlot) return;

            lock (session.CacheLock)
            {
                switch (msgId)
                {
                    case 4:
                        session.PlayerInfoPacket = (byte[])toClient.Clone();
                        break;
                    case 16:
                        session.PlayerHpPacket = (byte[])toClient.Clone();
                        break;
                    case 42:
                        session.PlayerManaPacket = (byte[])toClient.Clone();
                        break;
                    case 50:
                        session.PlayerBuffPacket = (byte[])toClient.Clone();
                        break;
                    case 5:
                        if (toClient.Length >= 6)
                        {
                            short slot = BinaryPrimitives.ReadInt16LittleEndian(toClient.AsSpan(4, 2));
                            session.PlayerSlotPackets[slot] = (byte[])toClient.Clone();
                        }
                        break;
                }
            }
        }

        private byte MapBackendSlotToClient(ProxySession observerSession, byte backendSlot)
        {
            if (backendSlot == 255) return 255;
            if (backendSlot == observerSession.BackendSlot) return observerSession.ClientSlot;

            var targetSession = FindSession(null, null, backendSlot, observerSession.CurrentWorldId);
            if (targetSession != null)
            {
                return targetSession.ClientSlot;
            }

            return MapSlot(backendSlot, observerSession.BackendSlot, observerSession.ClientSlot);
        }

        private void TrackBackendEntityPacket(ProxySession session, byte[] packet)
        {
            if (packet.Length < 4) return;
            byte msgId = packet[2];

            lock (session.EntityLock)
            {
                if (msgId == 14 && packet.Length >= 5) // PlayerActive
                {
                    byte pid = packet[3];
                    bool active = packet[4] != 0;
                    if (pid != session.BackendSlot)
                    {
                        byte mappedPid = MapBackendSlotToClient(session, pid);
                        if (active) session.ActiveOtherPlayers.Add(mappedPid);
                        else session.ActiveOtherPlayers.Remove(mappedPid);
                    }
                }
                else if (msgId == 27 && packet.Length >= 28) // SyncProjectile ([3..4]=id, [5..12]=pos, [13..20]=vel, [21..24]=knockBack, [25..26]=damage, [27]=owner)
                {
                    short projId = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(3, 2));
                    byte owner = MapBackendSlotToClient(session, packet[27]);
                    session.ActiveProjectiles.Add((projId, owner));
                }
                else if (msgId == 29 && packet.Length >= 6) // KillProjectile
                {
                    short projId = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(3, 2));
                    byte owner = MapBackendSlotToClient(session, packet[5]);
                    session.ActiveProjectiles.Remove((projId, owner));
                }
            }
        }

        private byte[] PrepareOutboundPacket(ProxySession session, byte[] packet, byte fromSlot, byte toSlot)
        {
            if (fromSlot == toSlot || fromSlot == 255 || toSlot == 255 || packet.Length < 4)
            {
                return packet;
            }

            byte msgId = packet[2];
            int slotOffset = msgId switch
            {
                4 or 5 or 12 or 13 or 14 or 16 or 30 or 35 or 36 or 40 or 41 or 42 or 43
                or 45 or 50 or 51 or 55 or 58 or 62 or 66 or 80 or 96 or 99 or 102 or 117
                or 118 or 120 or 121 or 124 or 125 or 128 or 134 or 135 or 139 or 147 => 3,
                65 => (packet[3] & 3) != 1 ? 4 : -1, // Teleport: flag bits 0..1 == 1 adalah NPC teleport, bukan player
                22 or 29 => 5,
                27 => 27, // SyncProjectile: owner byte ada di offset 27
                _ => -1
            };

            if (slotOffset < 0 || packet.Length <= slotOffset)
            {
                return packet;
            }

            byte current = packet[slotOffset];
            byte mapped = (fromSlot == session.BackendSlot)
                ? MapBackendSlotToClient(session, current)
                : MapSlot(current, fromSlot, toSlot);

            if (mapped == current) return packet;

            byte[] copy = (byte[])packet.Clone();
            copy[slotOffset] = mapped;
            return copy;
        }

        private static byte MapSlot(byte slot, byte a, byte b)
        {
            if (a == b || a == 255 || b == 255) return slot;
            if (slot == a) return b;
            if (slot == b) return a;
            return slot;
        }

        private static byte[] BuildRequestEssentialTilesPacket(ProxySession session, int x, int y)
        {
            byte[]? template;
            lock (session.CacheLock) template = session.RequestTilesPacket;
            if (template != null && template.Length >= 11)
            {
                byte[] clone = (byte[])template.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(clone.AsSpan(3, 4), x);
                BinaryPrimitives.WriteInt32LittleEndian(clone.AsSpan(7, 4), y);
                return clone;
            }

            byte[] pkt = new byte[11];
            pkt[0] = 11;
            pkt[1] = 0;
            pkt[2] = 8;
            BinaryPrimitives.WriteInt32LittleEndian(pkt.AsSpan(3, 4), x);
            BinaryPrimitives.WriteInt32LittleEndian(pkt.AsSpan(7, 4), y);
            return pkt;
        }

        private static string TryReadPlayerNameFromPacket4(byte[] packet)
        {
            // Packet 4: [len(2)][id=4(1)][whoAmI(1)][skinVariant(1)][hair(1)][7-bit string name] (atau offset +1/+2 jika ada field baru)
            foreach (int offset in new[] { 6, 7, 8 })
            {
                try
                {
                    if (packet.Length <= offset + 1) continue;
                    int strLen = packet[offset];
                    if (strLen <= 0 || strLen > 24 || offset + 1 + strLen > packet.Length) continue;

                    using var ms = new MemoryStream(packet, offset, packet.Length - offset);
                    using var br = new BinaryReader(ms, Encoding.UTF8);
                    string candidate = br.ReadString();
                    if (!string.IsNullOrWhiteSpace(candidate) && candidate.All(c => !char.IsControl(c)))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }
            return string.Empty;
        }

        private static string TryReadNetworkText(byte[] packet, int offset)
        {
            if (packet.Length <= offset + 1) return string.Empty;
            string afterMode = TryReadPacketString(packet, offset + 1);
            if (!string.IsNullOrWhiteSpace(afterMode)) return afterMode;
            return TryReadPacketString(packet, offset);
        }

        private static string TryReadPacketString(byte[] packet, int offset)
        {
            try
            {
                if (packet.Length <= offset) return string.Empty;
                using var ms = new MemoryStream(packet, offset, packet.Length - offset);
                using var br = new BinaryReader(ms, Encoding.UTF8);
                return br.ReadString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<byte[]?> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
        {
            byte[] lenBuf = new byte[2];
            if (!await ReadExactAsync(stream, lenBuf, 0, 2, ct)) return null;

            ushort totalLen = BinaryPrimitives.ReadUInt16LittleEndian(lenBuf);
            if (totalLen < 3) return null;

            byte[] packet = new byte[totalLen];
            packet[0] = lenBuf[0];
            packet[1] = lenBuf[1];

            if (!await ReadExactAsync(stream, packet, 2, totalLen - 2, ct)) return null;
            return packet;
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int n;
                try
                {
                    n = await stream.ReadAsync(buffer.AsMemory(offset + readTotal, count - readTotal), ct);
                }
                catch
                {
                    return false;
                }

                if (n <= 0) return false;
                readTotal += n;
            }
            return true;
        }

        private static async Task<bool> SendPacketAsync(NetworkStream stream, SemaphoreSlim writeLock, byte[] packet, CancellationToken ct)
        {
            await writeLock.WaitAsync(ct);
            try
            {
                await stream.WriteAsync(packet, ct);
                await stream.FlushAsync(ct);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                writeLock.Release();
            }
        }

        private static async Task SendRawAsync(NetworkStream stream, byte[] packet, CancellationToken ct)
        {
            await stream.WriteAsync(packet, ct);
            await stream.FlushAsync(ct);
        }

        private static async Task SendClientDisconnectAndDisposeAsync(ProxySession session, string reason)
        {
            try
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
                bw.Write((ushort)0);
                bw.Write((byte)2); // Disconnect
                bw.Write((byte)0); // NetworkText.Mode.Literal (Terraria 1.4+)
                bw.Write(reason ?? "Koneksi multiworld terputus.");
                bw.Flush();

                byte[] pkt = ms.ToArray();
                BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(0, 2), (ushort)pkt.Length);
                await SendPacketAsync(session.ClientStream, session.ClientWriteLock, pkt, CancellationToken.None);
            }
            catch { }
            finally
            {
                session.Dispose();
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
            _sessions.Clear();
            try { _cts.Dispose(); } catch { }
        }
    }
}
