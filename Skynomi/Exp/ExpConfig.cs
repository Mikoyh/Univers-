using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using TShockAPI;
using Skynomi.Modules;

namespace Skynomi.Exp
{
    public class ExpConfig
    {
        [JsonProperty("Base Exp For Level 1")]
        public int BaseExp { get; set; } = 35;

        [JsonProperty("Linear Step Addition")]
        public double LinearStep { get; set; } = 25.0;

        [JsonProperty("Growth Scale Factor")]
        public double GrowthScale { get; set; } = 15.5;

        [JsonProperty("Growth Exponent (Curvature)")]
        public double GrowthExponent { get; set; } = 2.0;

        [JsonProperty("Chat Exp Min")]
        public int ChatExpMin { get; set; } = 2;

        [JsonProperty("Chat Exp Max")]
        public int ChatExpMax { get; set; } = 8;

        [JsonProperty("Chat Exp Cooldown Seconds")]
        public int ChatCooldownSeconds { get; set; } = 15;

        [JsonProperty("Mob Exp Min")]
        public int MobExpMin { get; set; } = 12;

        [JsonProperty("Mob Exp Max")]
        public int MobExpMax { get; set; } = 25;

        [JsonProperty("Boss Exp Multiplier")]
        public double BossExpMultiplier { get; set; } = 35;

        [JsonProperty("Enable Custom Chat Format")]
        public bool EnableChatFormat { get; set; } = true;

        [JsonProperty("Chat Format Template")]
        public string ChatFormat { get; set; } = "[i:{BadgeId}] [c/{LevelColor}:[{Level}][c/{LevelColor}:]] [c/{LevelColor}:{Prefix}] [c/{LevelColor}:{PlayerName}]: [c/e2e8f0:{Message}]";

        [JsonProperty("Enable Level Up Server Broadcast")]
        public bool EnableLevelUpBroadcast { get; set; } = true;

        [JsonProperty("Level Badge Tiers")]
        public Dictionary<int, LevelBadgeConfig> BadgeTiers { get; set; } = new Dictionary<int, LevelBadgeConfig>
        {
            { 1, new LevelBadgeConfig { BadgeItemId = 3507, HexColor = "94a3b8", Title = "Copper Broadsword (Lv.1-99)" } },
            { 100, new LevelBadgeConfig { BadgeItemId = 273, HexColor = "a855f7", Title = "Night's Edge (Lv.100-249)" } },
            { 250, new LevelBadgeConfig { BadgeItemId = 757, HexColor = "22c55e", Title = "Terra Blade (Lv.250-499)" } },
            { 500, new LevelBadgeConfig { BadgeItemId = 3456, HexColor = "f59e0b", Title = "Solar Fragment (Lv.500-749)" } },
            { 750, new LevelBadgeConfig { BadgeItemId = 3063, HexColor = "ec4899", Title = "Meowmere (Lv.750-949)" } },
            { 950, new LevelBadgeConfig { BadgeItemId = 4956, HexColor = "06b6d4", Title = "Zenith Apex (Lv.950-1000)" } }
        };

        /// <summary>
        /// Formula EXP Seimbang Skala Level 1 s/d 1000 (10x Lipat Lebih Granular):
        /// - Level 1: 5 EXP (bunuh 1 green slime langsung Lv. 2)
        /// - Level 100 (Rank 1): 213 EXP (1x Eye of Cthulhu / King Slime langsung naik 5-8 level)
        /// - Level 200 (Rank 2): 442 EXP (1x Boss Pre-Hardmode memberi ~800 EXP = 2x Level Up)
        /// - Level 350 (Rank 3): 798 EXP (Pembukaan Tag Elemen & Large Gem)
        /// - Level 500 (Rank 4): 1.185 EXP (Pembukaan Reaksi Kombo Dua Elemen)
        /// - Level 1000 (Apex Cap): 2.575 EXP (Puncak Level Karakter Maksimum Server)
        /// </summary>
public long CalculateRequiredExp(int level)
{
    if (level <= 1) return BaseExp;

    int diff = level - 1;

    // FASE 1: Level di bawah 500 (Sangat Make Sense & Flow Mengalir)
    if (level < 500)
    {
        double earlyScale = GrowthScale * 1.8;
        return (long)(BaseExp + (LinearStep * diff) + (earlyScale * Math.Pow(diff, 2.1)));
    }

    // FASE 2: Level 500 sampai 799 (Target ~2 Hari Grind Aktif per Level)
    // Asumsi per hari player aktif berburu menghasilkan ~30.000 - 40.000 EXP murni (Rasio 1/1000 HP)
    if (level >= 500 && level < 800)
    {
        // Kita kunci kebutuhan EXP di kisaran 75.000 - 90.000 EXP per level
        long expAt500 = (long)(BaseExp + (LinearStep * 499) + ((GrowthScale * 1.8) * Math.Pow(499, 2.1)));
        int diff500 = level - 499;
        
        return expAt500 + (long)(75000 * diff500 + (GrowthScale * Math.Pow(diff500, 1.2)));
    }

    // FASE 3: Level 800 ke atas (Target ~7 Hari Grind Brutal per Level)
    // Kebutuhan EXP melonjak ke kisaran 250.000 - 280.000 EXP per level
    long expAt800 = (long)(BaseExp + (LinearStep * 499) + ((GrowthScale * 1.8) * Math.Pow(499, 2.1)));
    long cumulativeAt800 = expAt800 + (long)(75000 * 300 + (GrowthScale * Math.Pow(300, 1.2)));
    int diff800 = level - 799;

    return cumulativeAt800 + (long)(260000 * diff800 + (GrowthScale * 5.0 * Math.Pow(diff800, 1.5)));
}



        public int GetBadgeIdForLevel(int level)
        {
            int selected = 3507;
            foreach (var kvp in BadgeTiers)
            {
                if (level >= kvp.Key) selected = kvp.Value.BadgeItemId;
            }
            return selected;
        }

        public string GetLevelColorForLevel(int level)
        {
            string col = "94a3b8";
            foreach (var kvp in BadgeTiers)
            {
                if (level >= kvp.Key) col = kvp.Value.HexColor;
            }
            return col;
        }

        public static ExpConfig Read()
        {
            string dir = Path.Combine(TShock.SavePath, "Skynomi");
            string path = Path.Combine(dir, "ExpConfig.json");
            Directory.CreateDirectory(dir);

            try
            {
                if (!File.Exists(path))
                {
                    var def = new ExpConfig();
                    File.WriteAllText(path, JsonConvert.SerializeObject(def, Formatting.Indented));
                    return def;
                }
                return JsonConvert.DeserializeObject<ExpConfig>(File.ReadAllText(path)) ?? new ExpConfig();
            }
            catch (Exception ex)
            {
                Log.Error("[Skynomi.Exp] Gagal membaca ExpConfig: " + ex.Message);
                return new ExpConfig();
            }
        }
    }

    public class LevelBadgeConfig
    {
        public int BadgeItemId { get; set; }
        public string HexColor { get; set; } = "ffffff";
        public string Title { get; set; } = "Badge";
    }
}
