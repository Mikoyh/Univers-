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
        public int BaseExp { get; set; } = 25;

        [JsonProperty("Linear Step Addition")]
        public double LinearStep { get; set; } = 8.0;

        [JsonProperty("Growth Scale Factor")]
        public double GrowthScale { get; set; } = 1.2;

        [JsonProperty("Growth Exponent (Curvature)")]
        public double GrowthExponent { get; set; } = 2.0;

        [JsonProperty("Chat Exp Min")]
        public int ChatExpMin { get; set; } = 3;

        [JsonProperty("Chat Exp Max")]
        public int ChatExpMax { get; set; } = 7;

        [JsonProperty("Chat Exp Cooldown Seconds")]
        public int ChatCooldownSeconds { get; set; } = 15;

        [JsonProperty("Mob Exp Min")]
        public int MobExpMin { get; set; } = 6;

        [JsonProperty("Mob Exp Max")]
        public int MobExpMax { get; set; } = 11;

        [JsonProperty("Boss Exp Multiplier")]
        public double BossExpMultiplier { get; set; } = 30;

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

            // FASE EARLY-MID (Lv 11+): Lv 1-10 tetap santai/cepat, mulai Lv 11+ naik bertahap agar Lv 20-30 terasa menantang
            if (level >= 11)
            {
                int diff11 = level - 10;
                value += (45.0 * diff11) + (12.0 * Math.Pow(diff11, 2.05));
            }

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
            string dir = Path.Combine(UniversPlugin.GetEffectiveSavePath(), "Univers");
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

                // Migrasi kurva EXP lama (terlalu berat maupun terlalu mudah) ke kurva seimbang Lv.1-10 ez & Lv.20-30+ menantang
                bool isOldHeavyCurve = loaded.BaseExp == 35 &&
                    Math.Abs(loaded.LinearStep - 25.0) < 0.01 &&
                    Math.Abs(loaded.GrowthScale - 15.5) < 0.01 &&
                    Math.Abs(loaded.GrowthExponent - 2.0) < 0.01;

                bool isOldEasyCurve = loaded.BaseExp == 20 &&
                    Math.Abs(loaded.LinearStep - 3.5) < 0.01 &&
                    Math.Abs(loaded.GrowthScale - 0.14) < 0.01 &&
                    Math.Abs(loaded.GrowthExponent - 1.75) < 0.01;

                if (isOldHeavyCurve || isOldEasyCurve)
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
                    loaded.BossExpMultiplier = def.BossExpMultiplier;
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
