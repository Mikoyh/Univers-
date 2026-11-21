using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;
using Univers;

namespace Skynomi.Ranks
{
    public class RankEntry
    {
        public int Index { get; set; } = 0;
        public string Id { get; set; } = "Mortal";
        public string Name { get; set; } = "Mortal";
        public string Prefix { get; set; } = "[c/E2E8F0:[][c/CCD4E0:m][c/B5C1D0:o][c/9FADC0:r][c/8D9CB2:t][c/7F8FA5:a][c/728198:l][c/64748B:]]";
        public int MinLevel { get; set; } = 1;
        public int MaxLevelCap { get; set; } = 30;
        public int MinPlaytimeMinutes { get; set; } = 0;
        public List<string> RequiredBosses { get; set; } = new List<string>();
        public int MaxItemTierAllowed { get; set; } = 0;
        public int MinJobsCompleted { get; set; } = 0;
        public int MinElementTags { get; set; } = 0;
        public int MinElementCombos { get; set; } = 0;
        public double ExpMultiplier { get; set; } = 1.0;
        public long RankUpCostShards { get; set; } = 250;
        public long MaxMoneyLimitShards { get; set; } = 1500;

        public long RankUpCostCredits { get => RankUpCostShards; set => RankUpCostShards = value; }
        public long MaxMoneyLimitCredits { get => MaxMoneyLimitShards; set => MaxMoneyLimitShards = value; }
        public long RankUpCostCopper { get => RankUpCostShards; set => RankUpCostShards = value; }
        public long MaxMoneyLimitCopper { get => MaxMoneyLimitShards; set => MaxMoneyLimitShards = value; }
        public List<string> Benefits { get; set; } = new List<string>();
        public List<string> Constraints { get; set; } = new List<string>();
        public string Description { get; set; } = "Pemain pemula fana.";
    }

    public class RankConfig
    {
        [JsonProperty("Enable Rank System")]
        public bool EnableRankSystem { get; set; } = true;

        [JsonProperty("Enable Item Tier Restriction")]
        public bool EnableItemLock { get; set; } = true;

        [JsonProperty("Enable Wallet Balance Cap Per Rank")]
        public bool EnableWalletCap { get; set; } = true;

        [JsonProperty("Broadcast When Player Ranks Up")]
        public bool BroadcastRankUp { get; set; } = true;

