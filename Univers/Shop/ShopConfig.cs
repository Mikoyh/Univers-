/*
 * =====================================================================
 *  ShopConfig.cs - Konfigurasi Katalog Server Shop, Pajak & Auction House
 *  Namespace: Univers.Shop (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace Univers.Shop
{
    public class ShopCatalogItem
    {
        public int ItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "utility";
        public long BuyPrice { get; set; } = 0;
        public long SellPrice { get; set; } = 0;
    }

    public class ShopConfig
    {
        public double ShopTaxPercent { get; set; } = 5.0;
        public int ListingDurationHours { get; set; } = 48;
        public long ListingFeeShards { get; set; } = 0;
        public long MinListingPrice { get; set; } = 10;
        public long MaxListingPrice { get; set; } = 100_000_000;
        public int ConfirmTimeoutSeconds { get; set; } = 6;
        public bool AllowSelfBuyForTesting { get; set; } = false;

        public Dictionary<int, int> MaxListingsPerRank { get; set; } = new()
        {
            { 0, 2 },
            { 1, 3 },
            { 2, 4 },
            { 3, 6 },
            { 4, 8 },
            { 5, 10 },
            { 6, 12 },
            { 7, 15 },
            { 8, 20 }
        };

        public List<ShopCatalogItem> CatalogItems { get; set; } = new()
        {
            // Potions
            new ShopCatalogItem { ItemId = ItemID.LesserHealingPotion, Name = "Lesser Healing Potion", Category = "potions", BuyPrice = 15, SellPrice = 3 },
            new ShopCatalogItem { ItemId = ItemID.HealingPotion, Name = "Healing Potion", Category = "potions", BuyPrice = 40, SellPrice = 8 },
            new ShopCatalogItem { ItemId = ItemID.GreaterHealingPotion, Name = "Greater Healing Potion", Category = "potions", BuyPrice = 120, SellPrice = 25 },
            new ShopCatalogItem { ItemId = ItemID.SuperHealingPotion, Name = "Super Healing Potion", Category = "potions", BuyPrice = 350, SellPrice = 70 },
            new ShopCatalogItem { ItemId = ItemID.IronskinPotion, Name = "Ironskin Potion", Category = "potions", BuyPrice = 85, SellPrice = 18 },
            new ShopCatalogItem { ItemId = ItemID.RegenerationPotion, Name = "Regeneration Potion", Category = "potions", BuyPrice = 80, SellPrice = 16 },
            new ShopCatalogItem { ItemId = ItemID.SwiftnessPotion, Name = "Swiftness Potion", Category = "potions", BuyPrice = 75, SellPrice = 15 },
            new ShopCatalogItem { ItemId = ItemID.SpelunkerPotion, Name = "Spelunker Potion", Category = "potions", BuyPrice = 150, SellPrice = 30 },
            new ShopCatalogItem { ItemId = ItemID.MiningPotion, Name = "Mining Potion", Category = "potions", BuyPrice = 110, SellPrice = 22 },

            // Ammo
            new ShopCatalogItem { ItemId = ItemID.WoodenArrow, Name = "Wooden Arrow", Category = "ammo", BuyPrice = 2, SellPrice = 1 },
            new ShopCatalogItem { ItemId = ItemID.MusketBall, Name = "Musket Ball", Category = "ammo", BuyPrice = 3, SellPrice = 1 },

            // Utility
            new ShopCatalogItem { ItemId = ItemID.Torch, Name = "Torch", Category = "utility", BuyPrice = 3, SellPrice = 1 },
            new ShopCatalogItem { ItemId = ItemID.Rope, Name = "Rope", Category = "utility", BuyPrice = 4, SellPrice = 1 },
            new ShopCatalogItem { ItemId = ItemID.Bomb, Name = "Bomb", Category = "utility", BuyPrice = 35, SellPrice = 7 },
            new ShopCatalogItem { ItemId = ItemID.Dynamite, Name = "Dynamite", Category = "utility", BuyPrice = 140, SellPrice = 30 },

            // Materials & Drops
            new ShopCatalogItem { ItemId = ItemID.Wood, Name = "Wood", Category = "materials", BuyPrice = 4, SellPrice = 1 },
            new ShopCatalogItem { ItemId = ItemID.StoneBlock, Name = "Stone Block", Category = "materials", BuyPrice = 4, SellPrice = 1 },
            new ShopCatalogItem { ItemId = ItemID.Gel, Name = "Gel", Category = "materials", BuyPrice = 10, SellPrice = 2 },
            new ShopCatalogItem { ItemId = ItemID.Lens, Name = "Lens", Category = "materials", BuyPrice = 45, SellPrice = 12 },
            new ShopCatalogItem { ItemId = ItemID.CopperOre, Name = "Copper Ore", Category = "materials", BuyPrice = 20, SellPrice = 6 },
            new ShopCatalogItem { ItemId = ItemID.Obsidian, Name = "Obsidian", Category = "materials", BuyPrice = 60, SellPrice = 15 },
            new ShopCatalogItem { ItemId = ItemID.FallenStar, Name = "Fallen Star", Category = "materials", BuyPrice = 120, SellPrice = 35 },
            new ShopCatalogItem { ItemId = ItemID.LifeCrystal, Name = "Life Crystal", Category = "materials", BuyPrice = 1500, SellPrice = 400 },
            new ShopCatalogItem { ItemId = ItemID.ManaCrystal, Name = "Mana Crystal", Category = "materials", BuyPrice = 450, SellPrice = 100 },

            // Boss Summons
            new ShopCatalogItem { ItemId = ItemID.SlimeCrown, Name = "Slime Crown", Category = "boss", BuyPrice = 650, SellPrice = 120 },
            new ShopCatalogItem { ItemId = ItemID.SuspiciousLookingEye, Name = "Suspicious Looking Eye", Category = "boss", BuyPrice = 900, SellPrice = 180 },
            new ShopCatalogItem { ItemId = ItemID.WormFood, Name = "Worm Food", Category = "boss", BuyPrice = 1400, SellPrice = 250 },
            new ShopCatalogItem { ItemId = ItemID.BloodySpine, Name = "Bloody Spine", Category = "boss", BuyPrice = 1400, SellPrice = 250 },
            new ShopCatalogItem { ItemId = ItemID.Abeemination, Name = "Abeemination", Category = "boss", BuyPrice = 2200, SellPrice = 400 }
        };

        public static ShopConfig Load(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<ShopConfig>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
                }

                var def = new ShopConfig();
                def.Normalize();
                def.Save(path);
                return def;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.Shop] Gagal memuat ShopConfig.json: {ex.Message}");
                return new ShopConfig();
            }
        }

        public void Save(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.Shop] Gagal menyimpan ShopConfig.json: {ex.Message}");
            }
        }

        public void Normalize()
        {
            ShopTaxPercent = Math.Clamp(ShopTaxPercent, 0.0, 50.0);
            ListingDurationHours = Math.Clamp(ListingDurationHours, 1, 720);
            ListingFeeShards = Math.Max(0, ListingFeeShards);
            MinListingPrice = Math.Max(1, MinListingPrice);
            MaxListingPrice = Math.Max(MinListingPrice, MaxListingPrice);
            ConfirmTimeoutSeconds = Math.Clamp(ConfirmTimeoutSeconds, 2, 30);

            CatalogItems ??= new List<ShopCatalogItem>();
            foreach (var item in CatalogItems)
            {
                item.Category = string.IsNullOrWhiteSpace(item.Category) ? "utility" : item.Category.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(item.Name) && item.ItemId > 0)
                {
                    item.Name = Lang.GetItemNameValue(item.ItemId);
                }
            }
        }

        public ShopCatalogItem? GetCatalogItemById(int itemId)
        {
            return CatalogItems.FirstOrDefault(x => x.ItemId == itemId);
        }

        public ShopCatalogItem? FindCatalogItem(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            string q = query.Trim();

            if (int.TryParse(q, out int id))
            {
                return GetCatalogItemById(id);
            }

            var exact = CatalogItems.FirstOrDefault(x => x.Name.Equals(q, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var prefix = CatalogItems.FirstOrDefault(x => x.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase));
            if (prefix != null) return prefix;

            return CatalogItems.FirstOrDefault(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetCategories()
        {
            return CatalogItems
                .Select(x => x.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        public List<ShopCatalogItem> GetByCategory(string category)
        {
            string cat = category.Trim().ToLowerInvariant();
            return CatalogItems
                .Where(x => x.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
