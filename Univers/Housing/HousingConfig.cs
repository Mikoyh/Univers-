/*
 * =====================================================================
 *  HousingConfig.cs - Konfigurasi Sistem Housing / Plot RP
 *  Namespace: Univers.Housing (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.Housing
{
    public class HouseTierInfo
    {
        public int Level { get; set; }
        public string Title { get; set; } = string.Empty;
        public long UpgradeCostShards { get; set; }
        public int MaxCoOwners { get; set; }
        public int MaxMembers { get; set; }
    }

    public class HousingConfig
    {
        public bool Enabled { get; set; } = true;

        // Default hanya aktif di Hub ("lobby") agar world RTP bebas eksplorasi
        public List<string> AllowedWorldIds { get; set; } = new() { "lobby" };

        public long DefaultPlotPrice { get; set; } = 1000;
        public int DefaultPlotWidth { get; set; } = 44;
        public int DefaultPlotHeight { get; set; } = 30;
        public int DefaultGridSpacing { get; set; } = 8;

        public double AbandonRefundPercent { get; set; } = 50.0;
        public double ResaleTaxPercent { get; set; } = 5.0;
        public int SignConfirmTimeoutSeconds { get; set; } = 6;
        public bool EnableCozyBuff { get; set; } = true;

        // Maksimum kavling rumah yang bisa dimiliki berdasarkan RankIndex (0-8)
        public Dictionary<int, int> MaxHousesPerRank { get; set; } = new()
        {
            { 0, 1 },
            { 1, 1 },
            { 2, 2 },
            { 3, 2 },
            { 4, 3 },
            { 5, 3 },
            { 6, 4 },
            { 7, 4 },
            { 8, 5 }
        };

        public List<HouseTierInfo> HouseTiers { get; set; } = new()
        {
            new HouseTierInfo { Level = 1, Title = "Cottage", UpgradeCostShards = 0, MaxCoOwners = 1, MaxMembers = 4 },
            new HouseTierInfo { Level = 2, Title = "Townhouse", UpgradeCostShards = 1500, MaxCoOwners = 2, MaxMembers = 8 },
            new HouseTierInfo { Level = 3, Title = "Manor", UpgradeCostShards = 4000, MaxCoOwners = 3, MaxMembers = 12 },
            new HouseTierInfo { Level = 4, Title = "Grand Estate", UpgradeCostShards = 10000, MaxCoOwners = 5, MaxMembers = 20 },
            new HouseTierInfo { Level = 5, Title = "Cosmic Citadel", UpgradeCostShards = 25000, MaxCoOwners = 8, MaxMembers = 40 }
        };

        public static HousingConfig Load(string path)
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
                    var loaded = JsonConvert.DeserializeObject<HousingConfig>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        loaded.Save(path);
                        return loaded;
                    }
                }

                var def = new HousingConfig();
                def.Normalize();
                def.Save(path);
                return def;
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[Univers.Housing] Gagal memuat HousingConfig.json: {ex.Message}");
                var fallback = new HousingConfig();
                fallback.Normalize();
                return fallback;
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
                TShock.Log.ConsoleError($"[Univers.Housing] Gagal menyimpan HousingConfig.json: {ex.Message}");
            }
        }

        public void Normalize()
        {
            DefaultPlotPrice = Math.Max(0, DefaultPlotPrice);
            DefaultPlotWidth = Math.Clamp(DefaultPlotWidth, 14, 200);
            DefaultPlotHeight = Math.Clamp(DefaultPlotHeight, 12, 150);
            DefaultGridSpacing = Math.Clamp(DefaultGridSpacing, 2, 40);
            AbandonRefundPercent = Math.Clamp(AbandonRefundPercent, 0.0, 100.0);
            ResaleTaxPercent = Math.Clamp(ResaleTaxPercent, 0.0, 50.0);
            SignConfirmTimeoutSeconds = Math.Clamp(SignConfirmTimeoutSeconds, 2, 20);
            AllowedWorldIds ??= new List<string> { "lobby" };
            MaxHousesPerRank ??= new Dictionary<int, int>();
            HouseTiers ??= new List<HouseTierInfo>();
            if (HouseTiers.Count == 0)
            {
                HouseTiers = new HousingConfig().HouseTiers;
            }
        }

        public bool IsWorldAllowed(string worldId)
        {
            if (AllowedWorldIds == null || AllowedWorldIds.Count == 0) return true;
            return AllowedWorldIds.Any(w => w.Equals("*", StringComparison.OrdinalIgnoreCase) ||
                                            w.Equals(worldId, StringComparison.OrdinalIgnoreCase));
        }

        public int GetMaxHousesForRank(int rankIndex)
        {
            if (MaxHousesPerRank.TryGetValue(rankIndex, out int limit)) return Math.Max(1, limit);
            return 1;
        }

        public HouseTierInfo GetTier(int level)
        {
            return HouseTiers.FirstOrDefault(t => t.Level == level)
                ?? HouseTiers.OrderBy(t => t.Level).LastOrDefault()
                ?? new HouseTierInfo { Level = 1, Title = "Cottage", UpgradeCostShards = 0, MaxCoOwners = 1, MaxMembers = 4 };
        }
    }
}
