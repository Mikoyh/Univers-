/*
 * =====================================================================
 *  ShopCommands.cs - Command /shop, /ah (/market), dan /shopadmin
 *  Namespace: Univers.Shop (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Skynomi.Economy;
using Terraria;
using TShockAPI;

namespace Univers.Shop
{
    public static class ShopCommands
    {
        private static readonly ConcurrentDictionary<int, (int ListingId, DateTime ExpiresAtUtc)> _ahBuyConfirm = new();

        public static void Register()
        {
            Commands.ChatCommands.RemoveAll(c =>
                c.Names.Contains("shop") ||
                c.Names.Contains("ah") ||
                c.Names.Contains("market") ||
                c.Names.Contains("shopadmin"));

            Commands.ChatCommands.Add(new Command("univers.shop.use", OnShopCommand, "shop")
            {
                HelpText = "Buka Server Shop: /shop <kategori|buy|sell>"
            });

            Commands.ChatCommands.Add(new Command("univers.ah.use", OnAuctionCommand, "ah", "market")
            {
                HelpText = "Pasar & Lelang Player (Offline Support): /ah <sell|buy|search|my|cancel|claim>"
            });

            Commands.ChatCommands.Add(new Command("univers.shop.admin", OnShopAdminCommand, "shopadmin")
            {
                HelpText = "Kelola Showcase Fisik Spawn: /shopadmin <adddisplay|addsellsign|addbooth|removedisplay|list|reload>"
            });
        }

        private static void OnShopCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null) return;

            var p = args.Parameters;
            if (p.Count == 0 || p[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                long bal = player.IsLoggedIn ? EconomyApi.GetBalance(player.Account.Name) : 0;
                var cats = ShopModule.Config.GetCategories();
                player.SendInfoMessage("[ === UNIVERS SERVER SHOP === ]");
                player.SendInfoMessage($"Dompet: {EconomyApi.FormatColored(bal)} | Kategori: {string.Join(", ", cats)}");
                player.SendInfoMessage("Beli cepat   : /shop buy <nama|id> [qty]   | Lihat kategori: /shop <kategori>");
                player.SendInfoMessage("Jual cepat   : /shop sell [qty]            | Atau klik 2x Sign Showcase di Spawn!");
                player.SendInfoMessage("Pasar Player : /ah                         | Lapak Fisik   : Klik Sign [LAPAK KOSONG] di Spawn");
                return;
            }

            string sub = p[0].ToLowerInvariant();

            if (sub == "buy")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Login terlebih dahulu untuk berbelanja.");
                    return;
                }

                if (p.Count < 2)
                {
                    player.SendErrorMessage("Format: /shop buy <nama item / id> [jumlah]");
                    return;
                }

                int qty = 1;
                string query;
                if (p.Count >= 3 && int.TryParse(p[^1], out int parsedQty))
                {
                    qty = parsedQty;
                    query = string.Join(" ", p.Skip(1).Take(p.Count - 2));
                }
                else
                {
                    query = string.Join(" ", p.Skip(1));
                }

                var item = ShopModule.Config.FindCatalogItem(query);
                if (item == null || item.BuyPrice <= 0)
                {
                    player.SendErrorMessage($"Item '{query}' tidak dijual di Server Shop. Cek kategori lewat /shop.");
                    return;
                }

                if (qty <= 0 || qty > 9999)
                {
                    player.SendErrorMessage("Jumlah pembelian harus antara 1 s/d 9999.");
                    return;
                }

                long totalCost;
                try
                {
                    totalCost = checked(item.BuyPrice * qty);
                }
                catch (OverflowException)
                {
                    player.SendErrorMessage("Total harga terlalu besar.");
                    return;
                }

                if (ShopApi.TryBuyServerItem(player, item.ItemId, 0, qty, totalCost, out _, out string msg))
                {
                    player.SendSuccessMessage(msg);
                }
                else
                {
                    player.SendErrorMessage(msg);
                }
                return;
            }

            if (sub == "sell")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Login terlebih dahulu untuk menjual item.");
                    return;
                }

                int? qty = null;
                if (p.Count >= 2)
                {
                    if (!int.TryParse(p[1], out int parsedQty) || parsedQty <= 0)
                    {
                        player.SendErrorMessage("Format: /shop sell [jumlah]");
                        return;
                    }
                    qty = parsedQty;
                }

                int slot = player.TPlayer.selectedItem;
                if (ShopApi.TrySellHeldToServer(player, slot, qty, out string msg))
                {
                    player.SendSuccessMessage(msg);
                }
                else
                {
                    player.SendErrorMessage(msg);
                }
                return;
            }

            // Cek sebagai nama kategori
            var catItems = ShopModule.Config.GetByCategory(sub);
            if (catItems.Count == 0)
            {
                player.SendErrorMessage($"Kategori/subcommand '{sub}' tidak ditemukan. Kategori tersedia: {string.Join(", ", ShopModule.Config.GetCategories())}");
                return;
            }

            int page = 1;
            if (p.Count >= 2 && int.TryParse(p[1], out int parsedPage) && parsedPage > 0)
            {
                page = parsedPage;
            }

            const int perPage = 6;
            int totalPages = Math.Max(1, (int)Math.Ceiling(catItems.Count / (double)perPage));
            page = Math.Clamp(page, 1, totalPages);

            player.SendInfoMessage($"[ TOKO SERVER - {sub.ToUpperInvariant()} (Hal {page}/{totalPages}) ]");
            foreach (var it in catItems.Skip((page - 1) * perPage).Take(perPage))
            {
                string buyStr = it.BuyPrice > 0 ? $"Beli: {EconomyApi.FormatColored(it.BuyPrice)}" : "Beli: -";
                string sellStr = it.SellPrice > 0 ? $"Jual: {EconomyApi.Format(it.SellPrice)}" : "Jual: -";
                player.SendMessage($"{ShopApi.FormatItemTag(it.ItemId)} {it.Name} (#{it.ItemId}) | {buyStr} | {sellStr}", Microsoft.Xna.Framework.Color.White);
            }
            player.SendInfoMessage("Ketik /shop buy <nama|id> [qty] untuk membeli, atau /shop sell sambil memegang item.");
        }

        private static void OnAuctionCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null) return;

            var p = args.Parameters;
            if (p.Count == 0 || int.TryParse(p[0], out _))
            {
                int page = p.Count >= 1 && int.TryParse(p[0], out int pg) && pg > 0 ? pg : 1;
                ShowListings(player, null, page);
                return;
            }

            string sub = p[0].ToLowerInvariant();

            if (sub == "help")
            {
                player.SendInfoMessage("[ === UNIVERS AUCTION HOUSE (/ah) === ]");
                player.SendInfoMessage("/ah [hal]                 - Lihat semua barang yang dijual player");
                player.SendInfoMessage("/ah search <kata kunci>   - Cari barang tertentu");
                player.SendInfoMessage("/ah sell <harga> [qty]    - Jual item di tangan ke pasar (aman saat offline!)");
                player.SendInfoMessage("/ah buy <id>              - Beli barang dari pasar (konfirmasi 2x)");
                player.SendInfoMessage("/ah my                    - Lihat jualan aktifmu");
                player.SendInfoMessage("/ah cancel <id>           - Tarik kembali barang jualanmu");
                player.SendInfoMessage("/ah claim                 - Ambil item tertunda (tas penuh / expired)");
                return;
            }

            if (sub == "search" || sub == "find")
            {
                if (p.Count < 2)
                {
                    player.SendErrorMessage("Format: /ah search <nama item atau penjual>");
                    return;
                }
                string kw = string.Join(" ", p.Skip(1));
                ShowListings(player, kw, 1);
                return;
            }

            if (sub == "sell")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Login terlebih dahulu.");
                    return;
                }

                if (p.Count < 2 || !long.TryParse(p[1].Replace(",", "").Replace(".", ""), out long totalPrice))
                {
                    player.SendErrorMessage("Format: /ah sell <total harga shard> [qty] (sambil memegang item di tangan)");
                    return;
                }

                int slot = player.TPlayer.selectedItem;
                Item inv = slot >= 0 && slot < 50 ? player.TPlayer.inventory[slot] : new Item();
                if (inv == null || inv.IsAir || inv.type <= 0)
                {
                    player.SendErrorMessage("Pegang item di hotbar terlebih dahulu sebelum /ah sell.");
                    return;
                }

                int qty = inv.stack;
                if (p.Count >= 3)
                {
                    if (!int.TryParse(p[2], out qty) || qty <= 0)
                    {
                        player.SendErrorMessage("Jumlah item tidak valid.");
                        return;
                    }
                }

                if (ShopApi.TryCreateListing(player, slot, qty, totalPrice, null, out _, out string msg))
                {
                    player.SendSuccessMessage("[ AH ] " + msg);
                }
                else
                {
                    player.SendErrorMessage("[ AH ] " + msg);
                }
                return;
            }

            if (sub == "buy")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Login terlebih dahulu.");
                    return;
                }

                if (p.Count < 2 || !int.TryParse(p[1], out int listingId))
                {
                    player.SendErrorMessage("Format: /ah buy <listingId>");
                    return;
                }

                bool explicitConfirm = p.Count >= 3 && p[2].Equals("confirm", StringComparison.OrdinalIgnoreCase);
                bool doubleConfirm = false;
                if (_ahBuyConfirm.TryGetValue(player.Index, out var prev) &&
                    prev.ListingId == listingId &&
                    DateTime.UtcNow <= prev.ExpiresAtUtc)
                {
                    doubleConfirm = true;
                }

                if (!explicitConfirm && !doubleConfirm)
                {
                    var preview = ShopModule.Database.GetListingById(listingId);
                    if (preview == null || preview.Status != ListingStatus.Active)
                    {
                        player.SendErrorMessage($"Listing #{listingId} tidak ditemukan atau sudah laku.");
                        return;
                    }

                    _ahBuyConfirm[player.Index] = (listingId, DateTime.UtcNow.AddSeconds(ShopModule.Config.ConfirmTimeoutSeconds));
                    string label = ShopApi.FormatItemLabel(preview.ItemId, preview.Stack, preview.Prefix);
                    player.SendInfoMessage($"[ KONFIRMASI AH #{listingId} ] {label} oleh {preview.SellerAccount} | Harga: {EconomyApi.FormatColored(preview.TotalPrice)}");
                    player.SendInfoMessage($"Ketik ulang '/ah buy {listingId}' dalam {ShopModule.Config.ConfirmTimeoutSeconds} detik untuk MEMBELI.");
                    return;
                }

                _ahBuyConfirm.TryRemove(player.Index, out _);
                if (ShopApi.TryBuyListing(player, listingId, out _, out _, out string msg))
                {
                    player.SendSuccessMessage(msg);
                }
                else
                {
                    player.SendErrorMessage(msg);
                }
                return;
            }

            if (sub == "my")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Login terlebih dahulu.");
                    return;
                }

                var mine = ShopModule.Database.GetActiveListingsBySeller(player.Account.Name);
                int max = ShopApi.GetMaxListingsForPlayer(player);
                player.SendInfoMessage($"[ JUALAN AKTIFMU ({mine.Count}/{max}) ]");
                if (mine.Count == 0)
                {
                    player.SendInfoMessage("Kamu belum punya barang yang dijual. Gunakan /ah sell <harga> atau klik Sign [LAPAK KOSONG] di Spawn.");
                    return;
                }

                foreach (var l in mine)
                {
                    string boothTag = l.BoothDisplayId.HasValue ? $" [Lapak #{l.BoothDisplayId}]" : "";
                    TimeSpan rem = l.ExpiresAtUtc - DateTime.UtcNow;
                    player.SendMessage($"#{l.Id}{boothTag} {ShopApi.FormatItemLabel(l.ItemId, l.Stack, l.Prefix)} | {EconomyApi.FormatColored(l.TotalPrice)} | Sisa {Math.Max(0, (int)rem.TotalHours)}j", Microsoft.Xna.Framework.Color.White);
                }
                return;
            }

            if (sub == "cancel")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Login terlebih dahulu.");
                    return;
                }

                if (p.Count < 2 || !int.TryParse(p[1], out int listingId))
                {
                    player.SendErrorMessage("Format: /ah cancel <listingId>");
                    return;
                }

                bool isAdmin = player.HasPermission("univers.shop.admin");
                if (ShopApi.TryCancelListing(player, listingId, isAdmin, out _, out string msg))
                {
                    player.SendSuccessMessage("[ AH ] " + msg);
                }
                else
                {
                    player.SendErrorMessage("[ AH ] " + msg);
                }
                return;
            }

            if (sub == "claim")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Login terlebih dahulu.");
                    return;
                }

                int claimed = ShopApi.ClaimPendingItems(player, out int remaining);
                if (claimed == 0 && remaining == 0)
                {
                    player.SendInfoMessage("[ AH CLAIM ] Tidak ada item tertunda untuk diambil.");
                }
                else if (remaining > 0)
                {
                    player.SendWarningMessage($"[ AH CLAIM ] Berhasil mengambil {claimed} item! Masih ada {remaining} item tersisa karena tas penuh.");
                }
                else
                {
                    player.SendSuccessMessage($"[ AH CLAIM ] Berhasil mengambil seluruh {claimed} item ke inventory!");
                }
                return;
            }

            player.SendErrorMessage("Subcommand tidak dikenali. Ketik /ah help untuk daftar perintah.");
        }

        private static void ShowListings(TSPlayer player, string? keyword, int page)
        {
            var all = ShopModule.Database.GetActiveListings()
                .Where(x => x.ExpiresAtUtc > DateTime.UtcNow)
                .ToList();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string kw = keyword.Trim();
                all = all.Where(x =>
                    x.ItemName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    x.SellerAccount.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            const int perPage = 6;
            int totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)perPage));
            page = Math.Clamp(page, 1, totalPages);

            string title = string.IsNullOrWhiteSpace(keyword)
                ? $"[ AUCTION HOUSE UNIVERS (Hal {page}/{totalPages}) | Total: {all.Count} ]"
                : $"[ CARI AH: '{keyword}' (Hal {page}/{totalPages}) | Ditemukan: {all.Count} ]";

            player.SendInfoMessage(title);
            if (all.Count == 0)
            {
                player.SendInfoMessage("Belum ada barang yang dijual saat ini. Gunakan /ah sell <harga> atau Lapak Fisik di Spawn!");
                return;
            }

            foreach (var l in all.Skip((page - 1) * perPage).Take(perPage))
            {
                string boothTag = l.BoothDisplayId.HasValue ? $" [Lapak #{l.BoothDisplayId}]" : "";
                player.SendMessage($"#{l.Id}{boothTag} {ShopApi.FormatItemLabel(l.ItemId, l.Stack, l.Prefix)} | {EconomyApi.FormatColored(l.TotalPrice)} | Penjual: {l.SellerAccount}", Microsoft.Xna.Framework.Color.White);
            }
            player.SendInfoMessage("Ketik /ah buy <id> untuk membeli | /ah sell <harga> untuk menjual | /ah help untuk info lengkap.");
        }

        private static void OnShopAdminCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null) return;

            var p = args.Parameters;
            if (p.Count == 0 || p[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                player.SendInfoMessage("[ === UNIVERS SHOPADMIN (SHOWCASE & LAPAK FISIK) === ]");
                player.SendInfoMessage("/shopadmin adddisplay <harga/unit> [qty]  - Pegang item (atau isi Frame), lalu klik Sign");
                player.SendInfoMessage("/shopadmin addsellsign                    - Klik Sign untuk ubah jadi Terminal Jual ke Server");
                player.SendInfoMessage("/shopadmin addbooth                       - Klik Sign untuk ubah jadi [LAPAK KOSONG] Player");
                player.SendInfoMessage("/shopadmin removedisplay                  - Klik Sign untuk melepas Showcase/Lapak");
                player.SendInfoMessage("/shopadmin listdisplays                   - Lihat daftar semua Sign Showcase/Lapak");
                player.SendInfoMessage("/shopadmin reload                         - Muat ulang ShopConfig.json & refresh semua visual");
                return;
            }

            string sub = p[0].ToLowerInvariant();

            if (sub == "adddisplay")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Harus dijalankan oleh admin yang login di dalam game.");
                    return;
                }

                if (p.Count < 2 || !long.TryParse(p[1].Replace(",", "").Replace(".", ""), out long price) || price <= 0)
                {
                    player.SendErrorMessage("Format: /shopadmin adddisplay <harga per unit> [qty]");
                    return;
                }

                int qty = 1;
                if (p.Count >= 3 && (!int.TryParse(p[2], out qty) || qty <= 0))
                {
                    player.SendErrorMessage("Qty harus angka positif.");
                    return;
                }

                int slot = player.TPlayer.selectedItem;
                Item held = slot >= 0 && slot < 50 ? player.TPlayer.inventory[slot] : new Item();
                int itemId = held != null && !held.IsAir ? held.type : 0;
                byte prefix = held != null && !held.IsAir ? held.prefix : (byte)0;

                ShopModule.StartAdminBind(player, PhysicalDisplayType.ServerShop, itemId, prefix, price, qty);
                if (itemId > 0)
                {
                    player.SendSuccessMessage($"[ SHOPADMIN ] Mode pasang Server Showcase aktif ({ShopApi.FormatItemLabel(itemId, qty, prefix)} @ {EconomyApi.Format(price)}/unit).");
                }
                else
                {
                    player.SendSuccessMessage("[ SHOPADMIN ] Mode pasang Server Showcase aktif (akan membaca item dari Item Frame di dekat Sign).");
                }
                player.SendInfoMessage("Sekarang KLIK KANAN SIGN target dalam 30 detik (Item Frame di dekatnya otomatis terdeteksi)!");
                return;
            }

            if (sub == "addsellsign")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Harus dijalankan oleh admin di dalam game.");
                    return;
                }

                ShopModule.StartAdminBind(player, PhysicalDisplayType.ServerSellback, 0, 0, 0, 1);
                player.SendSuccessMessage("[ SHOPADMIN ] Mode pasang Terminal Jual Cepat aktif! KLIK KANAN SIGN target dalam 30 detik.");
                return;
            }

            if (sub == "addbooth")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Harus dijalankan oleh admin di dalam game.");
                    return;
                }

                ShopModule.StartAdminBind(player, PhysicalDisplayType.PlayerBooth, 0, 0, 0, 1);
                player.SendSuccessMessage("[ SHOPADMIN ] Mode pasang Lapak Player Kosong aktif! KLIK KANAN SIGN target dalam 30 detik.");
                return;
            }

            if (sub == "removedisplay")
            {
                if (!player.IsLoggedIn)
                {
                    player.SendErrorMessage("Harus dijalankan oleh admin di dalam game.");
                    return;
                }

                ShopModule.StartAdminRemove(player);
                player.SendWarningMessage("[ SHOPADMIN ] Mode lepas Showcase/Lapak aktif! KLIK KANAN SIGN yang ingin dilepas dalam 30 detik.");
                return;
            }

            if (sub == "listdisplays")
            {
                var all = ShopModule.Database.GetAllDisplays();
                player.SendInfoMessage($"[ SHOPADMIN ] Total Showcase/Lapak Fisik Terpasang: {all.Count}");
                foreach (var d in all)
                {
                    string frameInfo = d.HasFrame ? $"Frame({d.FrameX},{d.FrameY})" : "NoFrame";
                    string detail = d.DisplayType == PhysicalDisplayType.ServerShop
                        ? $"{ShopApi.GetItemName(d.ServerItemId)} x{d.ServerQuantity} @ {EconomyApi.Format(d.ServerPrice)}"
                        : (d.ActiveListingId.HasValue ? $"Terisi Listing #{d.ActiveListingId}" : "Kosong");
                    player.SendMessage($"#{d.Id} [{d.DisplayType}] Sign({d.SignX},{d.SignY}) {frameInfo} -> {detail}", Microsoft.Xna.Framework.Color.White);
                }
                return;
            }

            if (sub == "reload")
            {
                ShopModule.Reload();
                player.SendSuccessMessage("[ SHOPADMIN ] ShopConfig.json berhasil dimuat ulang dan visual Showcase/Lapak disinkronkan!");
                return;
            }

            player.SendErrorMessage("Subcommand tidak dikenali. Ketik /shopadmin help.");
        }
    }
}
