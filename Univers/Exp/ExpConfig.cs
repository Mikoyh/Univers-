using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using TShockAPI;
using Univers.Modules;

namespace Univers.Exp
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
        public string ChatFormat { get; set; } = "[i:{BadgeId}] [c/{LevelColor}:[][c/{LevelColor}:{Level}][c/{LevelColor}:]] {Prefix} [c/{LevelColor}:{PlayerName}]: [c/e2e8f0:{Message}]";

        [JsonProperty("Enable Level Up Server Broadcast")]
        public bool EnableLevelUpBroadcast { get; set; } = false;

        [JsonProperty("Enable Level Up Fireworks")]
        public bool EnableLevelUpFireworks { get; set; } = true;

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

        public long CalculateRequiredExp(int level)
        {
            if (level <= 1) return BaseExp;

            int diff = level - 1;

            // FASE 1: Level di bawah 500
            if (level < 500)
            {
                double earlyScale = GrowthScale * 1.8;
                return (long)(BaseExp + (LinearStep * diff) + (earlyScale * Math.Pow(diff, 2.1)));
            }

            // FASE 2: Level 500 sampai 799
            if (level >= 500 && level < 800)
            {
                long expAt500 = (long)(BaseExp + (LinearStep * 499) + ((GrowthScale * 1.8) * Math.Pow(499, 2.1)));
                int diff500 = level - 499;

                return expAt500 + (long)(75000 * diff500 + (GrowthScale * Math.Pow(diff500, 1.2)));
            }

            // FASE 3: Level 800 ke atas
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
            string dir = Path.Combine(TShock.SavePath, "Univers");
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
                var loaded = JsonConvert.DeserializeObject<ExpConfig>(File.ReadAllText(path)) ?? new ExpConfig();
                if (string.IsNullOrWhiteSpace(loaded.ChatFormat) ||
                    loaded.ChatFormat.Contains("[c/{LevelColor}:{Prefix}]", StringComparison.OrdinalIgnoreCase) ||
                    loaded.ChatFormat.Contains("[c/{LevelColor}:[{Level}][c/{LevelColor}:]]", StringComparison.OrdinalIgnoreCase) ||
                    loaded.ChatFormat.Contains("Lv.{Level}", StringComparison.OrdinalIgnoreCase))
                {
                    loaded.ChatFormat = new ExpConfig().ChatFormat;
                    File.WriteAllText(path, JsonConvert.SerializeObject(loaded, Formatting.Indented));
                }
                return loaded;
            }
            catch (Exception ex)
            {
                Log.Error("[Univers.Exp] Gagal membaca ExpConfig: " + ex.Message);
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