        [JsonProperty("Cosmic Ranks List")]
        public List<RankEntry> Ranks { get; set; } = new List<RankEntry>
        {
            new RankEntry
            {
                Index = 0, Id = "Mortal", Name = "Mortal", Prefix = "[c/E2E8F0:[][c/CCD4E0:m][c/B5C1D0:o][c/9FADC0:r][c/8D9CB2:t][c/7F8FA5:a][c/728198:l][c/64748B:]]",
                MinLevel = 1, MaxLevelCap = 30, MinPlaytimeMinutes = 0,
                RequiredBosses = new List<string>(), MaxItemTierAllowed = 0, MinJobsCompleted = 0, MinElementTags = 0, MinElementCombos = 0,
                ExpMultiplier = 1.0, RankUpCostShards = 250, MaxMoneyLimitShards = 1500,
                Benefits = new List<string> { "Item Tier 0 (Starter Wood & Copper)", "Akses 1 Job Dasar", "Cap Lv 30 (Adaptasi Cepat)" },
                Constraints = new List<string> { "Batas Dompet 1,500 Shard", "Item Tier 1+ Terkunci" },
                Description = "Manusia fana biasa yang baru tiba di semesta Terraria. Level progression dimulai mudah (Cap Lv. 30)."
            },
            new RankEntry
            {
                Index = 1, Id = "Awakened", Name = "Awakened", Prefix = "[c/A7F3D0:[][c/8DECC4:a][c/74E5B8:w][c/5ADEAB:a][c/41D79F:k][c/2FCC94:e][c/24BF89:n][c/1AB17E:e][c/0FA474:d][c/059669:]]",
                MinLevel = 30, MaxLevelCap = 75, MinPlaytimeMinutes = 20,
                RequiredBosses = new List<string> { "KingSlime", "EyeOfCthulhu" }, MaxItemTierAllowed = 1, MinJobsCompleted = 3, MinElementTags = 0, MinElementCombos = 0,
                ExpMultiplier = 1.15, RankUpCostShards = 800, MaxMoneyLimitShards = 5000,
                Benefits = new List<string> { "Ritual Elemen Bangkit di Tutorial", "Item Tier 1", "+15% EXP", "Cap Lv 75" },
                Constraints = new List<string> { "Batas Dompet 5,000 Shard", "Item Tier 2+ Terkunci" },
                Description = "Prajurit yang jiwanya telah terbangun dan menerima elemen jiwanya."
            },
            new RankEntry
            {
                Index = 2, Id = "Transcendant", Name = "Transcendant", Prefix = "[c/67E8F9:[][c/60E1F9:t][c/59DBF9:r][c/51D4F9:a][c/4ACEF8:n][c/43C7F8:s][c/3CC0F8:c][c/34B9F4:e][c/2CB0ED:n][c/23A7E5:d][c/1B9EDE:a][c/1396D6:n][c/0A8DCF:t][c/0284C7:]]",
                MinLevel = 75, MaxLevelCap = 160, MinPlaytimeMinutes = 60,
                RequiredBosses = new List<string> { "KingSlime", "EyeOfCthulhu", "Deerclops", "EaterOfWorldsOrBrain", "QueenBee" }, MaxItemTierAllowed = 2, MinJobsCompleted = 10, MinElementTags = 0, MinElementCombos = 0,
                ExpMultiplier = 1.35, RankUpCostShards = 3500, MaxMoneyLimitShards = 20000,
                Benefits = new List<string> { "Item Tier 2", "+35% EXP", "Diskon Cooldown Job -30s", "Cap Lv 160" },
                Constraints = new List<string> { "Batas Dompet 20,000 Shard", "Item Tier 3+ Terkunci" },
                Description = "Penjelajah yang melampaui batasan mortal dan menundukkan ekosistem permukaan."
            },
            new RankEntry
            {
                Index = 3, Id = "Ascendant", Name = "Ascendant", Prefix = "[c/A5B4FC:[][c/9EACFB:a][c/97A4FA:s][c/8F9CFA:c][c/8894F9:e][c/818CF8:n][c/777EF4:d][c/6D70F0:a][c/6362ED:n][c/5954E9:t][c/4F46E5:]]",
                MinLevel = 160, MaxLevelCap = 300, MinPlaytimeMinutes = 180,
                RequiredBosses = new List<string> { "KingSlime", "EyeOfCthulhu", "Deerclops", "EaterOfWorldsOrBrain", "QueenBee", "Skeletron", "WallOfFlesh" }, MaxItemTierAllowed = 3, MinJobsCompleted = 25, MinElementTags = 10, MinElementCombos = 0,
                ExpMultiplier = 1.60, RankUpCostShards = 12000, MaxMoneyLimitShards = 60000,
                Benefits = new List<string> { "[TAG BUKA] Buka Fitur Element Tag & Large Gems Display", "Item Tier 3", "+60% EXP", "Cap Lv 300" },
                Constraints = new List<string> { "Batas Dompet 60,000 Shard", "Kombo 2 Elemen Belum Buka" },
                Description = "Ksatria agung pembasmi Wall of Flesh pembuka fenomena Large Gem di kepala."
            },
            new RankEntry
            {
                Index = 4, Id = "Aether", Name = "Aether", Prefix = "[c/E9D5FF:[][c/DDBEFE:a][c/D2A7FD:e][c/C690FC:t][c/B776F5:h][c/A45AE8:e][c/913EDB:r][c/7E22CE:]]",
                MinLevel = 300, MaxLevelCap = 500, MinPlaytimeMinutes = 480,
                RequiredBosses = new List<string> { "WallOfFlesh", "QueenSlime", "TheTwins", "TheDestroyer" }, MaxItemTierAllowed = 4, MinJobsCompleted = 45, MinElementTags = 25, MinElementCombos = 15,
                ExpMultiplier = 1.95, RankUpCostShards = 40000, MaxMoneyLimitShards = 150000,
                Benefits = new List<string> { "[KOMBO BUKA] Buka Reaksi Kombo Dual-Element", "Item Tier 4", "+95% EXP", "Cap Lv 500" },
                Constraints = new List<string> { "Batas Dompet 150,000 Shard", "Item Post-Plantera Terkunci" },
                Description = "Penyelaras dimensi Aether yang mampu memicu reaksi kombo dual-element."
            },
            new RankEntry
            {
                Index = 5, Id = "Stellar", Name = "Stellar", Prefix = "[c/FEF08A:[][c/FDE76D:s][c/FCDE50:t][c/FBD532:e][c/FACC15:l][c/F6AF13:l][c/F29211:a][c/EE750E:r][c/EA580C:]]",
                MinLevel = 500, MaxLevelCap = 720, MinPlaytimeMinutes = 1200,
                RequiredBosses = new List<string> { "SkeletronPrime", "Plantera", "Pumpking", "MourningWood" }, MaxItemTierAllowed = 5, MinJobsCompleted = 80, MinElementTags = 60, MinElementCombos = 35,
                ExpMultiplier = 2.35, RankUpCostShards = 95000, MaxMoneyLimitShards = 400000,
                Benefits = new List<string> { "[MULTI-JOB BUKA] Bisa Jalankan 2 Jobs Sekaligus", "Item Tier 5", "+135% EXP", "Cap Lv 720" },
                Constraints = new List<string> { "Batas Dompet 400,000 Shard", "Item Tier 6+ Terkunci" },
                Description = "Pahlawan bertabur cahaya bintang Stellar yang menguasai dua profesi pekerjaan sekaligus."
            },
            new RankEntry
            {
                Index = 6, Id = "Astral", Name = "Astral", Prefix = "[c/FBCFE8:[][c/FBB4CC:a][c/FB99AF:s][c/FB7E93:t][c/F7657C:r][c/F04D6B:a][c/E83559:l][c/E11D48:]]",
                MinLevel = 720, MaxLevelCap = 880, MinPlaytimeMinutes = 3000,
                RequiredBosses = new List<string> { "Golem", "DukeFishron", "EmpressOfLight", "MartianSaucer", "Betsy" }, MaxItemTierAllowed = 6, MinJobsCompleted = 130, MinElementTags = 120, MinElementCombos = 60,
                ExpMultiplier = 2.90, RankUpCostShards = 220000, MaxMoneyLimitShards = 1000000,
                Benefits = new List<string> { "Job Cooldown -50%", "Item Tier 6", "+190% EXP", "Cap Lv 880" },
                Constraints = new List<string> { "Batas Dompet 1,000,000 Shard", "Item Lunar Terkunci" },
                Description = "Penakluk dimensi astral yang menundukkan monster laut purba Duke Fishron dan Ratu Cahaya."
            },
            new RankEntry
            {
                Index = 7, Id = "Cosmic", Name = "Cosmic", Prefix = "[c/F472B6:[] [c/E876C6:[] [c/DD7AD5:c][c/D17EE5:o][c/C682F4:s][c/B18AFC:m][c/9397FB:i][c/74A4FA:c] [c/56B0F9:]] [c/38BDF8:]]",
                MinLevel = 880, MaxLevelCap = 980, MinPlaytimeMinutes = 7200,
                RequiredBosses = new List<string> { "LunaticCultist", "CelestialPillars", "MoonLord", "IceQueen" }, MaxItemTierAllowed = 7, MinJobsCompleted = 200, MinElementTags = 250, MinElementCombos = 120,
                ExpMultiplier = 3.75, RankUpCostShards = 650000, MaxMoneyLimitShards = 3000000,
                Benefits = new List<string> { "[TRIPLE-JOB BUKA] Bisa Jalankan 3 Jobs Sekaligus", "Item Tier 7 (Zenith Allowed)", "+275% EXP", "Cap Lv 980" },
                Constraints = new List<string> { "Batas Dompet 3,000,000 Shard", "Kasta Univers Belum Terbuka" },
                Description = "Penguasa tata surya kosmik pemegang pedang Zenith dan zirah Luminite pilar celestial."
            },
            new RankEntry
            {
                Index = 8, Id = "Univers", Name = "Univers", Prefix = " [c/52FFFF:[] [c/3BC2FF:[] [c/2484FF:u][c/1965FF:n][c/0D47FF:i][c/0228FF:v][c/0C22FD:e][c/161DFC:r][c/2017FA:s] [c/340BF6:]] [c/4800F3:]] ",
                MinLevel = 980, MaxLevelCap = 1000, MinPlaytimeMinutes = 21600,
                RequiredBosses = new List<string> { "DaytimeEmpressOfLight", "MoonLord", "CelestialPillars", "DukeFishron", "Betsy", "MartianSaucer", "Plantera", "Golem", "TheTwins", "TheDestroyer", "SkeletronPrime" }, MaxItemTierAllowed = 8, MinJobsCompleted = 280, MinElementTags = 400, MinElementCombos = 200,
                ExpMultiplier = 5.0, RankUpCostShards = 2000000, MaxMoneyLimitShards = 10000000,
                Benefits = new List<string> { "[PUNCAK LEVEL 1000] Kasta Tertinggi Univers", "Item Tier 8 (Terraprisma)", "Instant Job Delay 0m", "Batas Dompet 10,000,000 Shard", "Resonansi Elemen Max 25%" },
                Constraints = new List<string> { "Kasta Puncak Berdaulat: Tidak ada batasan lagi." },
                Description = "KASTA TERTINGGI SERVER UNIVERS! Pemilik kedaulatan mutlak atas semesta Terraria."
            }
        };

