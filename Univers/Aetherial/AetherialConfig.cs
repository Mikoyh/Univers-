/*
 * =====================================================================
 *  AetherialConfig.cs - Configuration for proc chance, tag durations, and balance
 * =====================================================================
 */

using System;
using System.IO;
using Newtonsoft.Json;

namespace Univers.Aetherial
{
    public class AetherialConfig
    {
        public int TagProcChancePercent { get; set; } = 15;   // 15% Proc Chance per hit
        public int TagDurationSeconds { get; set; } = 5;      // 5 Detik Durasi Tag di Mob
        public int UserCooldownSeconds { get; set; } = 3;     // 3 Detik Personal Cooldown (+5s = 8s Total Lockout)
        public double SoloSynergyBonusPercent { get; set; } = 5.0; // +5% Damage
        public double SoloMarkedBonusPercent { get; set; } = 10.0; // +10% Damage
        public double SecretElementOmniaChancePercent { get; set; } = 0.000001; // 0.000001% Chance Easter Egg

        public static AetherialConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                var cfg = new AetherialConfig();
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                return cfg;
            }

            try
            {
                return JsonConvert.DeserializeObject<AetherialConfig>(File.ReadAllText(path)) ?? new AetherialConfig();
            }
            catch
            {
                return new AetherialConfig();
            }
        }
    }
}
