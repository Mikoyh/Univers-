/*
 * =====================================================================
 *  JobConfig.cs - Configuration file loader and task shift tuning
 * =====================================================================
 */

using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Skynomi.Jobs
{
    public class JobConfig
    {
        public int DefaultMaxJobsPerPlayer { get; set; } = 1; // Default 1, rank tinggi bisa dobel job
        public int LeaveJobCooldownMinutes { get; set; } = 30; // 30 Menit Cooldown saat leave job
        public int TaskCooldownMinutes { get; set; } = 5;      // 5 Menit Cooldown istirahat antar shift
        public double LevelBonusPercent { get; set; } = 2.5;
        public bool EnableCombatTextFeedback { get; set; } = true;
        public bool EnableLoginReminder { get; set; } = true;

        public static JobConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                var cfg = new JobConfig();
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                return cfg;
            }

            try
            {
                return JsonConvert.DeserializeObject<JobConfig>(File.ReadAllText(path)) ?? new JobConfig();
            }
            catch
            {
                return new JobConfig();
            }
        }
    }
}
