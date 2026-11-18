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

namespace Skynomi.Jobs
{
public class ActiveJobShift
{
    public string JobId { get; set; } = string.Empty;
    public int TargetId { get; set; } = 0;

    // [FIX MUTLAK NAMA MOB] Menerjemahkan ID Terraria (termasuk ID negatif) menjadi nama asli yang mudah dibaca
    public string TargetName 
    { 
        get 
        {
            if (JobId == "hunter")
            {
                NPC mockNpc = new NPC();
                mockNpc.SetDefaults(TargetId);
                // Mengambil nama resmi (misal: "Green Slime" bukan "-3")
                return !string.IsNullOrEmpty(mockNpc.TypeName) ? mockNpc.TypeName : "Unknown Monster";
            }
            
            // Fallback default untuk miner/fisher yang menggunakan nama string manual bawaan database
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
    public string AccountName { get; set; } = string.Empty; // <-- FIX CS8618: Berikan nilai awal default kosong

    public List<string> ActiveJobs { get; set; } = new();
    public Dictionary<string, int> JobLevels { get; set; } = new();
    public Dictionary<string, long> JobExp { get; set; } = new();
    public Dictionary<string, ActiveJobShift> ActiveShifts { get; set; } = new();

        public DateTime LastJobLeaveTime { get; set; } = DateTime.MinValue; // Untuk 30 menit cooldown
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
            // BALANCING EKONOMI SKYNOMI 2.0 (1000 HP = 1 Shard. Batas Dompet Mortal = 1500 Shard)
            // Gaji disesuaikan rasional: Kisaran 5 s/d 150 Shard maksimal di level tinggi.
            
            if (jobId == "miner")
            {
                if (level < 5)   // Lv. 1 - 4
                    return new ActiveJobShift { JobId = "miner", TargetName = "Stone Block", TargetId = TileID.Stone, RequiredCount = 30, RewardCoins = 5, RewardExp = 25 };
                if (level < 10)  // Lv. 5 - 9
                    return new ActiveJobShift { JobId = "miner", TargetName = "Copper / Tin Ore", TargetId = TileID.Copper, RequiredCount = 20, RewardCoins = 8, RewardExp = 40 };
                if (level < 15)  // Lv. 10 - 14
                    return new ActiveJobShift { JobId = "miner", TargetName = "Iron / Lead Ore", TargetId = TileID.Iron, RequiredCount = 20, RewardCoins = 12, RewardExp = 65 };
                if (level < 25)  // Lv. 15 - 24
                    return new ActiveJobShift { JobId = "miner", TargetName = "Silver / Tungsten Ore", TargetId = TileID.Silver, RequiredCount = 15, RewardCoins = 18, RewardExp = 90 };
                if (level < 40)  // Lv. 25 - 39
                    return new ActiveJobShift { JobId = "miner", TargetName = "Gold / Platinum Ore", TargetId = TileID.Gold, RequiredCount = 15, RewardCoins = 25, RewardExp = 130 };
                if (level < 60)  // Lv. 40 - 59 (Pre-Hardmode Peak)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Hellstone Ore", TargetId = TileID.Hellstone, RequiredCount = 12, RewardCoins = 40, RewardExp = 200 };
                if (level < 85)  // Lv. 60 - 84 (Early Hardmode)
                    return new ActiveJobShift { JobId = "miner", TargetName = "Cobalt / Palladium", TargetId = TileID.Cobalt, RequiredCount = 10, RewardCoins = 65, RewardExp = 350 };
                
                // End-game Vocation
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
                // SEKUENS PROGRES MONSTER BERTAHAP SANGAT HALUS (GRANULAR)
                if (level < 4)       // Awal Karir: Hanya Slime murni
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Green / Blue Slime", TargetId = NPCID.GreenSlime, RequiredCount = 10, RewardCoins = 4, RewardExp = 20 };
                if (level < 10)      // Mulai menghadapi Zombie malam
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Surface Zombie", TargetId = NPCID.Zombie, RequiredCount = 8, RewardCoins = 7, RewardExp = 35 };
                if (level < 20)      // Eksplorasi Gua Kedalaman
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Cave Jellyfish / Bat", TargetId = NPCID.CaveBat, RequiredCount = 8, RewardCoins = 12, RewardExp = 60 };
                if (level < 35)      // Ancaman Koropsi / Brain
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Eater of Souls / Crimera", TargetId = NPCID.EaterofSouls, RequiredCount = 10, RewardCoins = 22, RewardExp = 110 };
                if (level < 55)      // Menjelang Underworld Kedalaman Akhir Pre-Hardmode
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Underworld Demon", TargetId = NPCID.Demon, RequiredCount = 6, RewardCoins = 45, RewardExp = 220 };
                if (level < 80)      // Masuk Hardmode Awal
                    return new ActiveJobShift { JobId = "hunter", TargetName = "Armored Skeleton", TargetId = NPCID.ArmoredSkeleton, RequiredCount = 6, RewardCoins = 75, RewardExp = 400 };

                // Puncak Tantangan Hunter
                return new ActiveJobShift { JobId = "hunter", TargetName = "Chaos Elemental", TargetId = NPCID.ChaosElemental, RequiredCount = 5, RewardCoins = 140, RewardExp = 750 };
            }

            // Fallback dasar aman untuk profesi pendukung
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

// <-- FIX CS8603: Tambahkan tanda ? setelah nama kelas agar compiler tahu metode ini diizinkan mengembalikan null
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
