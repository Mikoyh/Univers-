/*
 * =====================================================================
 *  SurvivalConfig.cs - Konfigurasi Engine Don't Starve RPG
 *  Namespace: Univers.Survival (.NET 9.0 / TShock 6.x)
 *  Aktif secara default hanya di world 'survival' (pause di 'lobby')
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TShockAPI;

namespace Univers.Survival
{
    public class SurvivalConfig
    {
        public bool Enabled { get; set; } = true;

        // Hanya world di daftar ini yang mengaktifkan mekanik Hunger / Sanity / Suhu / Wetness
        public List<string> ActiveWorldIds { get; set; } = new() { "survival" };

        // Bersihkan debuff survival saat masuk ke world non-survival (mis. lobby)
        public bool ClearSurvivalDebuffsOutsideActiveWorlds { get; set; } = true;

        // --- HUNGER ---
        public double MaxHunger { get; set; } = 100.0;
        public double StartingHunger { get; set; } = 100.0;
        public double RespawnHunger { get; set; } = 80.0;
        public double PassiveHungerDrainPerSecond { get; set; } = 0.085;
        public double MovingHungerMultiplier { get; set; } = 1.35;
        public double ActivityHungerDrain { get; set; } = 0.08;
        public double PeckishThreshold { get; set; } = 60.0;
        public double HungryThreshold { get; set; } = 35.0;
        public double StarvingThreshold { get; set; } = 10.0;
        public int StarvingDamage { get; set; } = 4;

        // --- SANITY ---
        public double MaxSanity { get; set; } = 100.0;
        public double StartingSanity { get; set; } = 100.0;
        public double RespawnSanity { get; set; } = 80.0;
        public double DarknessSanityDrainPerSecond { get; set; } = 0.45;
        public double RainSanityDrainPerSecond { get; set; } = 0.10;
        public double BossSanityDrainPerSecond { get; set; } = 0.30;
        public double DaylightSanityRegenPerSecond { get; set; } = 0.07;
        public double CampfireSanityRegenPerSecond { get; set; } = 0.55;
        public double BedSanityRegenPerSecond { get; set; } = 0.85;
        public double SunflowerSanityRegenPerSecond { get; set; } = 0.25;
        public double LowSanityThreshold { get; set; } = 30.0;
        public double ShadowMonsterThreshold { get; set; } = 15.0;
        public int ShadowMonsterCooldownSeconds { get; set; } = 90;
        public int DarknessGraceSeconds { get; set; } = 4;
        public int DarknessDamage { get; set; } = 6;

        // --- TEMPERATURE & WETNESS ---
        public double IdealTemperature { get; set; } = 25.0;
        public double MinTemperature { get; set; } = -20.0;
        public double MaxTemperature { get; set; } = 60.0;
        public double ColdThreshold { get; set; } = 8.0;
        public double FreezingThreshold { get; set; } = 0.0;
        public double HotThreshold { get; set; } = 40.0;
        public double HeatstrokeThreshold { get; set; } = 48.0;
        public int ThermalDamage { get; set; } = 3;
        public double TemperatureAdaptRatePerSecond { get; set; } = 0.45;
        public double RainWetnessGainPerSecond { get; set; } = 1.6;
        public double WaterWetnessGainPerSecond { get; set; } = 9.0;
        public double DryRatePerSecond { get; set; } = 1.2;
        public double CampfireDryMultiplier { get; set; } = 4.0;
        public int EnvironmentScanRadiusTiles { get; set; } = 12;

        // --- HUD & PERSISTENCE ---
        public bool DefaultShowStatusHud { get; set; } = false;
        public int StatusHudRefreshSeconds { get; set; } = 2;
        public int AutoSaveIntervalSeconds { get; set; } = 30;

        public bool IsWorldActive(string worldId)
        {
            if (!Enabled || ActiveWorldIds == null || ActiveWorldIds.Count == 0) return false;
            return ActiveWorldIds.Any(id => !string.IsNullOrWhiteSpace(id) &&
                                            id.Equals(worldId, StringComparison.OrdinalIgnoreCase));
        }

        public static SurvivalConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<SurvivalConfig>(json);
                    if (cfg != null)
                    {
                        if (cfg.ActiveWorldIds == null || cfg.ActiveWorldIds.Count == 0)
                        {
                            cfg.ActiveWorldIds = new List<string> { "survival" };
                        }
                        cfg.Save(path);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleWarn($"[Univers.Survival] Gagal membaca SurvivalConfig.json, menggunakan default: {ex.Message}");
            }

            var fresh = new SurvivalConfig();
            fresh.Save(path);
            return fresh;
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
                TShock.Log.ConsoleError($"[Univers.Survival] Gagal menyimpan SurvivalConfig.json: {ex.Message}");
            }
        }
    }
}
