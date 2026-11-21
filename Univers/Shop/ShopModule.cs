/*
 * =====================================================================
 *  ShopModule.cs - Engine Fisik Showcase Item Frame + Sign & Timer AH
 *  Namespace: Univers.Shop (.NET 9.0 / TShock 6.x)
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
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using Univers;

namespace Univers.Shop
{
    internal class AdminBindSession
    {
        public bool IsRemoveMode { get; set; }
        public PhysicalDisplayType DisplayType { get; set; }
        public int ItemId { get; set; }
        public byte Prefix { get; set; }
        public long UnitPrice { get; set; }
        public int Quantity { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    internal class PlayerBoothPriceSession
    {
        public int BoothDisplayId { get; set; }
        public int SlotIndex { get; set; }
        public int ItemId { get; set; }
        public int Stack { get; set; }
        public byte Prefix { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    internal class SignConfirmSession
    {
        public int DisplayId { get; set; }
        public int ContextListingId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public static class ShopModule
    {
        public static ShopConfig Config { get; private set; } = new();
        public static ShopDatabase Database { get; private set; } = null!;

        private static string _configPath = string.Empty;
        private static System.Timers.Timer? _expiryTimer;
        private static bool _initialSyncPending = false;

        private static readonly ConcurrentQueue<Action> _mainThreadActions = new();
        private static readonly ConcurrentDictionary<int, AdminBindSession> _adminBinds = new();
        private static readonly ConcurrentDictionary<int, PlayerBoothPriceSession> _boothPricePrompts = new();
        private static readonly ConcurrentDictionary<int, SignConfirmSession> _signConfirms = new();

        public static void Initialize(string savePath)
        {
            string dir = Path.Combine(savePath, "Univers");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "ShopConfig.json");

            Config = ShopConfig.Load(_configPath);
            Database = new ShopDatabase(Path.Combine(dir, "univers_shop.db"));

            ServerApi.Hooks.NetGetData.Register(UniversPlugin.Instance, OnNetGetData);
            GetDataHandlers.TileEdit += OnTileEdit;
            ServerApi.Hooks.ServerChat.Register(UniversPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Register(UniversPlugin.Instance, OnNetGreetPlayer);
            ServerApi.Hooks.GameUpdate.Register(UniversPlugin.Instance, OnGameUpdate);

            _expiryTimer = new System.Timers.Timer(60000);
            _expiryTimer.Elapsed += OnExpiryTimerElapsed;
            _expiryTimer.AutoReset = true;
            _expiryTimer.Start();

            _initialSyncPending = true;
            ShopCommands.Register();
            TShock.Log.ConsoleInfo("[Univers.Shop] Hybrid Visual Showcase + Offline Auction House v1.0 aktif!");
        }

        public static void Reload()
        {
            if (!string.IsNullOrEmpty(_configPath))
            {
                Config = ShopConfig.Load(_configPath);
            }
            QueueMainThreadAction(RefreshAllPhysicalVisuals);
            TShock.Log.ConsoleInfo("[Univers.Shop] Konfigurasi ShopConfig.json dimuat ulang.");
        }

        public static void Dispose()
        {
            try
            {
                if (_expiryTimer != null)
                {
                    _expiryTimer.Stop();
                    _expiryTimer.Dispose();
                    _expiryTimer = null;
                }
            }
            catch { }

            ServerApi.Hooks.NetGetData.Deregister(UniversPlugin.Instance, OnNetGetData);
            GetDataHandlers.TileEdit -= OnTileEdit;
            ServerApi.Hooks.ServerChat.Deregister(UniversPlugin.Instance, OnServerChat);
            ServerApi.Hooks.NetGreetPlayer.Deregister(UniversPlugin.Instance, OnNetGreetPlayer);
            ServerApi.Hooks.GameUpdate.Deregister(UniversPlugin.Instance, OnGameUpdate);

            _adminBinds.Clear();
            _boothPricePrompts.Clear();
            _signConfirms.Clear();
            while (_mainThreadActions.TryDequeue(out _)) { }

            Database?.Dispose();
        }

        public static void QueueMainThreadAction(Action action)
        {
            if (action != null)
            {
                _mainThreadActions.Enqueue(action);
            }
        }

        public static void StartAdminBind(TSPlayer admin, PhysicalDisplayType type, int itemId, byte prefix, long unitPrice, int quantity)
        {
            _adminBinds[admin.Index] = new AdminBindSession
            {
                IsRemoveMode = false,
                DisplayType = type,
                ItemId = itemId,
                Prefix = prefix,
                UnitPrice = unitPrice,
                Quantity = quantity,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
            };
        }

        public static void StartAdminRemove(TSPlayer admin)
        {
            _adminBinds[admin.Index] = new AdminBindSession
            {
                IsRemoveMode = true,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30)
            };
        }

        private static void OnGameUpdate(EventArgs args)
        {
            if (_initialSyncPending)
            {
                _initialSyncPending = false;
                RefreshAllPhysicalVisuals();
            }

            int processed = 0;
            while (processed < 32 && _mainThreadActions.TryDequeue(out var act))
            {
                processed++;
                try
                {
                    act();
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[Univers.Shop] MainThread action error: {ex.Message}");
                }
            }
        }

        private static void OnExpiryTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                lock (ShopApi.TxLock)
                {
                    var expired = Database.GetExpiredActiveListings(DateTime.UtcNow);
                    foreach (var listing in expired)
                    {
                        ShopApi.ExpireListingInternal(listing);
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.Shop] Expiry timer error: {ex.Message}");
            }
        }

        private static void OnNetGreetPlayer(GreetPlayerEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || !player.Active) return;

            System.Threading.Tasks.Task.Delay(4000).ContinueWith(_ =>
            {
                if (player == null || !player.Active || !player.IsLoggedIn) return;

                try
                {
                    var notices = Database.PopOfflineNotices(player.Account.Name);
                    foreach (var n in notices)
                    {
                        player.SendSuccessMessage(n.Message);
                    }

                    int claimCount = Database.GetPendingClaimCount(player.Account.Name);
                    if (claimCount > 0)
                    {
                        player.SendInfoMessage($"[ AH CLAIM ] Kamu punya {claimCount} item menunggu diambil! Ketik /ah claim.");
                    }
                }
                catch { }
            });
        }

        private static void OnServerChat(ServerChatEventArgs args)
        {
            if (args.Handled) return;
            var player = TShock.Players[args.Who];
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            if (!_boothPricePrompts.TryGetValue(player.Index, out var prompt)) return;

            string text = args.Text?.Trim() ?? string.Empty;
            if (text.StartsWith(TShock.Config.Settings.CommandSpecifier) ||
                text.StartsWith(TShock.Config.Settings.SilentCommandSpecifier))
            {
                return;
            }

            args.Handled = true;
            _boothPricePrompts.TryRemove(player.Index, out _);

            if (DateTime.UtcNow > prompt.ExpiresAtUtc)
            {
                player.SendWarningMessage("[ LAPAK ] Waktu input harga habis (20 detik). Klik ulang Sign Lapak jika ingin menjual.");
                return;
            }

            if (text.Equals("batal", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                player.SendInfoMessage("[ LAPAK ] Pemasangan lapak dibatalkan.");
                return;
            }

            string cleanNum = text.Replace(",", "").Replace(".", "").Trim();
            if (!long.TryParse(cleanNum, out long price))
            {
                player.SendErrorMessage("[ LAPAK ] Masukkan angka harga saja (contoh: 500) atau ketik 'batal'. Pemasangan dibatalkan.");
                return;
            }

            QueueMainThreadAction(() =>
            {
                if (!player.Active || !player.IsLoggedIn) return;

                Item cur = player.TPlayer.inventory[prompt.SlotIndex];
                if (cur == null || cur.IsAir || cur.type != prompt.ItemId || cur.prefix != prompt.Prefix || cur.stack < prompt.Stack)
                {
                    player.SendErrorMessage("[ LAPAK ] Item di tangan berubah sebelum harga dikonfirmasi! Transaksi dibatalkan.");
                    return;
                }

                if (ShopApi.TryCreateListing(player, prompt.SlotIndex, prompt.Stack, price, prompt.BoothDisplayId, out _, out string msg))
                {
                    player.SendSuccessMessage("[ LAPAK ] " + msg);
                }
                else
                {
                    player.SendErrorMessage("[ LAPAK ] " + msg);
                }
            });
        }

        private static void OnTileEdit(object? sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (e.Handled || e.Player == null) return;

            bool isDestructiveOrOverwrite =
                e.Action == GetDataHandlers.EditAction.KillTile ||
                e.Action == GetDataHandlers.EditAction.KillTileNoItem ||
                e.Action == GetDataHandlers.EditAction.PlaceTile ||
                e.Action == GetDataHandlers.EditAction.KillWall ||
                e.Action == GetDataHandlers.EditAction.ReplaceTile ||
                e.Action == GetDataHandlers.EditAction.ReplaceWall;

            if (!isDestructiveOrOverwrite) return;

            var displays = Database.GetAllDisplays();
            foreach (var d in displays)
            {
                bool touchesSignOrSupport = e.X >= d.SignX && e.X <= d.SignX + 1 && e.Y >= d.SignY && e.Y <= d.SignY + 2;
                bool touchesFrame = d.HasFrame && e.X >= d.FrameX && e.X <= d.FrameX + 1 && e.Y >= d.FrameY && e.Y <= d.FrameY + 1;

                if (touchesSignOrSupport || touchesFrame)
                {
                    e.Handled = true;
                    e.Player.SendTileSquareCentered(e.X, e.Y, 4);

                    if (e.Player.HasPermission("univers.shop.admin"))
                    {
                        e.Player.SendWarningMessage("[ TOKO ] Objek ini terhubung ke Showcase/Lapak! Gunakan /shopadmin removedisplay dulu sebelum membongkarnya.");
                    }
                    else
                    {
                        e.Player.SendErrorMessage("[ TOKO ] Area Showcase & Lapak Spawn dilindungi oleh server!");
                    }
                    return;
                }
            }
        }

        private static void OnNetGetData(GetDataEventArgs e)
        {
            int msgId = (int)e.MsgID;
            if (msgId != MessageID.SignRead &&
                msgId != MessageID.SignNew &&
                msgId != MessageID.PlaceItemFrame)
            {
                return;
            }

            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null || !player.Active) return;

            if (msgId == MessageID.PlaceItemFrame)
            {
                if (e.Length < 4) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                short rawX = reader.ReadInt16();
                short rawY = reader.ReadInt16();

                if (TryGetFrameTopLeft(rawX, rawY, out int fx, out int fy))
                {
                    var display = Database.GetDisplayByFrame(fx, fy);
                    if (display != null)
                    {
                        e.Handled = true;
                        SyncItemFrameOnly(display);
                        player.SendData(PacketTypes.PlayerSlot, "", player.Index, player.TPlayer.selectedItem);
                        player.SendInfoMessage("[ TOKO ] Item Frame Showcase hanya untuk dilihat (hover). Klik Sign di bawahnya untuk membeli/memasang!");
                    }
                }
                return;
            }

            if (msgId == MessageID.SignNew)
            {
                if (e.Length < 6) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                reader.ReadInt16(); // signId
                short rawX = reader.ReadInt16();
                short rawY = reader.ReadInt16();

                if (TryGetSignTopLeft(rawX, rawY, out int sx, out int sy, out _))
                {
                    var existing = Database.GetDisplayBySign(sx, sy);
                    if (existing != null)
                    {
                        e.Handled = true;
                        RefreshSignTextOnly(existing);
                        player.SendErrorMessage("[ TOKO ] Teks Sign Showcase dikelola otomatis oleh sistem!");
                    }
                }
                return;
            }

            if (msgId == MessageID.SignRead)
            {
                if (e.Length < 4) return;
                using var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length));
                short rawX = reader.ReadInt16();
                short rawY = reader.ReadInt16();

                if (!TryGetSignTopLeft(rawX, rawY, out int sx, out int sy, out int signIdx))
                {
                    return;
                }

                // Cek apakah admin sedang dalam mode bind / remove
                if (_adminBinds.TryGetValue(player.Index, out var adminSession))
                {
                    if (DateTime.UtcNow <= adminSession.ExpiresAtUtc)
                    {
                        e.Handled = true;
                        _adminBinds.TryRemove(player.Index, out _);
                        HandleAdminSignClick(player, adminSession, sx, sy, signIdx);
                        return;
                    }
                    _adminBinds.TryRemove(player.Index, out _);
                }

                var display = Database.GetDisplayBySign(sx, sy);
                if (display == null)
                {
                    // Sign biasa milik player -> biarkan Terraria membuka Sign Read/Edit seperti normal
                    return;
                }

                e.Handled = true;

                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("[ TOKO ] Login terlebih dahulu sebelum bertransaksi!");
                    return;
                }

                HandlePlayerDisplayClick(player, display);
            }
        }

        private static void HandleAdminSignClick(TSPlayer admin, AdminBindSession session, int sx, int sy, int signIdx)
        {
            if (!admin.HasPermission("univers.shop.admin"))
            {
                admin.SendErrorMessage("Kamu tidak punya akses univers.shop.admin.");
                return;
            }

            if (session.IsRemoveMode)
            {
                var existing = Database.GetDisplayBySign(sx, sy);
                if (existing == null)
                {
                    admin.SendWarningMessage("[ SHOPADMIN ] Sign ini tidak terdaftar sebagai Showcase/Lapak.");
                    return;
                }

                if (existing.ActiveListingId.HasValue)
                {
                    ShopApi.TryCancelListing(admin, existing.ActiveListingId.Value, true, out _, out _);
                }

                if (existing.HasFrame && existing.DisplayType == PhysicalDisplayType.PlayerBooth)
                {
                    SetItemFrameContents(existing.FrameX, existing.FrameY, 0, 0, 0);
                }

                Database.DeleteDisplay(existing.Id);
                if (signIdx >= 0 && Main.sign[signIdx] != null)
                {
                    Sign.TextSign(signIdx, "");
                }
                admin.SendSuccessMessage($"[ SHOPADMIN ] Showcase #{existing.Id} berhasil dilepas dari Sign ({sx},{sy}).");
                return;
            }

            var conflict = Database.GetDisplayBySign(sx, sy);
            if (conflict != null)
            {
                admin.SendErrorMessage($"[ SHOPADMIN ] Sign ini sudah terdaftar sebagai #{conflict.Id}. Hapus dulu dengan /shopadmin removedisplay.");
                return;
            }

            bool hasFrame = TryFindNearestItemFrame(sx, sy, 3, out int fx, out int fy);
            if (hasFrame && Database.GetDisplayByFrame(fx, fy) != null)
            {
                hasFrame = false;
            }

            int finalItemId = session.ItemId;
            byte finalPrefix = session.Prefix;

            if (session.DisplayType == PhysicalDisplayType.ServerShop && finalItemId <= 0 && hasFrame)
            {
                int teId = TEItemFrame.Find(fx, fy);
                if (teId >= 0 && TileEntity.ByID.TryGetValue(teId, out var te) && te is TEItemFrame frame &&
                    frame.item != null && !frame.item.IsAir && frame.item.type > 0)
                {
                    finalItemId = frame.item.type;
                    finalPrefix = frame.item.prefix;
                }
            }

            if (session.DisplayType == PhysicalDisplayType.ServerShop && finalItemId <= 0)
            {
                admin.SendErrorMessage("[ SHOPADMIN ] Pegang item di hotbar ATAU isi Item Frame dulu sebelum klik Sign!");
                return;
            }

            var rec = new PhysicalDisplayRecord
            {
                DisplayType = session.DisplayType,
                SignX = sx,
                SignY = sy,
                HasFrame = hasFrame,
                FrameX = fx,
                FrameY = fy,
                ServerItemId = finalItemId,
                ServerItemPrefix = finalPrefix,
                ServerQuantity = Math.Max(1, session.Quantity),
                ServerPrice = Math.Max(1, session.UnitPrice),
                CreatedBy = admin.Account.Name,
                CreatedAtUtc = DateTime.UtcNow
            };

            Database.SaveDisplay(rec);
            RefreshDisplayVisual(rec);

            string frameStatus = hasFrame ? $" + Item Frame ({fx},{fy}) tersambung!" : " (tanpa Item Frame di dekatnya)";
            admin.SendSuccessMessage($"[ SHOPADMIN ] Showcase #{rec.Id} ({rec.DisplayType}) berhasil dipasang di Sign ({sx},{sy}){frameStatus}");
        }

        private static void HandlePlayerDisplayClick(TSPlayer player, PhysicalDisplayRecord display)
        {
            if (display.DisplayType == PhysicalDisplayType.ServerShop)
            {
                long totalCost;
                try
                {
                    totalCost = checked(display.ServerPrice * display.ServerQuantity);
                }
                catch (OverflowException)
                {
                    player.SendErrorMessage("[ TOKO ] Nominal harga item ini tidak valid.");
                    return;
                }

                string itemLabel = ShopApi.FormatItemLabel(display.ServerItemId, display.ServerQuantity, display.ServerItemPrefix);

                if (IsConfirmedClick(player, display.Id, 0))
                {
                    if (ShopApi.TryBuyServerItem(player, display.ServerItemId, display.ServerItemPrefix, display.ServerQuantity, totalCost, out _, out string msg))
                    {
                        player.SendSuccessMessage(msg);
                        SendWorldText(display, "TERBELI!", Color.LightGreen);
                    }
                    else
                    {
                        player.SendErrorMessage(msg);
                    }
                }
                else
                {
                    SetConfirmClick(player, display.Id, 0);
                    player.SendInfoMessage($"[ TOKO SERVER ] {itemLabel} | Harga: {EconomyApi.FormatColored(totalCost)}");
                    player.SendInfoMessage($"[ KONFIRMASI ] Klik Sign ini 1x lagi dalam {Config.ConfirmTimeoutSeconds} detik untuk MEMBELI.");
                    SendWorldText(display, "Klik 1x lagi = Beli", Color.Gold);
                }
                return;
            }

            if (display.DisplayType == PhysicalDisplayType.ServerSellback)
            {
                int slot = player.TPlayer.selectedItem;
                Item inv = slot >= 0 && slot < 50 ? player.TPlayer.inventory[slot] : new Item();

                if (inv == null || inv.IsAir || inv.type <= 0)
                {
                    player.SendWarningMessage("[ JUAL KE SERVER ] Pegang item di hotbar (tangan) terlebih dahulu, lalu klik Sign ini 2x untuk menjual!");
                    return;
                }

                var catalog = Config.GetCatalogItemById(inv.type);
                if (catalog == null || catalog.SellPrice <= 0)
                {
                    player.SendWarningMessage($"[ JUAL KE SERVER ] {ShopApi.GetItemName(inv.type)} tidak dibeli server. Gunakan Lapak kosong atau /ah sell!");
                    return;
                }

                long payout = checked(catalog.SellPrice * inv.stack);
                string itemLabel = ShopApi.FormatItemLabel(inv.type, inv.stack, inv.prefix);

                if (IsConfirmedClick(player, display.Id, inv.type))
                {
                    if (ShopApi.TrySellHeldToServer(player, slot, null, out string msg))
                    {
                        player.SendSuccessMessage(msg);
                        SendWorldText(display, $"+{EconomyApi.Format(payout)}", Color.Violet);
                    }
                    else
                    {
                        player.SendErrorMessage(msg);
                    }
                }
                else
                {
                    SetConfirmClick(player, display.Id, inv.type);
                    player.SendInfoMessage($"[ JUAL KE SERVER ] {itemLabel} -> Estimasi hasil: {EconomyApi.FormatColored(payout)}");
                    player.SendInfoMessage($"[ KONFIRMASI ] Klik Sign ini 1x lagi dalam {Config.ConfirmTimeoutSeconds} detik untuk MENJUAL seluruh stack di tangan.");
                    SendWorldText(display, "Klik 1x lagi = Jual", Color.Violet);
                }
                return;
            }

            if (display.DisplayType == PhysicalDisplayType.PlayerBooth)
            {
                MarketListing? active = display.ActiveListingId.HasValue
                    ? Database.GetListingById(display.ActiveListingId.Value)
                    : null;

                if (active != null && active.Status == ListingStatus.Active && active.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    lock (ShopApi.TxLock)
                    {
                        ShopApi.ExpireListingInternal(active);
                    }
                    active = null;
                    display = Database.GetDisplayById(display.Id) ?? display;
                }

                // Kasus A: Lapak kosong -> Player pasang dagangan di sini
                if (active == null || active.Status != ListingStatus.Active)
                {
                    if (display.ActiveListingId.HasValue)
                    {
                        display.ActiveListingId = null;
                        Database.SaveDisplay(display);
                        RefreshDisplayVisual(display);
                    }

                    int slot = player.TPlayer.selectedItem;
                    Item inv = slot >= 0 && slot < 50 ? player.TPlayer.inventory[slot] : new Item();
                    if (inv == null || inv.IsAir || inv.type <= 0)
                    {
                        player.SendInfoMessage($"[ LAPAK #{display.Id} KOSONG ] Pegang item di hotbar, lalu klik Sign ini untuk memajang dagangan!");
                        SendWorldText(display, "Pegang Item u/ Jual", Color.Cyan);
                        return;
                    }

                    if (ShopApi.IsRestrictedShopItem(inv.type))
                    {
                        player.SendErrorMessage("[ LAPAK ] Item Coin / Aetherial Large Gem tidak boleh dijual di Lapak.");
                        return;
                    }

                    int activeCount = Database.GetActiveListingsBySeller(player.Account.Name).Count;
                    int maxListings = ShopApi.GetMaxListingsForPlayer(player);
                    if (activeCount >= maxListings)
                    {
                        player.SendErrorMessage($"[ LAPAK ] Slot jualan kamu penuh ({activeCount}/{maxListings} untuk rank kamu).");
                        return;
                    }

                    _boothPricePrompts[player.Index] = new PlayerBoothPriceSession
                    {
                        BoothDisplayId = display.Id,
                        SlotIndex = slot,
                        ItemId = inv.type,
                        Stack = inv.stack,
                        Prefix = inv.prefix,
                        ExpiresAtUtc = DateTime.UtcNow.AddSeconds(20)
                    };

                    string label = ShopApi.FormatItemLabel(inv.type, inv.stack, inv.prefix);
                    player.SendSuccessMessage($"[ LAPAK #{display.Id} ] Mau jual {label}!");
                    player.SendInfoMessage("[ KETIK HARGA ] Tulis nominal SHARD langsung di chat (misal: 500) dalam 20 detik, atau ketik 'batal'.");
                    SendWorldText(display, "Ketik Harga di Chat!", Color.Cyan);
                    return;
                }

                // Kasus B: Lapak sedang terisi -> Player inspect atau beli (2x klik)
                bool isOwner = active.SellerAccount.Equals(player.Account.Name, StringComparison.OrdinalIgnoreCase);
                string listingLabel = ShopApi.FormatItemLabel(active.ItemId, active.Stack, active.Prefix);

                if (isOwner && !Config.AllowSelfBuyForTesting)
                {
                    player.SendInfoMessage($"[ LAPAK #{display.Id} MILIKMU ] {listingLabel} | Harga: {EconomyApi.FormatColored(active.TotalPrice)} (Listing #{active.Id})");
                    player.SendInfoMessage($"[ TARIK BARANG ] Ketik /ah cancel {active.Id} jika ingin menarik kembali barang ini.");
                    return;
                }

                if (IsConfirmedClick(player, display.Id, active.Id))
                {
                    if (ShopApi.TryBuyListing(player, active.Id, out _, out _, out string msg))
                    {
                        player.SendSuccessMessage(msg);
                        SendWorldText(display, "TERJUAL!", Color.LightGreen);
                    }
                    else
                    {
                        player.SendErrorMessage(msg);
                    }
                }
                else
                {
                    SetConfirmClick(player, display.Id, active.Id);
                    string selfHint = isOwner ? " [SELF-TEST MODE]" : string.Empty;
                    player.SendInfoMessage($"[ LAPAK #{display.Id}{selfHint} ] {listingLabel} | Penjual: {active.SellerAccount} | Harga: {EconomyApi.FormatColored(active.TotalPrice)}");
                    player.SendInfoMessage($"[ KONFIRMASI ] Klik Sign ini 1x lagi dalam {Config.ConfirmTimeoutSeconds} detik untuk MEMBELI.");
                    SendWorldText(display, "Klik 1x lagi = Beli", Color.Gold);
                }
            }
        }

        private static bool IsConfirmedClick(TSPlayer player, int displayId, int contextId)
        {
            if (_signConfirms.TryGetValue(player.Index, out var session))
            {
                if (session.DisplayId == displayId &&
                    session.ContextListingId == contextId &&
                    DateTime.UtcNow <= session.ExpiresAtUtc)
                {
                    _signConfirms.TryRemove(player.Index, out _);
                    return true;
                }
            }
            return false;
        }

        private static void SetConfirmClick(TSPlayer player, int displayId, int contextId)
        {
            _signConfirms[player.Index] = new SignConfirmSession
            {
                DisplayId = displayId,
                ContextListingId = contextId,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Config.ConfirmTimeoutSeconds)
            };
        }

        private static void SendWorldText(PhysicalDisplayRecord display, string text, Color color)
        {
            float wx = display.SignX * 16f + 16f;
            float wy = display.SignY * 16f - 18f;
            TSPlayer.All.SendData(PacketTypes.CreateCombatTextExtended, text, (int)color.PackedValue, wx, wy);
        }

        public static void RefreshAllPhysicalVisuals()
        {
            foreach (var d in Database.GetAllDisplays())
            {
                RefreshDisplayVisual(d);
            }
        }

        public static void RefreshDisplayVisual(PhysicalDisplayRecord display)
        {
            SyncItemFrameOnly(display);
            RefreshSignTextOnly(display);
        }

        private static void SyncItemFrameOnly(PhysicalDisplayRecord display)
        {
            if (!display.HasFrame) return;

            if (display.DisplayType == PhysicalDisplayType.ServerShop)
            {
                SetItemFrameContents(display.FrameX, display.FrameY, display.ServerItemId, 1, display.ServerItemPrefix);
            }
            else if (display.DisplayType == PhysicalDisplayType.ServerSellback)
            {
                SetItemFrameContents(display.FrameX, display.FrameY, ItemID.GoldCoin, 1, 0);
            }
            else if (display.DisplayType == PhysicalDisplayType.PlayerBooth)
            {
                var listing = display.ActiveListingId.HasValue
                    ? Database.GetListingById(display.ActiveListingId.Value)
                    : null;

                if (listing != null && listing.Status == ListingStatus.Active && listing.ExpiresAtUtc > DateTime.UtcNow)
                {
                    SetItemFrameContents(display.FrameX, display.FrameY, listing.ItemId, Math.Max(1, listing.Stack), listing.Prefix);
                }
                else
                {
                    SetItemFrameContents(display.FrameX, display.FrameY, 0, 0, 0);
                }
            }
        }

        private static void RefreshSignTextOnly(PhysicalDisplayRecord display)
        {
            int signIdx = Sign.ReadSign(display.SignX, display.SignY, false);
            if (signIdx < 0 || Main.sign[signIdx] == null) return;

            string text;
            if (display.DisplayType == PhysicalDisplayType.ServerShop)
            {
                long total = display.ServerPrice * display.ServerQuantity;
                text = $"[TOKO SERVER]\n{ShopApi.GetItemName(display.ServerItemId)} x{display.ServerQuantity}\nHarga: {EconomyApi.Format(total)}\nKlik Sign 2x = Beli";
            }
            else if (display.DisplayType == PhysicalDisplayType.ServerSellback)
            {
                text = "[JUAL KE SERVER]\nPegang Item di Tangan\nKlik Sign 2x = Jual Cepat\n(Ores, Potion, Material)";
            }
            else
            {
                var listing = display.ActiveListingId.HasValue
                    ? Database.GetListingById(display.ActiveListingId.Value)
                    : null;

                if (listing != null && listing.Status == ListingStatus.Active && listing.ExpiresAtUtc > DateTime.UtcNow)
                {
                    text = $"[LAPAK #{display.Id}]\n{listing.ItemName} x{listing.Stack}\nPenjual: {listing.SellerAccount}\nHarga: {EconomyApi.Format(listing.TotalPrice)}\nKlik Sign 2x = Beli";
                }
                else
                {
                    text = $"[LAPAK #{display.Id} KOSONG]\nPegang item di tangan\nlalu klik Sign ini\nuntuk mulai berjualan!";
                }
            }

            Sign.TextSign(signIdx, text);
        }

        private static void SetItemFrameContents(int frameTopX, int frameTopY, int itemId, int stack, byte prefix)
        {
            int teId = TEItemFrame.Find(frameTopX, frameTopY);
            if (teId < 0)
            {
                teId = TEItemFrame.Place(frameTopX, frameTopY);
            }

            if (teId >= 0 && TileEntity.ByID.TryGetValue(teId, out var te) && te is TEItemFrame frame)
            {
                if (itemId <= 0)
                {
                    frame.item.SetDefaults(0);
                    frame.item.stack = 0;
                    frame.item.prefix = 0;
                }
                else
                {
                    frame.item.SetDefaults(itemId);
                    frame.item.stack = Math.Max(1, stack);
                    if (prefix > 0) frame.item.Prefix(prefix);
                }

                NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, frame.ID, frame.Position.X, frame.Position.Y);
            }
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

        private static bool TryGetFrameTopLeft(int rawX, int rawY, out int topX, out int topY)
        {
            topX = 0;
            topY = 0;

            if (rawX < 0 || rawY < 0 || rawX >= Main.maxTilesX || rawY >= Main.maxTilesY) return false;
            ITile tile = Main.tile[rawX, rawY];
            if (tile == null || !tile.active() || tile.type != TileID.ItemFrame) return false;

            int offsetX = (tile.frameX / 18) % 2;
            int offsetY = (tile.frameY / 18) % 2;
            topX = rawX - offsetX;
            topY = rawY - offsetY;
            return true;
        }

        private static bool TryFindNearestItemFrame(int signX, int signY, int radius, out int frameTopX, out int frameTopY)
        {
            frameTopX = 0;
            frameTopY = 0;
            int bestDistSq = int.MaxValue;
            bool found = false;

            int minX = Math.Max(0, signX - radius);
            int maxX = Math.Min(Main.maxTilesX - 1, signX + 1 + radius);
            int minY = Math.Max(0, signY - radius - 1);
            int maxY = Math.Min(Main.maxTilesY - 1, signY + 1 + radius);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (TryGetFrameTopLeft(x, y, out int fx, out int fy))
                    {
                        int dx = fx - signX;
                        int dy = fy - signY;
                        int distSq = dx * dx + dy * dy;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            frameTopX = fx;
                            frameTopY = fy;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }
    }
}