        [JsonProperty("Item Tier Overrides (ItemId -> TierNumber)")]
        public Dictionary<int, int> ItemTierOverrides { get; set; } = new Dictionary<int, int>
        {
            { 3507, 0 }, { 1, 1 }, { 98, 1 }, { 127, 2 }, { 65, 3 }, { 121, 3 },
            { 492, 4 }, { 534, 4 }, { 757, 5 }, { 1326, 5 }, { 370, 6 }, { 4956, 7 }, { 5005, 8 }
        };

        public int GetItemTier(int itemId)
        {
            if (ItemTierOverrides.TryGetValue(itemId, out int tier)) return tier;
            return 0;
        }

        public RankEntry GetRankByIndex(int index)
        {
            return Ranks.FirstOrDefault(r => r.Index == index) ?? Ranks[0];
        }

        public RankEntry GetRankForTier(int tier)
        {
            return Ranks.FirstOrDefault(r => r.MaxItemTierAllowed >= tier) ?? Ranks.Last();
        }

        public static RankConfig Read()
        {
            string path = Path.Combine(UniversPlugin.GetEffectiveSavePath(), "Skynomi", "RankConfig.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var defaultCfg = new RankConfig();
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<RankConfig>(File.ReadAllText(path)) ?? new RankConfig();
                    bool upgraded = false;

                    foreach (var rank in loaded.Ranks)
                    {
                        var defRank = defaultCfg.Ranks.FirstOrDefault(r => r.Index == rank.Index || string.Equals(r.Id, rank.Id, StringComparison.OrdinalIgnoreCase));
                        if (defRank != null)
                        {
                            if (!string.IsNullOrEmpty(rank.Name) && rank.Name.StartsWith("Rank ", StringComparison.OrdinalIgnoreCase))
                            {
                                int colonIdx = rank.Name.IndexOf(':');
                                if (colonIdx > 0 && colonIdx + 1 < rank.Name.Length)
                                {
                                    rank.Name = rank.Name.Substring(colonIdx + 1).Trim();
                                    upgraded = true;
                                }
                            }

                            string trimmedPrefix = rank.Prefix?.Trim() ?? "";
                            bool isOldSimplePrefix =
                                string.IsNullOrEmpty(trimmedPrefix) ||
                                trimmedPrefix == "[Mortal]" ||
                                trimmedPrefix == "[Awakened]" ||
                                trimmedPrefix == "[Transcendant]" ||
                                trimmedPrefix == "[Ascendant]" ||
                                trimmedPrefix == "[Aether]" ||
                                trimmedPrefix == "[Stellar]" ||
                                trimmedPrefix == "[Astral]" ||
                                trimmedPrefix == "[Cosmic]" ||
                                trimmedPrefix == "[Univers]" ||
                                trimmedPrefix == "[c/64748B:[][c/CBD5E1:Mortal][c/64748B:]]" ||
                                trimmedPrefix == "[c/059669:[][c/34D399:Awakened][c/059669:]]" ||
                                trimmedPrefix == "[c/0284C7:[][c/38BDF8:Transcendant][c/0284C7:]]" ||
                                trimmedPrefix == "[c/4F46E5:[][c/818CF8:Ascendant][c/4F46E5:]]" ||
                                trimmedPrefix == "[c/9333EA:[][c/C084FC:Aether][c/9333EA:]]" ||
                                trimmedPrefix == "[c/D97706:[][c/FDE047:Stellar][c/D97706:]]" ||
                                trimmedPrefix == "[c/E11D48:[][c/FB7185:Astral][c/E11D48:]]" ||
                                trimmedPrefix == "[c/A855F7:[][c/E879F9:C][c/F472B6:o][c/FB7185:s][c/F472B6:m][c/E879F9:i][c/C084FC:c][c/A855F7:]]";

                            if (isOldSimplePrefix)
                            {
                                rank.Prefix = defRank.Prefix;
                                upgraded = true;
                            }
                        }
                    }

                    if (upgraded)
                    {
                        File.WriteAllText(path, JsonConvert.SerializeObject(loaded, Formatting.Indented));
                    }

                    return loaded;
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(defaultCfg, Formatting.Indented));
                return defaultCfg;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError("[Skynomi.Ranks] Gagal membaca RankConfig.json: " + ex.Message);
                return new RankConfig();
            }
        }
    }
}
