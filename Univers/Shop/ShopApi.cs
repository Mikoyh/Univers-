/*
 * =====================================================================
 *  ShopApi.cs - Transactional Anti-Dupe Shop & Auction Engine
 *  Namespace: Univers.Shop (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Skynomi.Economy;
using Skynomi.Ranks;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Shop
{
    public readonly struct ItemSnapshot
    {
        public int ItemId { get; }
        public int Stack { get; }
        public byte Prefix { get; }
        public string Name { get; }

        public ItemSnapshot(int itemId, int stack, byte prefix, string name)
        {
            ItemId = itemId;
            Stack = stack;
            Prefix = prefix;
            Name = name;
        }
    }

    public static class ShopApi
    {
        internal static readonly object TxLock = new();

        public static bool IsRestrictedShopItem(int itemId)
        {
            if (itemId <= 0) return true;

            return itemId == ItemID.CopperCoin ||
                   itemId == ItemID.SilverCoin ||
                   itemId == ItemID.GoldCoin ||
                   itemId == ItemID.PlatinumCoin ||
                   itemId == ItemID.LargeAmethyst ||
                   itemId == ItemID.LargeTopaz ||
                   itemId == ItemID.LargeSapphire ||
                   itemId == ItemID.LargeEmerald ||
                   itemId == ItemID.LargeRuby ||
                   itemId == ItemID.LargeDiamond ||
                   itemId == ItemID.LargeAmber;
        }

        public static string GetItemName(int itemId)
        {
            if (itemId <= 0) return "Air";
            string name = Lang.GetItemNameValue(itemId);
            return string.IsNullOrWhiteSpace(name) ? $"Item #{itemId}" : name;
        }

        public static string FormatItemTag(int itemId, int stack = 1, byte prefix = 0)
        {
            if (itemId <= 0) return "[Kosong]";
            if (stack > 1) return $"[i/s{stack}:{itemId}]";
            if (prefix > 0) return $"[i/p{prefix}:{itemId}]";
            return $"[i:{itemId}]";
        }

        public static string FormatItemLabel(int itemId, int stack = 1, byte prefix = 0)
        {
            string name = GetItemName(itemId);
            string tag = FormatItemTag(itemId, stack, prefix);
            return stack > 1 ? $"{tag} {name} x{stack}" : $"{tag} {name}";
        }

        public static int GetMaxListingsForPlayer(TSPlayer player)
        {
            int rankIndex = 0;
            try
            {
                rankIndex = RankApi.GetRankIndex(player);
            }
            catch { }

            if (ShopModule.Config.MaxListingsPerRank.TryGetValue(rankIndex, out int limit))
            {
                return Math.Max(1, limit);
            }
            return 2;
        }

        private static int GetMaxStackForItem(int itemId)
        {
            var probe = new Item();
            probe.SetDefaults(itemId);
            return Math.Max(1, probe.maxStack);
        }

        public static bool TryTakeFromInventorySlot(TSPlayer player, int slot, int quantity, out ItemSnapshot taken, out string error)
        {
            taken = default;
            error = string.Empty;

            if (player == null || !player.Active || !player.IsLoggedIn || player.TPlayer == null)
            {
                error = "Kamu harus online dan sudah login untuk melakukan transaksi.";
                return false;
            }

            if (slot < 0 || slot >= 50)
            {
                error = "Pilih item di Hotbar atau Inventory utama terlebih dahulu.";
                return false;
            }

            if (quantity <= 0)
            {
                error = "Jumlah item harus lebih dari 0.";
                return false;
            }

            Item inv = player.TPlayer.inventory[slot];
            if (inv == null || inv.IsAir || inv.type <= 0 || inv.stack <= 0)
            {
                error = "Slot tangan kamu kosong! Pegang item yang ingin diproses.";
                return false;
            }

            if (IsRestrictedShopItem(inv.type))
            {
                error = "Item khusus (Coin / Aetherial Large Gem) tidak boleh dijual di Toko/AH.";
                return false;
            }

            if (inv.stack < quantity)
            {
                error = $"Jumlah item di tangan kurang (punya {inv.stack}, butuh {quantity}).";
                return false;
            }

            int itemId = inv.type;
            byte prefix = inv.prefix;
            string itemName = GetItemName(itemId);

            inv.stack -= quantity;
            if (inv.stack <= 0)
            {
                inv.SetDefaults(0);
                inv.stack = 0;
                inv.prefix = 0;
            }

            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, slot);
            taken = new ItemSnapshot(itemId, quantity, prefix, itemName);
            return true;
        }

        public static bool TryGiveOrQueueItem(TSPlayer? player, string accountName, int itemId, int stack, byte prefix, string sourceReason, out bool queuedToClaim)
        {
            queuedToClaim = false;
            if (itemId <= 0 || stack <= 0) return false;

            int maxStack = GetMaxStackForItem(itemId);

            if (player != null && player.Active && player.IsLoggedIn && player.TPlayer != null)
            {
                int remainingCapacity = 0;
                for (int i = 0; i < 50; i++)
                {
                    Item slot = player.TPlayer.inventory[i];
                    if (slot == null || slot.IsAir || slot.type <= 0)
                    {
                        remainingCapacity += maxStack;
                    }
                    else if (maxStack > 1 && slot.type == itemId && slot.prefix == prefix && slot.stack < maxStack)
                    {
                        remainingCapacity += (maxStack - slot.stack);
                    }

                    if (remainingCapacity >= stack) break;
                }

                if (remainingCapacity >= stack)
                {
                    int toInsert = stack;
                    var touchedSlots = new List<int>();

                    if (maxStack > 1)
                    {
                        for (int i = 0; i < 50 && toInsert > 0; i++)
                        {
                            Item slot = player.TPlayer.inventory[i];
                            if (slot != null && !slot.IsAir && slot.type == itemId && slot.prefix == prefix && slot.stack < maxStack)
                            {
                                int add = Math.Min(toInsert, maxStack - slot.stack);
                                slot.stack += add;
                                toInsert -= add;
                                touchedSlots.Add(i);
                            }
                        }
                    }

                    for (int i = 0; i < 50 && toInsert > 0; i++)
                    {
                        Item slot = player.TPlayer.inventory[i];
                        if (slot == null || slot.IsAir || slot.type <= 0)
                        {
                            if (slot == null)
                            {
                                slot = new Item();
                                player.TPlayer.inventory[i] = slot;
                            }
                            int add = Math.Min(toInsert, maxStack);
                            slot.SetDefaults(itemId);
                            slot.stack = add;
                            if (prefix > 0) slot.Prefix(prefix);
                            toInsert -= add;
                            touchedSlots.Add(i);
                        }
                    }

                    foreach (int sIdx in touchedSlots)
                    {
                        NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, sIdx);
                    }

                    return true;
                }
            }

            // Inventory penuh atau player offline -> masukkan ke Pending Claim (/ah claim)
            ShopModule.Database.AddPendingClaim(new PendingClaimItem
            {
                AccountName = accountName,
                ItemId = itemId,
                ItemName = GetItemName(itemId),
                Stack = stack,
                Prefix = prefix,
                Reason = sourceReason,
                CreatedAtUtc = DateTime.UtcNow
            });
            queuedToClaim = true;
            return true;
        }

        public static bool TryCreateListing(
            TSPlayer seller,
            int slot,
            int quantity,
            long totalPrice,
            int? boothDisplayId,
            out MarketListing? created,
            out string message)
        {
            created = null;
            message = string.Empty;

            if (seller == null || !seller.Active || !seller.IsLoggedIn)
            {
                message = "Kamu harus login terlebih dahulu.";
                return false;
            }

            if (totalPrice < ShopModule.Config.MinListingPrice || totalPrice > ShopModule.Config.MaxListingPrice)
            {
                message = $"Harga harus antara {EconomyApi.Format(ShopModule.Config.MinListingPrice)} s/d {EconomyApi.Format(ShopModule.Config.MaxListingPrice)}.";
                return false;
            }

            lock (TxLock)
            {
                PhysicalDisplayRecord? booth = null;
                if (boothDisplayId.HasValue)
                {
                    booth = ShopModule.Database.GetDisplayById(boothDisplayId.Value);
                    if (booth == null || booth.DisplayType != PhysicalDisplayType.PlayerBooth)
                    {
                        message = "Lapak fisik tidak ditemukan.";
                        return false;
                    }
                    if (booth.ActiveListingId.HasValue)
                    {
                        var existing = ShopModule.Database.GetListingById(booth.ActiveListingId.Value);
                        if (existing != null && existing.Status == ListingStatus.Active && existing.ExpiresAtUtc > DateTime.UtcNow)
                        {
                            message = "Lapak ini baru saja dipakai oleh player lain!";
                            return false;
                        }
                        booth.ActiveListingId = null;
                        ShopModule.Database.SaveDisplay(booth);
                    }
                }

                var activeOwn = ShopModule.Database.GetActiveListingsBySeller(seller.Account.Name);
                int maxListings = GetMaxListingsForPlayer(seller);
                if (activeOwn.Count >= maxListings)
                {
                    message = $"Slot jualan kamu penuh ({activeOwn.Count}/{maxListings} untuk rank kamu).";
                    return false;
                }

                long fee = ShopModule.Config.ListingFeeShards;
                if (fee > 0 && EconomyApi.GetBalance(seller.Account.Name) < fee)
                {
                    message = $"Biaya pasang listing butuh {EconomyApi.FormatColored(fee)}.";
                    return false;
                }

                if (!TryTakeFromInventorySlot(seller, slot, quantity, out ItemSnapshot taken, out string takeErr))
                {
                    message = takeErr;
                    return false;
                }

                if (fee > 0 && !EconomyApi.TryDebit(seller.Account.Name, fee, "AH Listing Fee"))
                {
                    // Refund item ke tangan jika biaya tiba-tiba gagal
                    TryGiveOrQueueItem(seller, seller.Account.Name, taken.ItemId, taken.Stack, taken.Prefix, "Refund Listing Fee Failure", out _);
                    message = "Gagal memotong biaya pasang listing.";
                    return false;
                }

                var listing = new MarketListing
                {
                    SellerAccount = seller.Account.Name,
                    ItemId = taken.ItemId,
                    ItemName = taken.Name,
                    Stack = taken.Stack,
                    Prefix = taken.Prefix,
                    TotalPrice = totalPrice,
                    Status = ListingStatus.Active,
                    BoothDisplayId = booth?.Id,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(ShopModule.Config.ListingDurationHours)
                };

                ShopModule.Database.InsertListing(listing);

                if (booth != null)
                {
                    booth.ActiveListingId = listing.Id;
                    ShopModule.Database.SaveDisplay(booth);
                    ShopModule.RefreshDisplayVisual(booth);
                }

                created = listing;
                message = $"Listing #{listing.Id} terpasang: {FormatItemLabel(taken.ItemId, taken.Stack, taken.Prefix)} seharga {EconomyApi.FormatColored(totalPrice)}!";
                return true;
            }
        }

        public static bool TryBuyListing(
            TSPlayer buyer,
            int listingId,
            out MarketListing? soldListing,
            out bool deliveredDirect,
            out string message)
        {
            soldListing = null;
            deliveredDirect = false;
            message = string.Empty;

            if (buyer == null || !buyer.Active || !buyer.IsLoggedIn)
            {
                message = "Kamu harus login terlebih dahulu.";
                return false;
            }

            lock (TxLock)
            {
                var listing = ShopModule.Database.GetListingById(listingId);
                if (listing == null || listing.Status != ListingStatus.Active)
                {
                    message = $"Listing #{listingId} tidak ditemukan atau sudah terjual.";
                    return false;
                }

                if (listing.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    ExpireListingInternal(listing);
                    message = $"Listing #{listingId} sudah kedaluwarsa.";
                    return false;
                }

                bool isSelf = listing.SellerAccount.Equals(buyer.Account.Name, StringComparison.OrdinalIgnoreCase);
                if (isSelf && !ShopModule.Config.AllowSelfBuyForTesting)
                {
                    message = "Kamu tidak bisa membeli barang jualanmu sendiri. Gunakan /ah cancel " + listing.Id + " untuk menariknya kembali.";
                    return false;
                }

                if (!EconomyApi.TryDebit(buyer.Account.Name, listing.TotalPrice, $"AH Buy #{listing.Id}"))
                {
                    long bal = EconomyApi.GetBalance(buyer.Account.Name);
                    message = $"Shard tidak cukup! Harga: {EconomyApi.FormatColored(listing.TotalPrice)} | Dompetmu: {EconomyApi.FormatColored(bal)}.";
                    return false;
                }

                // Tandai terjual sebelum credit & delivery agar aman 100% dari double-buy
                listing.Status = ListingStatus.Sold;
                listing.BuyerAccount = buyer.Account.Name;
                listing.SoldAtUtc = DateTime.UtcNow;
                ShopModule.Database.UpdateListing(listing);

                long tax = (long)Math.Floor(listing.TotalPrice * (ShopModule.Config.ShopTaxPercent / 100.0));
                long sellerNet = Math.Max(1, listing.TotalPrice - tax);

                EconomyApi.TryCredit(listing.SellerAccount, sellerNet, $"AH Sale #{listing.Id}");

                var onlineSeller = TShock.Players.FirstOrDefault(p =>
                    p != null && p.Active && p.IsLoggedIn &&
                    p.Account.Name.Equals(listing.SellerAccount, StringComparison.OrdinalIgnoreCase));

                string itemLabel = FormatItemLabel(listing.ItemId, listing.Stack, listing.Prefix);
                if (onlineSeller != null && !isSelf)
                {
                    onlineSeller.SendSuccessMessage($"[ + AH TERJUAL ] {itemLabel} dibeli oleh {buyer.Name}!");
                    onlineSeller.SendInfoMessage($"[ + SALDO ] +{EconomyApi.FormatColored(sellerNet)} masuk ke akunmu (pajak {ShopModule.Config.ShopTaxPercent:0.#}%: {EconomyApi.Format(tax)}).");
                }
                else if (onlineSeller == null)
                {
                    ShopModule.Database.AddOfflineNotice(
                        listing.SellerAccount,
                        $"[ + AH OFFLINE SALE ] {itemLabel} laku dibeli {buyer.Name}! Hasil bersih +{EconomyApi.FormatColored(sellerNet)} sudah masuk ke akunmu."
                    );
                }

                TryGiveOrQueueItem(buyer, buyer.Account.Name, listing.ItemId, listing.Stack, listing.Prefix, $"Pembelian AH #{listing.Id}", out bool queued);
                deliveredDirect = !queued;

                if (listing.BoothDisplayId.HasValue)
                {
                    var booth = ShopModule.Database.GetDisplayById(listing.BoothDisplayId.Value);
                    if (booth != null && booth.ActiveListingId == listing.Id)
                    {
                        booth.ActiveListingId = null;
                        ShopModule.Database.SaveDisplay(booth);
                        ShopModule.RefreshDisplayVisual(booth);
                    }
                }

                ShopModule.Database.LogTransaction(new ShopTransactionLog
                {
                    Kind = listing.BoothDisplayId.HasValue ? "BoothSale" : "AuctionSale",
                    BuyerAccount = buyer.Account.Name,
                    SellerAccount = listing.SellerAccount,
                    ItemId = listing.ItemId,
                    Stack = listing.Stack,
                    Prefix = listing.Prefix,
                    GrossShards = listing.TotalPrice,
                    TaxShards = tax,
                    TimestampUtc = DateTime.UtcNow
                });

                soldListing = listing;
                message = queued
                    ? $"[ + TERBELI ] {itemLabel} seharga {EconomyApi.FormatColored(listing.TotalPrice)}! Tas penuh -> item disimpan aman di /ah claim."
                    : $"[ + TERBELI ] {itemLabel} seharga {EconomyApi.FormatColored(listing.TotalPrice)}! Cek inventory kamu.";
                return true;
            }
        }

        public static bool TryCancelListing(TSPlayer actor, int listingId, bool isAdmin, out bool deliveredDirect, out string message)
        {
            deliveredDirect = false;
            message = string.Empty;

            if (actor == null || !actor.Active || !actor.IsLoggedIn)
            {
                message = "Kamu harus login terlebih dahulu.";
                return false;
            }

            lock (TxLock)
            {
                var listing = ShopModule.Database.GetListingById(listingId);
                if (listing == null || listing.Status != ListingStatus.Active)
                {
                    message = $"Listing #{listingId} tidak ditemukan atau tidak lagi aktif.";
                    return false;
                }

                if (!isAdmin && !listing.SellerAccount.Equals(actor.Account.Name, StringComparison.OrdinalIgnoreCase))
                {
                    message = "Kamu tidak bisa membatalkan listing milik player lain.";
                    return false;
                }

                listing.Status = ListingStatus.Cancelled;
                ShopModule.Database.UpdateListing(listing);

                if (listing.BoothDisplayId.HasValue)
                {
                    var booth = ShopModule.Database.GetDisplayById(listing.BoothDisplayId.Value);
                    if (booth != null && booth.ActiveListingId == listing.Id)
                    {
                        booth.ActiveListingId = null;
                        ShopModule.Database.SaveDisplay(booth);
                        ShopModule.RefreshDisplayVisual(booth);
                    }
                }

                var targetPlayer = isAdmin && !listing.SellerAccount.Equals(actor.Account.Name, StringComparison.OrdinalIgnoreCase)
                    ? TShock.Players.FirstOrDefault(p => p != null && p.Active && p.IsLoggedIn && p.Account.Name.Equals(listing.SellerAccount, StringComparison.OrdinalIgnoreCase))
                    : actor;

                TryGiveOrQueueItem(targetPlayer, listing.SellerAccount, listing.ItemId, listing.Stack, listing.Prefix, $"Batal Listing #{listing.Id}", out bool queued);
                deliveredDirect = !queued;

                string itemLabel = FormatItemLabel(listing.ItemId, listing.Stack, listing.Prefix);
                message = queued
                    ? $"Listing #{listing.Id} ({itemLabel}) dibatalkan. Item dikirim ke /ah claim pemilik."
                    : $"Listing #{listing.Id} ({itemLabel}) dibatalkan dan dikembalikan ke inventory.";
                return true;
            }
        }

        internal static void ExpireListingInternal(MarketListing listing)
        {
            listing.Status = ListingStatus.Expired;
            ShopModule.Database.UpdateListing(listing);

            var onlineSeller = TShock.Players.FirstOrDefault(p =>
                p != null && p.Active && p.IsLoggedIn &&
                p.Account.Name.Equals(listing.SellerAccount, StringComparison.OrdinalIgnoreCase));

            TryGiveOrQueueItem(null, listing.SellerAccount, listing.ItemId, listing.Stack, listing.Prefix, $"Listing #{listing.Id} Kedaluwarsa", out _);

            string itemLabel = FormatItemLabel(listing.ItemId, listing.Stack, listing.Prefix);
            if (onlineSeller != null)
            {
                onlineSeller.SendInfoMessage($"[ AH EXPIRED ] Listing #{listing.Id} ({itemLabel}) kedaluwarsa. Ambil barang kembali dengan /ah claim.");
            }
            else
            {
                ShopModule.Database.AddOfflineNotice(
                    listing.SellerAccount,
                    $"[ AH EXPIRED ] Listing #{listing.Id} ({itemLabel}) kedaluwarsa. Ketik /ah claim untuk mengambil barangmu kembali."
                );
            }

            if (listing.BoothDisplayId.HasValue)
            {
                var booth = ShopModule.Database.GetDisplayById(listing.BoothDisplayId.Value);
                if (booth != null && booth.ActiveListingId == listing.Id)
                {
                    booth.ActiveListingId = null;
                    ShopModule.Database.SaveDisplay(booth);
                    ShopModule.QueueMainThreadAction(() => ShopModule.RefreshDisplayVisual(booth));
                }
            }
        }

        public static int ClaimPendingItems(TSPlayer player, out int remainingCount)
        {
            remainingCount = 0;
            if (player == null || !player.Active || !player.IsLoggedIn) return 0;

            lock (TxLock)
            {
                var claims = ShopModule.Database.GetPendingClaims(player.Account.Name);
                int claimed = 0;

                foreach (var item in claims)
                {
                    if (TryGiveDirectOnly(player, item.ItemId, item.Stack, item.Prefix))
                    {
                        ShopModule.Database.DeletePendingClaim(item.Id);
                        claimed++;
                    }
                    else
                    {
                        remainingCount++;
                    }
                }

                return claimed;
            }
        }

        private static bool TryGiveDirectOnly(TSPlayer player, int itemId, int stack, byte prefix)
        {
            if (player == null || !player.Active || !player.IsLoggedIn || player.TPlayer == null) return false;
            int maxStack = GetMaxStackForItem(itemId);
            int remainingCapacity = 0;

            for (int i = 0; i < 50; i++)
            {
                Item slot = player.TPlayer.inventory[i];
                if (slot == null || slot.IsAir || slot.type <= 0)
                {
                    remainingCapacity += maxStack;
                }
                else if (maxStack > 1 && slot.type == itemId && slot.prefix == prefix && slot.stack < maxStack)
                {
                    remainingCapacity += (maxStack - slot.stack);
                }

                if (remainingCapacity >= stack) break;
            }

            if (remainingCapacity < stack) return false;

            int toInsert = stack;
            var touched = new List<int>();

            if (maxStack > 1)
            {
                for (int i = 0; i < 50 && toInsert > 0; i++)
                {
                    Item slot = player.TPlayer.inventory[i];
                    if (slot != null && !slot.IsAir && slot.type == itemId && slot.prefix == prefix && slot.stack < maxStack)
                    {
                        int add = Math.Min(toInsert, maxStack - slot.stack);
                        slot.stack += add;
                        toInsert -= add;
                        touched.Add(i);
                    }
                }
            }

            for (int i = 0; i < 50 && toInsert > 0; i++)
            {
                Item slot = player.TPlayer.inventory[i];
                if (slot == null || slot.IsAir || slot.type <= 0)
                {
                    if (slot == null)
                    {
                        slot = new Item();
                        player.TPlayer.inventory[i] = slot;
                    }
                    int add = Math.Min(toInsert, maxStack);
                    slot.SetDefaults(itemId);
                    slot.stack = add;
                    if (prefix > 0) slot.Prefix(prefix);
                    toInsert -= add;
                    touched.Add(i);
                }
            }

            foreach (int idx in touched)
            {
                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, null, player.Index, idx);
            }
            return true;
        }

        public static bool TryBuyServerItem(TSPlayer buyer, int itemId, byte prefix, int quantity, long totalPrice, out bool deliveredDirect, out string message)
        {
            deliveredDirect = false;
            message = string.Empty;

            if (buyer == null || !buyer.Active || !buyer.IsLoggedIn)
            {
                message = "Kamu harus login terlebih dahulu.";
                return false;
            }

            if (quantity <= 0 || quantity > 9999)
            {
                message = "Jumlah pembelian harus antara 1 s/d 9999.";
                return false;
            }

            if (IsRestrictedShopItem(itemId))
            {
                message = "Item ini tidak tersedia untuk transaksi toko.";
                return false;
            }

            if (totalPrice <= 0)
            {
                message = "Harga item toko tidak valid.";
                return false;
            }

            lock (TxLock)
            {
                if (!EconomyApi.TryDebit(buyer.Account.Name, totalPrice, $"ServerShop Buy {GetItemName(itemId)} x{quantity}"))
                {
                    long bal = EconomyApi.GetBalance(buyer.Account.Name);
                    message = $"Shard tidak cukup! Total: {EconomyApi.FormatColored(totalPrice)} | Dompetmu: {EconomyApi.FormatColored(bal)}.";
                    return false;
                }

                TryGiveOrQueueItem(buyer, buyer.Account.Name, itemId, quantity, prefix, "Pembelian Server Shop", out bool queued);
                deliveredDirect = !queued;

                ShopModule.Database.LogTransaction(new ShopTransactionLog
                {
                    Kind = "ServerBuy",
                    BuyerAccount = buyer.Account.Name,
                    SellerAccount = "SERVER",
                    ItemId = itemId,
                    Stack = quantity,
                    Prefix = prefix,
                    GrossShards = totalPrice,
                    TaxShards = 0,
                    TimestampUtc = DateTime.UtcNow
                });

                string label = FormatItemLabel(itemId, quantity, prefix);
                if (!RankApi.CanUseItem(buyer, itemId))
                {
                    int reqTier = RankModule.Config.GetItemTier(itemId);
                    var reqRank = RankModule.Config.GetRankForTier(reqTier);
                    buyer.SendWarningMessage($"[ INFO TIER ] {GetItemName(itemId)} adalah Tier {reqTier} (membutuhkan Rank {reqRank.Name} untuk dipakai).");
                }

                message = queued
                    ? $"[ + BELI TOKO ] {label} dibeli seharga {EconomyApi.FormatColored(totalPrice)}! Tas penuh -> simpan ke /ah claim."
                    : $"[ + BELI TOKO ] Berhasil membeli {label} seharga {EconomyApi.FormatColored(totalPrice)}!";
                return true;
            }
        }

        public static bool TrySellHeldToServer(TSPlayer seller, int slot, int? requestedQty, out string message)
        {
            message = string.Empty;
            if (seller == null || !seller.Active || !seller.IsLoggedIn || seller.TPlayer == null)
            {
                message = "Kamu harus login terlebih dahulu.";
                return false;
            }

            if (slot < 0 || slot >= 50)
            {
                message = "Pegang item yang ingin dijual di Hotbar.";
                return false;
            }

            lock (TxLock)
            {
                Item inv = seller.TPlayer.inventory[slot];
                if (inv == null || inv.IsAir || inv.type <= 0 || inv.stack <= 0)
                {
                    message = "Tangan kamu kosong! Pegang item di hotbar untuk dijual.";
                    return false;
                }

                var catalog = ShopModule.Config.GetCatalogItemById(inv.type);
                if (catalog == null || catalog.SellPrice <= 0)
                {
                    message = $"{GetItemName(inv.type)} tidak dibeli oleh Server Shop. Jual ke player lain lewat /ah sell atau Lapak!";
                    return false;
                }

                int qty = requestedQty.HasValue ? Math.Min(requestedQty.Value, inv.stack) : inv.stack;
                if (qty <= 0)
                {
                    message = "Jumlah jual harus lebih dari 0.";
                    return false;
                }

                long payout;
                try
                {
                    payout = checked(catalog.SellPrice * qty);
                }
                catch (OverflowException)
                {
                    message = "Nominal transaksi terlalu besar.";
                    return false;
                }

                if (!TryTakeFromInventorySlot(seller, slot, qty, out ItemSnapshot taken, out string takeErr))
                {
                    message = takeErr;
                    return false;
                }

                EconomyApi.TryCredit(seller.Account.Name, payout, $"ServerShop Sell {taken.Name} x{qty}");

                ShopModule.Database.LogTransaction(new ShopTransactionLog
                {
                    Kind = "ServerSell",
                    BuyerAccount = "SERVER",
                    SellerAccount = seller.Account.Name,
                    ItemId = taken.ItemId,
                    Stack = taken.Stack,
                    Prefix = taken.Prefix,
                    GrossShards = payout,
                    TaxShards = 0,
                    TimestampUtc = DateTime.UtcNow
                });

                message = $"[ + JUAL TOKO ] Menjual {FormatItemLabel(taken.ItemId, taken.Stack, taken.Prefix)} -> +{EconomyApi.FormatColored(payout)}!";
                return true;
            }
        }
    }
}
