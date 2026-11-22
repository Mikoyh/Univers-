/*
 * =====================================================================
 *  JobDatabase.cs - Persistent storage for shifts, cooldowns, and profiles
 * =====================================================================
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LiteDB;
using Terraria;
using Terraria.ID;

namespace Univers.Jobs
{
    public class ActiveJobShift
    {
        public string JobId { get; set; } = string.Empty;
        public int TargetId { get; set; } = 0;

        public string TargetName
        {
            get
            {
                if (JobId == "hunter")
                {
                    NPC mockNpc = new NPC();
                    mockNpc.SetDefaults(TargetId);
                    return !string.IsNullOrEmpty(mockNpc.TypeName) ? mockNpc.TypeName : "Unknown Monster";
                }

                return _targetName;
            }
            set => _targetName = value;
        }

        private string _targetName = string.Empty;

        public string AccountName { get; set; } = string.Empty;
        public int RequiredCount { get; set; }
        public int CurrentCount { get; set; }
        public long RewardCoins { get; set; }
        public long RewardExp { get; set; }
        public DateTime ShiftCooldownUntil { get; set; } = DateTime.MinValue;
        public bool NeedsCooldownAlert { get; set; } = false;

        public bool IsOnCooldown => DateTime.UtcNow < ShiftCooldownUntil;
    }

    public class JobUserProfile
    {
        [BsonId]
        public string AccountName { get; set; } = string.Empty;

        public List<string> ActiveJobs { get; set; } = new();
        public Dictionary<string, int> JobLevels { get; set; } = new();
        public Dictionary<string, long> JobExp { get; set; } = new();
        public Dictionary<string, ActiveJobShift> ActiveShifts { get; set; } = new();

        public DateTime LastJobLeaveTime { get; set; } = DateTime.MinValue;
        public long TotalCoinsEarned { get; set; } = 0;
        public long TotalActionsCount { get; set; } = 0;
        public DateTime FirstJoinedJobs { get; set; } = DateTime.UtcNow;

        public bool HasJob(string jobId)
        {
            return ActiveJobs.Contains(jobId.ToLowerInvariant());
        }

        public int GetJobLevel(string jobId)
        {
            jobId = jobId.ToLowerInvariant();
            if (JobLevels.TryGetValue(jobId, out int lvl)) return lvl;
            return 1;
        }

        public long GetJobExp(string jobId)
        {
            jobId = jobId.ToLowerInvariant();
            if (JobExp.TryGetValue(jobId, out long exp)) return exp;
            return 0;
        }

        public ActiveJobShift GetOrCreateShift(string jobId)
        {
            jobId = jobId.ToLowerInvariant();
            var expected = CreateDefaultShiftForLevel(jobId, GetJobLevel(jobId));
            if (!ActiveShifts.TryGetValue(jobId, out var shift))
            {
                ActiveShifts[jobId] = expected;
                return expected;
            }

            // Sinkronkan target/upah lama di database agar ikut aturan balance terbaru tanpa mereset progres/cooldown
            if (shift.RewardCoins != expected.RewardCoins || shift.RewardExp != expected.RewardExp || shift.TargetId != expected.TargetId)
            {
                shift.TargetId = expected.TargetId;
                shift.TargetName = expected.TargetName;
                shift.RequiredCount = expected.RequiredCount;
                shift.CurrentCount = Math.Min(shift.CurrentCount, expected.RequiredCount - 1);
                shift.RewardCoins = expected.RewardCoins;
                shift.RewardExp = expected.RewardExp;
            }
            return shift;
        }

        public void RollNextShift(string jobId, int currentLvl)
        {
            jobId = jobId.ToLowerInvariant();
            ActiveShifts[jobId] = CreateDefaultShiftForLevel(jobId, currentLvl);
        }

        public bool AddJobExp(string jobId, long amount, out int newLevel)
        {
            jobId = jobId.ToLowerInvariant();
            if (!JobLevels.ContainsKey(jobId)) JobLevels[jobId] = 1;
            if (!JobExp.ContainsKey(jobId)) JobExp[jobId] = 0;

            JobExp[jobId] += amount;
            int currentLvl = Math.Clamp(JobLevels[jobId], 1, 50);
            JobLevels[jobId] = currentLvl;

            bool leveledUp = false;
            while (currentLvl < 50)
            {
                long requiredForNext = (long)(150 * Math.Pow(currentLvl, 1.45));
                if (JobExp[jobId] >= requiredForNext)
                {
                    JobExp[jobId] -= requiredForNext;
                    currentLvl++;
                    JobLevels[jobId] = currentLvl;
                    leveledUp = true;
                }
                else
                {
                    break;
                }
            }

            newLevel = currentLvl;
            return leveledUp;
        }

        private static ActiveJobShift CreateDefaultShiftForLevel(string jobId, int level)
        {
            level = Math.Clamp(level, 1, 50);

            if (jobId == "miner")
            {
                if (level < 5)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Stone Block", TargetId = TileID.Stone, RequiredCount = 30, RewardCoins = 40, RewardExp = 35 };
                if (level < 10)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Copper / Tin Ore", TargetId = TileID.Copper, RequiredCount = 20, RewardCoins = 75, RewardExp = 70 };
                if (level < 17)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Iron / Lead Ore", TargetId = TileID.Iron, RequiredCount = 20, RewardCoins = 140, RewardExp = 125 };
                if (level < 25)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Silver / Tungsten Ore", TargetId = TileID.Silver, RequiredCount = 15, RewardCoins = 260, RewardExp = 220 };
                if (level < 33)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Gold / Platinum Ore", TargetId = TileID.Gold, RequiredCount = 15, RewardCoins = 450, RewardExp = 360 };
                if (level < 41)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Hellstone / Cobalt", TargetId = TileID.Hellstone, RequiredCount = 12, RewardCoins = 750, RewardExp = 580 };

                return new ActiveJobShift { JobId = "miner", TargetName = "Chlorophyte Ore", TargetId = TileID.Chlorophyte, RequiredCount = 10, RewardCoins = 1150, RewardExp = 900 };
            }

            if (jobId == "fisher")
            {
                if (level < 5)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Bass (Air Tawar)", TargetId = ItemID.Bass, RequiredCount = 4, RewardCoins = 45, RewardExp = 40 };
                if (level < 10)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Tuna (Samudra)", TargetId = ItemID.Tuna, RequiredCount = 3, RewardCoins = 85, RewardExp = 80 };
                if (level < 17)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Wooden / Iron Crate", TargetId = ItemID.WoodenCrate, RequiredCount = 1, RewardCoins = 160, RewardExp = 145 };
                if (level < 25)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Prismite (Hallow)", TargetId = ItemID.Prismite, RequiredCount = 1, RewardCoins = 290, RewardExp = 240 };
                if (level < 33)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Crate / Rare Fish", TargetId = ItemID.WoodenCrate, RequiredCount = 2, RewardCoins = 500, RewardExp = 400 };
                if (level < 41)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Golden Crate", TargetId = ItemID.GoldenCrate, RequiredCount = 1, RewardCoins = 850, RewardExp = 650 };

                return new ActiveJobShift { JobId = "fisher", TargetName = "Golden Crate / Carp", TargetId = ItemID.GoldenCrate, RequiredCount = 1, RewardCoins = 1250, RewardExp = 950 };
            }

            if (jobId == "hunter")
            {
                if (level < 5)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Green / Blue Slime", TargetId = NPCID.GreenSlime, RequiredCount = 10, RewardCoins = 35, RewardExp = 35 };
                if (level < 10)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Surface Zombie", TargetId = NPCID.Zombie, RequiredCount = 8, RewardCoins = 70, RewardExp = 65 };
                if (level < 17)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Cave Bat / Cavern Mob", TargetId = NPCID.CaveBat, RequiredCount = 8, RewardCoins = 135, RewardExp = 120 };
                if (level < 25)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Eater of Souls / Crimera", TargetId = NPCID.EaterofSouls, RequiredCount = 10, RewardCoins = 250, RewardExp = 215 };
                if (level < 33)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Underworld Demon", TargetId = NPCID.Demon, RequiredCount = 6, RewardCoins = 440, RewardExp = 370 };
                if (level < 41)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Armored Skeleton", TargetId = NPCID.ArmoredSkeleton, RequiredCount = 6, RewardCoins = 760, RewardExp = 600 };

                return new ActiveJobShift { JobId = "hunter", TargetName = "Chaos Elemental", TargetId = NPCID.ChaosElemental, RequiredCount = 5, RewardCoins = 1200, RewardExp = 950 };
            }

            if (jobId == "farmer")
            {
                if (level < 5)
                    return new ActiveJobShift { JobId = "farmer", TargetName = "Wild Plants / Herbs", TargetId = 83, RequiredCount = 15, RewardCoins = 35, RewardExp = 35 };
                if (level < 10)
                    return new ActiveJobShift { JobId = "farmer", TargetName = "Wild Herbs & Mushrooms", TargetId = 83, RequiredCount = 20, RewardCoins = 70, RewardExp = 65 };
                if (level < 17)
                    return new ActiveJobShift { JobId = "farmer", TargetName = "Desert Cactus / Herbs", TargetId = 80, RequiredCount = 20, RewardCoins = 130, RewardExp = 120 };
                if (level < 25)
                    return new ActiveJobShift { JobId = "farmer", TargetName = "Glowing Mushrooms / Herbs", TargetId = 71, RequiredCount = 25, RewardCoins = 240, RewardExp = 210 };
                if (level < 33)
                    return new ActiveJobShift { JobId = "farmer", TargetName = "Mature Herbs Batch", TargetId = 83, RequiredCount = 30, RewardCoins = 420, RewardExp = 350 };
                if (level < 41)
                    return new ActiveJobShift { JobId = "farmer", TargetName = "Master Botanist Harvest", TargetId = 83, RequiredCount = 35, RewardCoins = 720, RewardExp = 570 };

                return new ActiveJobShift { JobId = "farmer", TargetName = "Grand Botanical Harvest", TargetId = 83, RequiredCount = 40, RewardCoins = 1100, RewardExp = 880 };
            }

            // Default: lumberjack
            if (level < 5)
                return new ActiveJobShift { JobId = jobId, TargetName = "Wood Tree (Kayu)", TargetId = TileID.Trees, RequiredCount = 20, RewardCoins = 35, RewardExp = 35 };
            if (level < 10)
                return new ActiveJobShift { JobId = jobId, TargetName = "Forest / Snow Trees", TargetId = TileID.Trees, RequiredCount = 25, RewardCoins = 70, RewardExp = 65 };
            if (level < 17)
                return new ActiveJobShift { JobId = jobId, TargetName = "Jungle / Evil Trees", TargetId = TileID.Trees, RequiredCount = 30, RewardCoins = 130, RewardExp = 120 };
            if (level < 25)
                return new ActiveJobShift { JobId = jobId, TargetName = "Palm / Timber Trees", TargetId = TileID.Trees, RequiredCount = 35, RewardCoins = 240, RewardExp = 210 };
            if (level < 33)
                return new ActiveJobShift { JobId = jobId, TargetName = "Hardmode Timber", TargetId = TileID.Trees, RequiredCount = 40, RewardCoins = 420, RewardExp = 350 };
            if (level < 41)
                return new ActiveJobShift { JobId = jobId, TargetName = "Hallowed Timber", TargetId = TileID.Trees, RequiredCount = 45, RewardCoins = 700, RewardExp = 560 };

            return new ActiveJobShift { JobId = jobId, TargetName = "Master Timber Harvest", TargetId = TileID.Trees, RequiredCount = 50, RewardCoins = 1050, RewardExp = 850 };
        }
    }

    public class JobDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<JobUserProfile> _profiles;

        public JobDatabase(string dbPath)
        {
            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _profiles = _db.GetCollection<JobUserProfile>("players_jobs");
            _profiles.EnsureIndex(x => x.AccountName);
        }

        public JobUserProfile? GetOrCreateProfile(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return null;

            var profile = _profiles.FindById(accountName);
            if (profile == null)
            {
                profile = new JobUserProfile
                {
                    AccountName = accountName,
                    ActiveJobs = new List<string>(),
                    JobLevels = new Dictionary<string, int>(),
                    JobExp = new Dictionary<string, long>()
                };
                _profiles.Insert(profile);
            }
            return profile;
        }

        public void SaveProfile(JobUserProfile profile)
        {
            if (profile != null)
            {
                _profiles.Upsert(profile);
            }
        }

        public IEnumerable<JobUserProfile> GetTopEarners(int limit = 10)
        {
            return _profiles.Query()
                .OrderByDescending(x => x.TotalCoinsEarned)
                .Limit(limit)
                .ToEnumerable();
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
