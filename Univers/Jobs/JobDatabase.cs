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
            if (!ActiveShifts.TryGetValue(jobId, out var shift))
            {
                shift = CreateDefaultShiftForLevel(jobId, GetJobLevel(jobId));
                ActiveShifts[jobId] = shift;
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
            int currentLvl = JobLevels[jobId];
            long requiredForNext = (long)(150 * Math.Pow(currentLvl, 1.45));

            if (JobExp[jobId] >= requiredForNext && currentLvl < 50)
            {
                JobLevels[jobId]++;
                newLevel = JobLevels[jobId];
                return true;
            }

            newLevel = currentLvl;
            return false;
        }

        private static ActiveJobShift CreateDefaultShiftForLevel(string jobId, int level)
        {
            if (jobId == "miner")
            {
                if (level < 5)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Stone Block", TargetId = TileID.Stone, RequiredCount = 30, RewardCoins = 5, RewardExp = 25 };
                if (level < 10)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Copper / Tin Ore", TargetId = TileID.Copper, RequiredCount = 20, RewardCoins = 8, RewardExp = 40 };
                if (level < 15)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Iron / Lead Ore", TargetId = TileID.Iron, RequiredCount = 20, RewardCoins = 12, RewardExp = 65 };
                if (level < 25)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Silver / Tungsten Ore", TargetId = TileID.Silver, RequiredCount = 15, RewardCoins = 18, RewardExp = 90 };
                if (level < 40)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Gold / Platinum Ore", TargetId = TileID.Gold, RequiredCount = 15, RewardCoins = 25, RewardExp = 130 };
                if (level < 60)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Hellstone Ore", TargetId = TileID.Hellstone, RequiredCount = 12, RewardCoins = 40, RewardExp = 200 };
                if (level < 85)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Cobalt / Palladium", TargetId = TileID.Cobalt, RequiredCount = 10, RewardCoins = 65, RewardExp = 350 };

                return new ActiveJobShift { JobId = "miner", TargetName = "Chlorophyte Ore", TargetId = TileID.Chlorophyte, RequiredCount = 8, RewardCoins = 110, RewardExp = 600 };
            }

            if (jobId == "fisher")
            {
                if (level < 6)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Bass (Air Tawar)", TargetId = ItemID.Bass, RequiredCount = 4, RewardCoins = 6, RewardExp = 30 };
                if (level < 15)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Tuna (Samudra)", TargetId = ItemID.Tuna, RequiredCount = 3, RewardCoins = 14, RewardExp = 75 };
                if (level < 30)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Wooden / Iron Crate", TargetId = ItemID.WoodenCrate, RequiredCount = 1, RewardCoins = 30, RewardExp = 150 };
                if (level < 60)
                    return new ActiveJobShift { JobId = "fisher", TargetName = "Prismite (Danau Hallow)", TargetId = ItemID.Prismite, RequiredCount = 1, RewardCoins = 60, RewardExp = 300 };

                return new ActiveJobShift { JobId = "fisher", TargetName = "Golden Crate / Carp", TargetId = ItemID.GoldenCrate, RequiredCount = 1, RewardCoins = 130, RewardExp = 700 };
            }

            if (jobId == "hunter")
            {
                if (level < 4)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Green / Blue Slime", TargetId = NPCID.GreenSlime, RequiredCount = 10, RewardCoins = 4, RewardExp = 20 };
                if (level < 10)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Surface Zombie", TargetId = NPCID.Zombie, RequiredCount = 8, RewardCoins = 7, RewardExp = 35 };
                if (level < 20)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Cave Jellyfish / Bat", TargetId = NPCID.CaveBat, RequiredCount = 8, RewardCoins = 12, RewardExp = 60 };
                if (level < 35)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Eater of Souls / Crimera", TargetId = NPCID.EaterofSouls, RequiredCount = 10, RewardCoins = 22, RewardExp = 110 };
                if (level < 55)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Underworld Demon", TargetId = NPCID.Demon, RequiredCount = 6, RewardCoins = 45, RewardExp = 220 };
                if (level < 80)
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Armored Skeleton", TargetId = NPCID.ArmoredSkeleton, RequiredCount = 6, RewardCoins = 75, RewardExp = 400 };

                return new ActiveJobShift { JobId = "hunter", TargetName = "Chaos Elemental", TargetId = NPCID.ChaosElemental, RequiredCount = 5, RewardCoins = 140, RewardExp = 750 };
            }

            return new ActiveJobShift { JobId = jobId, TargetName = "Wood Block", TargetId = TileID.Trees, RequiredCount = 20, RewardCoins = 4, RewardExp = 20 };
        }
    }

    public class JobDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<JobUserProfile> _profiles;

        public JobDatabase(string dbPath)
        {
            _db = new LiteDatabase(dbPath);
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
