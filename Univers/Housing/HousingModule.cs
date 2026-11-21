/*
 * =====================================================================
 *  HousingModule.cs - Engine RP Housing / Plot Squared untuk Terraria
 *  Namespace: Univers.Housing (.NET 9.0 / TShock 6.x)
 *  Fitur: Kavling Auto-Grid, Sign DIJUAL, Trust/Co-Owner/Member/Ban,
 *         Protection Lengkap, Visit/Lock, Likes/Top, Flags, Upgrade Tier
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Microsoft.Xna.Framework;
using Skynomi.Economy;
using Skynomi.Ranks;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using Univers;
using Univers.MultiWorld;

namespace Univers.Housing
{
    internal class AdminPlotSelection
    {
        public int? X1 { get; set; }
        public int? Y1 { get; set; }
        public int? X2 { get; set; }
        public int? Y2 { get; set; }
        public int? PendingPosClick { get; set; } // 1 or 2
        public string? PendingLinkSignCode { get; set; }
        public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(5);
    }

    internal class HousingSignConfirm
    {
        public int PlotId { get; set; }
        public bool IsResale { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public static class HousingModule
    {
        public static HousingConfig Config { get; private set; } = new();
        public static HousingDatabase Database { get; private set; } = null!;

        private static string _configPath = string.Empty;
        private static bool _initialized = false;
        private static bool _initialSyncPending = false;
        private static System.Timers.Timer? _presenceTimer;

        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private static readonly ConcurrentDictionary<int, AdminPlotSelection> _adminSelections = new();
        private static readonly ConcurrentDictionary<int, HousingSignConfirm> _signConfirms = new();
        private static readonly ConcurrentDictionary<int, int> _playerLastPlotId = new();
        private static readonly ConcurrentDictionary<int, DateTime> _lastDenyMsgUtc = new();

        public static string ActiveWorldId => MultiWorldModule.CurrentWorldId;

        public static bool IsHousingActiveInCurrentWorld()
        {
            if (!_initialized || !Config.Enabled) return false;
            return Config.IsWorldAllowed(ActiveWorldId);
        }

        public static void Initialize(string savePath)
        {
            if (_initialized) return;

            string universDir = Path.Combine(savePath, "Univers");
            string masterUniversDir = ResolveMasterUniversDir(universDir);
            Directory.CreateDirectory(masterUniversDir);

            _configPath = Path.Combine(masterUniversDir, "HousingConfig.json");
            Config = HousingConfig.Load(_configPath);
            Database = new HousingDatabase(Path.Combine(masterUniversDir, "univers_housing.db"));

            GetDataHandlers.TileEdit += OnTileEdit;
            GetDataHandlers.ChestOpen += OnChestOpen;
            GetDataHandlers.PaintTile += OnPaintTile;
            GetDataHandlers.PaintWall += OnPaintWall;
            GetDataHandlers.TogglePvp += OnTogglePvp;

            ServerApi.Hooks.NetGetData.Register(UniversPlugin.Instance, OnNetGetData);
            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.ServerLeave.Register(UniversPlugin.Instance, OnServerLeave);

            _presenceTimer = new System.Timers.Timer(600);
            _presenceTimer.Elapsed += OnPresenceTick;
            _presenceTimer.AutoReset = true;
            _presenceTimer.Start();

            _initialSyncPending = true;
            HousingCommands.Register();

            _initialized = true;
            TShock.Log.ConsoleInfo($"[Univers.Housing] Modul RP Housing aktif di world [{string.Join(", ", Config.AllowedWorldIds)}]!");
        }

        public static void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config = HousingConfig.Load(_configPath);
            }
            QueueMainThread(RefreshAllPlotSigns);
            TShock.Log.ConsoleInfo("[Univers.Housing] Konfigurasi HousingConfig.json dimuat ulang.");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;

            try { _presenceTimer?.Stop(); _presenceTimer?.Dispose(); } catch { }

            GetDataHandlers.TileEdit -= OnTileEdit;
            GetDataHandlers.ChestOpen -= OnChestOpen;
            GetDataHandlers.PaintTile -= OnPaintTile;
            GetDataHandlers.PaintWall -= OnPaintWall;
            GetDataHandlers.TogglePvp -= OnTogglePvp;

            ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnNetGetData);
            ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate);
            ServerApi.Hooks.ServerLeave.Deregister(UniversPlugin.Instance, OnServerLeave);

            _adminSelections.Clear();
            _signConfirms.Clear();
            _playerLastPlotId.Clear();
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

        public static void QueueMainThread(Action action)
        {
            if (action != null) _mainThreadQueue.Enqueue(action);
        }

        private static void OnGameUpdate(EventArgs args)
        {
            if (_initialSyncPending)
            {
                _initialSyncPending = false;
                if (IsHousingActiveInCurrentWorld())
                {
                    RefreshAllPlotSigns();
                }
            }

            int count = 0;
            while (count < 24 && _mainThreadQueue.TryDequeue(out var act))
            {
                count++;
                try { act(); } catch { }
            }
        }

        private static void OnServerLeave(LeaveEventArgs args)
        {
            _adminSelections.TryRemove(args.Who, out _);
            _signConfirms.TryRemove(args.Who, out _);
            _playerLastPlotId.TryRemove(args.Who, out _);
            _lastDenyMsgUtc.TryRemove(args.Who, out _);
        }

        #region Protection & Interaction Hooks

        private static void OnTileEdit(object? sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (e.Handled || e.Player == null || !IsHousingActiveInCurrentWorld()) return;

            // Cek mode seleksi admin pos1 / pos2
            if (_adminSelections.TryGetValue(e.Player.Index, out var sel) &&
                sel.PendingPosClick.HasValue &&
                DateTime.UtcNow <= sel.ExpiresAtUtc &&
                e.Player.HasPermission("univers.house.admin"))
            {
                int clickNum = sel.PendingPosClick.Value;
                sel.PendingPosClick = null;
                if (clickNum == 1)
                {
                    sel.X1 = e.X;
                    sel.Y1 = e.Y;
                    e.Player.SendSuccessMessage($"[HOUSING ADMIN] Sudut Pos 1 diset ke ({e.X}, {e.Y}).");
                }
                else
                {
                    sel.X2 = e.X;
                    sel.Y2 = e.Y;
                    e.Player.SendSuccessMessage($"[HOUSING ADMIN] Sudut Pos 2 diset ke ({e.X}, {e.Y}).");
                }
                e.Handled = true;
                e.Player.SendTileSquareCentered(e.X, e.Y, 4);
                return;
            }

            var plot = Database.GetPlotAt(ActiveWorldId, e.X, e.Y);
            if (plot == null) return;

            // Lindungi Sign properti agar tidak hancur kecuali admin
            if (plot.HasSign && e.X >= plot.SignX && e.X <= plot.SignX + 1 && e.Y >= plot.SignY && e.Y <= plot.SignY + 2)
            {
                if (!e.Player.HasPermission("univers.house.admin"))
                {
                    e.Handled = true;
                    e.Player.SendTileSquareCentered(e.X, e.Y, 4);
                    SendThrottledDeny(e.Player, "[HOUSING] Sign Properti dilindungi server!");
                    return;
                }
            }

            if (e.Player.HasPermission("univers.house.admin")) return;

            string accName = e.Player.Account?.Name ?? string.Empty;
            if (!e.Player.IsLoggedIn || !plot.CanBuild(accName))
            {
                e.Handled = true;
                e.Player.SendTileSquareCentered(e.X, e.Y, 4);
                string ownerText = plot.IsOwned ? $"milik {plot.OwnerDisplayName}" : "kavling DIJUAL (/house claim)";
                SendThrottledDeny(e.Player, $"[HOUSING] Kamu tidak punya izin membangun di {plot.EffectiveTitle} ({ownerText})!");
            }
        }

        private static void OnChestOpen(object? sender, GetDataHandlers.ChestOpenEventArgs e)
        {
            if (e.Handled || e.Player == null || !IsHousingActiveInCurrentWorld()) return;

            var plot = Database.GetPlotAt(ActiveWorldId, e.X, e.Y);
            if (plot == null || e.Player.HasPermission("univers.house.admin")) return;

            string accName = e.Player.Account?.Name ?? string.Empty;
            if (!e.Player.IsLoggedIn || !plot.CanOpenChests(accName))
            {
                e.Handled = true;
                SendThrottledDeny(e.Player, $"[HOUSING] Chest di dalam '{plot.EffectiveTitle}' terkunci! Hanya Owner / Co-Owner yang bisa membuka.");
            }
        }

        private static void OnPaintTile(object? sender, GetDataHandlers.PaintTileEventArgs e)
        {
            if (e.Handled || e.Player == null || !IsHousingActiveInCurrentWorld()) return;
            var plot = Database.GetPlotAt(ActiveWorldId, e.X, e.Y);
            if (plot == null || e.Player.HasPermission("univers.house.admin")) return;

            string accName = e.Player.Account?.Name ?? string.Empty;
            if (!e.Player.IsLoggedIn || !plot.CanBuild(accName))
            {
                e.Handled = true;
                e.Player.SendTileSquareCentered(e.X, e.Y, 3);
                SendThrottledDeny(e.Player, "[HOUSING] Kamu tidak punya izin mengecat di properti ini!");
            }
        }

        private static void OnPaintWall(object? sender, GetDataHandlers.PaintWallEventArgs e)
        {
            if (e.Handled || e.Player == null || !IsHousingActiveInCurrentWorld()) return;
            var plot = Database.GetPlotAt(ActiveWorldId, e.X, e.Y);
            if (plot == null || e.Player.HasPermission("univers.house.admin")) return;

            string accName = e.Player.Account?.Name ?? string.Empty;
            if (!e.Player.IsLoggedIn || !plot.CanBuild(accName))
            {
                e.Handled = true;
                e.Player.SendTileSquareCentered(e.X, e.Y, 3);
                SendThrottledDeny(e.Player, "[HOUSING] Kamu tidak punya izin mengecat dinding properti ini!");
            }
        }

        private static void OnTogglePvp(object? sender, GetDataHandlers.TogglePvpEventArgs e)
        {
            if (e.Handled || e.Player == null || !IsHousingActiveInCurrentWorld() || !e.Pvp) return;

            var plot = Database.GetPlotAt(ActiveWorldId, e.Player.TileX, e.Player.TileY);
            if (plot != null && !plot.FlagAllowPvp && !e.Player.HasPermission("univers.house.admin"))
            {
                e.Handled = true;
                e.Player.TPlayer.hostile = false;
                e.Player.SendData(PacketTypes.TogglePvp, "", e.Player.Index);
                SendThrottledDeny(e.Player, $"[HOUSING] PvP dinonaktifkan di area '{plot.EffectiveTitle}'!");
            }
        }

        private static void OnNetGetData(GetDataEventArgs e)
        {
            if (e.Handled || !IsHousingActiveInCurrentWorld()) return;

            int msgId = (int)e.MsgID;
            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null || !player.Active) return;

            if (msgId == MessageID.SignRead)
            {
                if (e.Length < 4) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                short rawX = reader.ReadInt16();
                short rawY = reader.ReadInt16();

                if (!TryGetSignTopLeft(rawX, rawY, out int sx, out int sy, out int signIdx)) return;

                // Cek mode admin linksign
                if (_adminSelections.TryGetValue(player.Index, out var sel) &&
                    !string.IsNullOrWhiteSpace(sel.PendingLinkSignCode) &&
                    DateTime.UtcNow <= sel.ExpiresAtUtc &&
                    player.HasPermission("univers.house.admin"))
                {
                    e.Handled = true;
                    string code = sel.PendingLinkSignCode;
                    sel.PendingLinkSignCode = null;
                    var targetPlot = Database.GetPlotByCode(code);
                    if (targetPlot == null)
                    {
                        player.SendErrorMessage($"[HOUSING ADMIN] Kavling '{code}' tidak ditemukan.");
                        return;
                    }
                    targetPlot.HasSign = true;
                    targetPlot.SignX = sx;
                    targetPlot.SignY = sy;
                    Database.SavePlot(targetPlot);
                    RefreshPlotSign(targetPlot);
                    player.SendSuccessMessage($"[HOUSING ADMIN] Sign di ({sx},{sy}) berhasil dihubungkan ke kavling '{targetPlot.Code}'!");
                    return;
                }

                var signPlot = Database.GetPlotBySign(ActiveWorldId, sx, sy);
                if (signPlot != null)
                {
                    e.Handled = true;
                    HandleHousingSignClick(player, signPlot);
                    return;
                }

                // Sign biasa di dalam plot: boleh dibaca jika CanInteract
                var insidePlot = Database.GetPlotAt(ActiveWorldId, sx, sy);
                if (insidePlot != null && !player.HasPermission("univers.house.admin"))
                {
                    string acc = player.Account?.Name ?? "";
                    if (!insidePlot.CanInteract(acc))
                    {
                        e.Handled = true;
                        SendThrottledDeny(player, "[HOUSING] Pemilik mengunci interaksi di rumah ini.");
                    }
                }
                return;
            }

            if (msgId == MessageID.SignNew)
            {
                if (e.Length < 6) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                reader.ReadInt16();
                short rawX = reader.ReadInt16();
                short rawY = reader.ReadInt16();

                if (!TryGetSignTopLeft(rawX, rawY, out int sx, out int sy, out _)) return;

                var signPlot = Database.GetPlotBySign(ActiveWorldId, sx, sy);
                if (signPlot != null)
                {
                    e.Handled = true;
                    RefreshPlotSign(signPlot);
                    player.SendErrorMessage("[HOUSING] Teks Sign Properti dikelola otomatis oleh sistem!");
                    return;
                }

                var insidePlot = Database.GetPlotAt(ActiveWorldId, sx, sy);
                if (insidePlot != null && !player.HasPermission("univers.house.admin"))
                {
                    string acc = player.Account?.Name ?? "";
                    if (!player.IsLoggedIn || !insidePlot.CanBuild(acc))
                    {
                        e.Handled = true;
                        SendThrottledDeny(player, "[HOUSING] Kamu tidak boleh mengubah isi Sign di properti orang lain!");
                    }
                }
                return;
            }

            if (msgId == MessageID.LiquidSet || msgId == MessageID.PlaceObject || msgId == MessageID.PlaceTileEntity)
            {
                if (e.Length < 4 || player.HasPermission("univers.house.admin")) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                short tx = reader.ReadInt16();
                short ty = reader.ReadInt16();

                var plot = Database.GetPlotAt(ActiveWorldId, tx, ty);
                if (plot != null)
                {
                    string acc = player.Account?.Name ?? "";
                    if (!player.IsLoggedIn || !plot.CanBuild(acc))
                    {
                        e.Handled = true;
                        player.SendTileSquareCentered(tx, ty, 4);
                        SendThrottledDeny(player, "[HOUSING] Area properti dilindungi!");
                    }
                }
                return;
            }

            if (msgId == MessageID.ChestUnlock)
            {
                if (e.Length < 5 || player.HasPermission("univers.house.admin")) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                reader.ReadByte();
                short tx = reader.ReadInt16();
                short ty = reader.ReadInt16();

                var plot = Database.GetPlotAt(ActiveWorldId, tx, ty);
                if (plot != null)
                {
                    string acc = player.Account?.Name ?? "";
                    if (!player.IsLoggedIn || !plot.CanOpenChests(acc))
                    {
                        e.Handled = true;
                        SendThrottledDeny(player, "[HOUSING] Kamu tidak boleh membuka kunci chest di properti ini!");
                    }
                }
                return;
            }

            if (msgId == MessageID.ToggleDoorState || msgId == MessageID.HitSwitch)
            {
                if (e.Length < 4 || player.HasPermission("univers.house.admin")) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                if (msgId == MessageID.ToggleDoorState) reader.ReadByte();
                short tx = reader.ReadInt16();
                short ty = reader.ReadInt16();

                var plot = Database.GetPlotAt(ActiveWorldId, tx, ty);
                if (plot != null)
                {
                    string acc = player.Account?.Name ?? "";
                    if (!plot.CanInteract(acc))
                    {
                        e.Handled = true;
                        player.SendTileSquareCentered(tx, ty, 3);
                        SendThrottledDeny(player, "[HOUSING] Owner menonaktifkan interaksi tamu di properti ini!");
                    }
                }
            }
        }

        private static void HandleHousingSignClick(TSPlayer player, HousingPlot plot)
        {
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("[HOUSING] Silakan /login terlebih dahulu.");
                return;
            }

            string accName = player.Account.Name;

            if (!plot.IsOwned)
            {
                // Beli kavling kosong dengan 2x klik konfirmasi
                if (_signConfirms.TryGetValue(player.Index, out var pending) &&
                    pending.PlotId == plot.Id &&
                    !pending.IsResale &&
                    DateTime.UtcNow <= pending.ExpiresAtUtc)
                {
                    _signConfirms.TryRemove(player.Index, out _);
                    var (ok, msg) = TryClaimPlot(player, plot);
                    if (ok) player.SendSuccessMessage("[HOUSING] " + msg);
                    else player.SendErrorMessage("[HOUSING] " + msg);
                    return;
                }

                _signConfirms[player.Index] = new HousingSignConfirm
                {
                    PlotId = plot.Id,
                    IsResale = false,
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Config.SignConfirmTimeoutSeconds)
                };

                player.SendMessage($"[HOUSING] Kavling '{plot.Code}' ({plot.DistrictName}) tersedia seharga {EconomyApi.FormatColored(plot.PriceShards)}!", Color.Gold);
                player.SendInfoMessage($"Klik Sign lagi dalam {Config.SignConfirmTimeoutSeconds} detik (atau ketik /house claim) untuk MEMBELI.");
                return;
            }

            if (plot.ForSaleByOwnerPrice > 0 && !plot.IsOwner(accName))
            {
                if (_signConfirms.TryGetValue(player.Index, out var pending) &&
                    pending.PlotId == plot.Id &&
                    pending.IsResale &&
                    DateTime.UtcNow <= pending.ExpiresAtUtc)
                {
                    _signConfirms.TryRemove(player.Index, out _);
                    var (ok, msg) = TryBuyResalePlot(player, plot);
                    if (ok) player.SendSuccessMessage("[HOUSING] " + msg);
                    else player.SendErrorMessage("[HOUSING] " + msg);
                    return;
                }

                _signConfirms[player.Index] = new HousingSignConfirm
                {
                    PlotId = plot.Id,
                    IsResale = true,
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Config.SignConfirmTimeoutSeconds)
                };

                player.SendMessage($"[HOUSING RESALE] Rumah '{plot.EffectiveTitle}' dijual oleh {plot.OwnerDisplayName} seharga {EconomyApi.FormatColored(plot.ForSaleByOwnerPrice)}!", Color.Gold);
                player.SendInfoMessage($"Klik Sign lagi dalam {Config.SignConfirmTimeoutSeconds} detik untuk MEMBELI properti ini.");
                return;
            }

            // Tampilkan kartu RP rumah
            var tier = Config.GetTier(plot.Level);
            string status = plot.IsPublicVisit ? "OPEN (Public)" : "LOCKED (Private)";
            player.SendMessage($"=== [{plot.EffectiveTitle}] ({plot.Code}) ===", Color.Cyan);
            player.SendInfoMessage($"Pemilik: {plot.OwnerDisplayName} | Tier Lv.{plot.Level} ({tier.Title}) | Status: {status}");
            if (!string.IsNullOrWhiteSpace(plot.Description))
            {
                player.SendInfoMessage($"Bio RP: {plot.Description}");
            }
            player.SendInfoMessage($"Likes: {plot.LikesCount} | Kunjungan: {plot.VisitsCount} | Ketik /house like jika suka rumah ini!");
        }

        #endregion

        #region Real-Time Presence, Lock Barrier, Ban Kick & Cozy Buff

        private static void OnPresenceTick(object? sender, ElapsedEventArgs e)
        {
            if (!IsHousingActiveInCurrentWorld()) return;

            var allPlots = Database.GetAllPlots(ActiveWorldId);
            if (allPlots.Count == 0) return;

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active || !player.RealPlayer) continue;

                int tx = player.TileX;
                int ty = player.TileY;
                var currentPlot = allPlots.FirstOrDefault(p => p.ContainsTile(tx, ty));
                int newPlotId = currentPlot?.Id ?? 0;
                _playerLastPlotId.TryGetValue(player.Index, out int prevPlotId);

                string accName = player.IsLoggedIn ? player.Account.Name : string.Empty;

                if (currentPlot != null)
                {
                    bool isAdmin = player.HasPermission("univers.house.admin");

                    // 1. Cek Ban atau Rumah Terkunci
                    if (!isAdmin && !currentPlot.CanEnter(accName))
                    {
                        HousingPlot bannedOrLocked = currentPlot;
                        QueueMainThread(() => EjectPlayerOutsidePlot(player, bannedOrLocked));
                        continue;
                    }

                    // 2. Matikan PvP jika FlagAllowPvp == false
                    if (!currentPlot.FlagAllowPvp && player.TPlayer.hostile && !isAdmin)
                    {
                        QueueMainThread(() =>
                        {
                            if (player.Active)
                            {
                                player.TPlayer.hostile = false;
                                player.SendData(PacketTypes.TogglePvp, "", player.Index);
                                SendThrottledDeny(player, $"[HOUSING] PvP otomatis dimatikan di '{currentPlot.EffectiveTitle}'.");
                            }
                        });
                    }

                    // 3. Cozy Buff di dalam rumah jika dimiliki
                    if (Config.EnableCozyBuff && currentPlot.IsOwned && currentPlot.FlagCozyRegen &&
                        (currentPlot.IsOwner(accName) || currentPlot.IsCoOwner(accName) || currentPlot.IsMember(accName)))
                    {
                        QueueMainThread(() =>
                        {
                            if (player.Active && !player.Dead)
                            {
                                player.SetBuff(BuffID.Campfire, 180);
                            }
                        });
                    }
                }

                if (newPlotId != prevPlotId)
                {
                    _playerLastPlotId[player.Index] = newPlotId;
                    if (currentPlot != null)
                    {
                        if (currentPlot.IsOwned && !currentPlot.IsOwner(accName))
                        {
                            currentPlot.VisitsCount++;
                            Database.SavePlot(currentPlot);
                        }

                        var tier = Config.GetTier(currentPlot.Level);
                        if (currentPlot.IsOwned)
                        {
                            string bio = string.IsNullOrWhiteSpace(currentPlot.Description) ? "" : $" - {currentPlot.Description}";
                            player.SendMessage($"[ ~ {currentPlot.EffectiveTitle} ~ ] {tier.Title} milik {currentPlot.OwnerDisplayName} | Likes: {currentPlot.LikesCount}{bio}", Color.Aquamarine);
                        }
                        else
                        {
                            player.SendMessage($"[ Kavling {currentPlot.Code} DIJUAL ] Harga: {EconomyApi.FormatColored(currentPlot.PriceShards)} | Ketik /house claim untuk beli!", Color.Gold);
                        }
                    }
                }
            }
        }

        public static void EjectPlayerOutsidePlot(TSPlayer player, HousingPlot plot)
        {
            if (player == null || !player.Active) return;

            int distLeft = Math.Abs(player.TileX - plot.MinX);
            int distRight = Math.Abs(plot.MaxX - player.TileX);
            int safeX = distLeft <= distRight ? Math.Max(5, plot.MinX - 4) : Math.Min(Main.maxTilesX - 6, plot.MaxX + 4);
            int safeY = plot.SpawnY > 0 ? plot.SpawnY : plot.MaxY - 3;

            player.Teleport(safeX * 16f, safeY * 16f);
            string reason = plot.IsBanned(player.Account?.Name ?? "")
                ? $"Kamu diblokir dari '{plot.EffectiveTitle}'!"
                : $"Rumah '{plot.EffectiveTitle}' sedang DIKUNCI (Private)!";
            SendThrottledDeny(player, $"[HOUSING] {reason}");
        }

        #endregion

        #region Core Business API (Claim, Auto, Grid, Upgrade, Resale, Abandon)

        public static (bool Ok, string Message) TryClaimPlot(TSPlayer player, HousingPlot plot)
        {
            if (!player.IsLoggedIn) return (false, "Silakan /login terlebih dahulu.");
            if (plot.IsOwned) return (false, $"Kavling '{plot.Code}' sudah dimiliki oleh {plot.OwnerDisplayName}.");

            string accName = player.Account.Name;
            int rankIndex = 0;
            try
            {
                rankIndex = RankModule.Database?.GetOrCreate(accName)?.RankIndex ?? 0;
            }
            catch { }

            int maxAllowed = Config.GetMaxHousesForRank(rankIndex);
            var owned = Database.GetOwnedPlots(accName);
            if (!player.HasPermission("univers.house.admin") && owned.Count >= maxAllowed)
            {
                return (false, $"Batas rumah untuk rank kamu saat ini adalah {maxAllowed} kavling! Naikkan /rankup untuk slot rumah lebih banyak.");
            }

            if (plot.PriceShards > 0 && !EconomyApi.TryDebit(accName, plot.PriceShards, $"BuyHouse:{plot.Code}"))
            {
                long bal = EconomyApi.GetBalance(accName);
                return (false, $"Shards tidak cukup! Harga: {EconomyApi.Format(plot.PriceShards)}, Saldo kamu: {EconomyApi.Format(bal)}.");
            }

            plot.OwnerAccount = accName;
            plot.OwnerDisplayName = player.Name;
            plot.CustomName = $"Rumah {player.Name}";
            plot.Description = "Selamat datang di rumahku!";
            plot.Level = 1;
            plot.CoOwners.Clear();
            plot.Members.Clear();
            plot.BannedPlayers.Clear();
            plot.LikedBy.Clear();
            plot.LikesCount = 0;
            plot.VisitsCount = 0;
            plot.IsPublicVisit = true;
            plot.ForSaleByOwnerPrice = 0;
            plot.PurchasedAtUtc = DateTime.UtcNow;

            Database.SavePlot(plot);
            QueueMainThread(() => RefreshPlotSign(plot));

            return (true, $"Selamat! Kavling '{plot.Code}' resmi menjadi milikmu ({EconomyApi.Format(plot.PriceShards)}). Ketik /house border untuk lihat batas tanah!");
        }

        public static (bool Ok, string Message) TryBuyResalePlot(TSPlayer buyer, HousingPlot plot)
        {
            if (!buyer.IsLoggedIn) return (false, "Silakan /login terlebih dahulu.");
            if (!plot.IsOwned || plot.ForSaleByOwnerPrice <= 0) return (false, "Properti ini tidak sedang dijual.");

            string buyerAcc = buyer.Account.Name;
            if (plot.IsOwner(buyerAcc)) return (false, "Ini sudah rumah milikmu sendiri!");

            int rankIndex = 0;
            try { rankIndex = RankModule.Database?.GetOrCreate(buyerAcc)?.RankIndex ?? 0; } catch { }
            int maxAllowed = Config.GetMaxHousesForRank(rankIndex);
            if (!buyer.HasPermission("univers.house.admin") && Database.GetOwnedPlots(buyerAcc).Count >= maxAllowed)
            {
                return (false, $"Slot rumahmu sudah penuh ({maxAllowed} kavling).");
            }

            long price = plot.ForSaleByOwnerPrice;
            if (!EconomyApi.TryDebit(buyerAcc, price, $"BuyResaleHouse:{plot.Code}"))
            {
                return (false, $"Shards tidak cukup! Harga: {EconomyApi.Format(price)}.");
            }

            long tax = (long)Math.Floor(price * (Config.ResaleTaxPercent / 100.0));
            long sellerNet = Math.Max(0, price - tax);
            string prevOwnerAcc = plot.OwnerAccount;
            string prevOwnerName = plot.OwnerDisplayName;

            EconomyApi.TryCredit(prevOwnerAcc, sellerNet, $"SellHouse:{plot.Code}");
            var sellerOnline = TShock.Players.FirstOrDefault(p => p != null && p.Active && p.IsLoggedIn && p.Account.Name.Equals(prevOwnerAcc, StringComparison.OrdinalIgnoreCase));
            sellerOnline?.SendSuccessMessage($"[HOUSING] Rumah '{plot.EffectiveTitle}' ({plot.Code}) telah dibeli oleh {buyer.Name}! (+{EconomyApi.Format(sellerNet)})");

            plot.OwnerAccount = buyerAcc;
            plot.OwnerDisplayName = buyer.Name;
            plot.CustomName = $"Rumah {buyer.Name}";
            plot.ForSaleByOwnerPrice = 0;
            plot.CoOwners.Clear();
            plot.Members.Clear();
            plot.BannedPlayers.Clear();
            plot.PurchasedAtUtc = DateTime.UtcNow;

            Database.SavePlot(plot);
            QueueMainThread(() => RefreshPlotSign(plot));

            return (true, $"Berhasil membeli properti '{plot.Code}' dari {prevOwnerName} seharga {EconomyApi.Format(price)}!");
        }

        public static (bool Ok, string Message) TryAbandonPlot(TSPlayer player, HousingPlot plot)
        {
            if (!plot.IsOwner(player.Account.Name) && !player.HasPermission("univers.house.admin"))
            {
                return (false, "Hanya pemilik rumah yang bisa melepas properti ini.");
            }

            long refund = (long)Math.Floor(plot.PriceShards * (Config.AbandonRefundPercent / 100.0));
            if (refund > 0 && plot.IsOwned)
            {
                EconomyApi.TryCredit(plot.OwnerAccount, refund, $"AbandonHouse:{plot.Code}");
            }

            ResetPlotToForSale(plot);
            return (true, $"Kavling '{plot.Code}' dilepas kembali ke pasar properti (Refund: {EconomyApi.Format(refund)}).");
        }

        public static void ResetPlotToForSale(HousingPlot plot)
        {
            plot.OwnerAccount = string.Empty;
            plot.OwnerDisplayName = string.Empty;
            plot.CustomName = string.Empty;
            plot.Description = string.Empty;
            plot.ForSaleByOwnerPrice = 0;
            plot.Level = 1;
            plot.CoOwners.Clear();
            plot.Members.Clear();
            plot.BannedPlayers.Clear();
            plot.LikedBy.Clear();
            plot.LikesCount = 0;
            plot.VisitsCount = 0;
            plot.IsPublicVisit = true;
            plot.FlagAllowGuestInteract = true;
            plot.FlagAllowGuestBuild = false;
            plot.FlagAllowGuestChests = false;
            plot.FlagAllowPvp = false;
            Database.SavePlot(plot);
            QueueMainThread(() => RefreshPlotSign(plot));
        }

        public static (bool Ok, string Message) TryUpgradePlot(TSPlayer player, HousingPlot plot)
        {
            if (!plot.IsOwner(player.Account.Name)) return (false, "Hanya Owner rumah yang bisa upgrade.");

            int nextLevel = plot.Level + 1;
            var nextTier = Config.HouseTiers.FirstOrDefault(t => t.Level == nextLevel);
            if (nextTier == null)
            {
                return (false, $"Rumahmu sudah mencapai Tier Maksimal (Lv.{plot.Level})!");
            }

            if (nextTier.UpgradeCostShards > 0 && !EconomyApi.TryDebit(player.Account.Name, nextTier.UpgradeCostShards, $"UpgradeHouse:{plot.Code}"))
            {
                return (false, $"Butuh {EconomyApi.Format(nextTier.UpgradeCostShards)} untuk upgrade ke '{nextTier.Title}' (Lv.{nextTier.Level}).");
            }

            plot.Level = nextTier.Level;
            Database.SavePlot(plot);
            QueueMainThread(() => RefreshPlotSign(plot));

            return (true, $"Rumah '{plot.EffectiveTitle}' naik ke Lv.{nextTier.Level} ({nextTier.Title})! Batas Co-Owner: {nextTier.MaxCoOwners}, Member: {nextTier.MaxMembers}.");
        }

        public static int BuildAutoPlotGrid(TSPlayer admin, string district, int count, long price, int width, int height)
        {
            int startX = admin.TileX + 2;
            int floorY = admin.TileY + 3;
            int created = 0;
            int existingCount = Database.GetAllPlots(ActiveWorldId).Count;

            for (int i = 0; i < count; i++)
            {
                int minX = startX + i * (width + Config.DefaultGridSpacing);
                int maxX = minX + width - 1;
                int maxY = floorY;
                int minY = Math.Max(10, floorY - height);

                if (maxX >= Main.maxTilesX - 15) break;

                // Buat pondasi lantai & tiang batas kiri/kanan + Sign DIJUAL di pintu depan
                for (int x = minX; x <= maxX; x++)
                {
                    WorldGen.PlaceTile(x, maxY, TileID.GrayBrick, true, true);
                    // Kosongkan 4 blok di atas lantai agar siap dibangun
                    for (int clearY = maxY - 4; clearY < maxY; clearY++)
                    {
                        WorldGen.KillTile(x, clearY, false, false, true);
                    }
                }

                // Pilar penanda batas kiri dan kanan (3 blok tinggi)
                for (int py = maxY - 3; py < maxY; py++)
                {
                    WorldGen.PlaceTile(minX, py, TileID.StoneSlab, true, true);
                    WorldGen.PlaceTile(maxX, py, TileID.StoneSlab, true, true);
                }

                // Pasang Sign DIJUAL di depan kiri kavling
                int signTileX = minX + 2;
                int signTileY = maxY - 1;
                WorldGen.PlaceSign(signTileX, signTileY, TileID.Signs);
                int signIdx = Sign.ReadSign(signTileX, signTileY, true);

                string code = $"{district.ToLowerInvariant()}-{existingCount + i + 1:00}";
                while (Database.GetPlotByCode(code) != null)
                {
                    existingCount++;
                    code = $"{district.ToLowerInvariant()}-{existingCount + i + 1:00}";
                }

                var plot = new HousingPlot
                {
                    Code = code,
                    WorldId = ActiveWorldId,
                    DistrictName = district,
                    MinX = minX,
                    MinY = minY,
                    MaxX = maxX,
                    MaxY = maxY,
                    SpawnX = minX + (width / 2),
                    SpawnY = maxY - 3,
                    PriceShards = price,
                    HasSign = signIdx >= 0 && Main.sign[signIdx] != null,
                    SignX = signIdx >= 0 && Main.sign[signIdx] != null ? Main.sign[signIdx].x : signTileX,
                    SignY = signIdx >= 0 && Main.sign[signIdx] != null ? Main.sign[signIdx].y : signTileY - 1,
                    CreatedAtUtc = DateTime.UtcNow
                };

                Database.SavePlot(plot);
                RefreshPlotSign(plot);

                NetMessage.SendTileSquare(-1, minX, maxY - 4, width + 2, 6);
                created++;
            }

            return created;
        }

        public static void RefreshAllPlotSigns()
        {
            foreach (var p in Database.GetAllPlots(ActiveWorldId))
            {
                RefreshPlotSign(p);
            }
        }

        public static void RefreshPlotSign(HousingPlot plot)
        {
            if (!plot.HasSign) return;
            int signIdx = Sign.ReadSign(plot.SignX, plot.SignY, false);
            if (signIdx < 0 || Main.sign[signIdx] == null) return;

            string text;
            if (!plot.IsOwned)
            {
                text = $"[KAVLING DIJUAL]\nKode: {plot.Code}\nZona: {plot.DistrictName}\nHarga: {EconomyApi.Format(plot.PriceShards)}\nKlik Sign 2x = Beli!";
            }
            else if (plot.ForSaleByOwnerPrice > 0)
            {
                text = $"[DIJUAL PEMILIK]\n{plot.EffectiveTitle}\nPemilik: {plot.OwnerDisplayName}\nHarga: {EconomyApi.Format(plot.ForSaleByOwnerPrice)}\nKlik Sign 2x = Beli!";
            }
            else
            {
                string lockStr = plot.IsPublicVisit ? "OPEN" : "LOCKED";
                text = $"[{plot.EffectiveTitle}]\nMilik: {plot.OwnerDisplayName}\nTier Lv.{plot.Level} | {lockStr}\nLikes: {plot.LikesCount} | ({plot.Code})";
            }

            Sign.TextSign(signIdx, text);
        }

        public static void ShowPlotBorderMarkers(TSPlayer player, HousingPlot plot)
        {
            int[] xs = { plot.MinX, plot.MaxX, plot.MinX, plot.MaxX };
            int[] ys = { plot.MinY, plot.MinY, plot.MaxY, plot.MaxY };
            string[] labels = { "[BATAS KIRI-ATAS]", "[BATAS KANAN-ATAS]", "[BATAS KIRI-BAWAH]", "[BATAS KANAN-BAWAH]" };

            for (int i = 0; i < 4; i++)
            {
                player.SendData(PacketTypes.CreateCombatTextExtended, labels[i], (int)Color.Gold.PackedValue, xs[i] * 16f, ys[i] * 16f);
            }

            // Tambahan marker garis atas/bawah di dekat ketinggian player
            player.SendData(PacketTypes.CreateCombatTextExtended, "|< BATAS KIRI", (int)Color.Cyan.PackedValue, plot.MinX * 16f, player.Y - 24f);
            player.SendData(PacketTypes.CreateCombatTextExtended, "BATAS KANAN >|", (int)Color.Cyan.PackedValue, plot.MaxX * 16f, player.Y - 24f);
        }

        internal static AdminPlotSelection GetOrCreateSelection(TSPlayer player)
        {
            return _adminSelections.GetOrAdd(player.Index, _ => new AdminPlotSelection());
        }

        private static bool TryGetSignTopLeft(int rawX, int rawY, out int topX, out int topY, out int signIdx)
        {
            topX = 0;
            topY = 0;
            signIdx = -1;
            if (rawX < 0 || rawY < 0 || rawX >= Main.maxTilesX || rawY >= Main.maxTilesY) return false;
            signIdx = Sign.ReadSign(rawX, rawY, false);
            if (signIdx < 0 || Main.sign[signIdx] == null) return false;
            topX = Main.sign[signIdx].x;
            topY = Main.sign[signIdx].y;
            return true;
        }

        private static void SendThrottledDeny(TSPlayer player, string message)
        {
            DateTime now = DateTime.UtcNow;
            if (_lastDenyMsgUtc.TryGetValue(player.Index, out var prev) && (now - prev).TotalSeconds < 2.0)
            {
                return;
            }
            _lastDenyMsgUtc[player.Index] = now;
            player.SendErrorMessage(message);
        }

        #endregion
    }
}
