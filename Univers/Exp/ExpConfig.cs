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
        public int BaseExp { get; set; } = 20;

        [JsonProperty("Linear Step Addition")]
        public double LinearStep { get; set; } = 3.5;

        [JsonProperty("Growth Scale Factor")]
        public double GrowthScale { get; set; } = 0.14;

        [JsonProperty("Growth Exponent (Curvature)")]
        public double GrowthExponent { get; set; } = 1.75;

        [JsonProperty("Chat Exp Min")]
        public int ChatExpMin { get; set; } = 3;

        [JsonProperty("Chat Exp Max")]
        public int ChatExpMax { get; set; } = 8;

        [JsonProperty("Chat Exp Cooldown Seconds")]
        public int ChatCooldownSeconds { get; set; } = 15;

        [JsonProperty("Mob Exp Min")]
        public int MobExpMin { get; set; } = 8;

        [JsonProperty("Mob Exp Max")]
        public int MobExpMax { get; set; } = 14;

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
            if (level <= 1) return Math.Max(1, BaseExp);

            int diff = level - 1;
            double value = BaseExp + (LinearStep * diff) + (GrowthScale * Math.Pow(diff, GrowthExponent));

            // FASE MID-LATE (Lv 500+): Tambahan kurva bertahap selaras Rank Stellar-Astral
            if (level >= 500)
            {
                int diff500 = level - 499;
                value += (180.0 * diff500) + (4.5 * Math.Pow(diff500, 1.45));
            }

            // FASE ENDGAME (Lv 800-1000): Tambahan kurva bertahap selaras Rank Cosmic-Univers
            if (level >= 800)
            {
                int diff800 = level - 799;
                value += (900.0 * diff800) + (18.0 * Math.Pow(diff800, 1.5));
            }

            return Math.Max(1L, (long)Math.Floor(value));
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
                bool changed = false;

                if (string.IsNullOrWhiteSpace(loaded.ChatFormat) ||
                    loaded.ChatFormat.Contains("[c/{LevelColor}:{Prefix}]", StringComparison.OrdinalIgnoreCase) ||
                    loaded.ChatFormat.Contains("[c/{LevelColor}:[{Level}][c/{LevelColor}:]]", StringComparison.OrdinalIgnoreCase) ||
                    loaded.ChatFormat.Contains("Lv.{Level}", StringComparison.OrdinalIgnoreCase))
                {
                    loaded.ChatFormat = new ExpConfig().ChatFormat;
                    changed = true;
                }

                // Migrasi kurva EXP lama yang terlalu berat (Lv.30 = ~302k EXP) ke kurva baru (~2.5k EXP)
                if (loaded.BaseExp == 35 &&
                    Math.Abs(loaded.LinearStep - 25.0) < 0.01 &&
                    Math.Abs(loaded.GrowthScale - 15.5) < 0.01 &&
                    Math.Abs(loaded.GrowthExponent - 2.0) < 0.01)
                {
                    var def = new ExpConfig();
                    loaded.BaseExp = def.BaseExp;
                    loaded.LinearStep = def.LinearStep;
                    loaded.GrowthScale = def.GrowthScale;
                    loaded.GrowthExponent = def.GrowthExponent;
                    loaded.ChatExpMin = def.ChatExpMin;
                    loaded.ChatExpMax = def.ChatExpMax;
                    loaded.MobExpMin = def.MobExpMin;
                    loaded.MobExpMax = def.MobExpMax;
                    changed = true;
                }

                if (changed)
                {
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
