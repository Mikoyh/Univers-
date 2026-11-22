/*
 * =====================================================================
 *  JobConfig.cs - Configuration file loader and task shift tuning
 * =====================================================================
 */

using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Univers.Jobs
{
    public class JobConfig
    {
        public int DefaultMaxJobsPerPlayer { get; set; } = 1; // Default 1, rank tinggi bisa dobel job
        public int LeaveJobCooldownMinutes { get; set; } = 30; // 30 Menit Cooldown saat leave job
        public int TaskCooldownMinutes { get; set; } = 5;      // 5 Menit Cooldown istirahat antar shift
        public double LevelBonusPercent { get; set; } = 2.0;   // +2% per job level (max +98% di Lv 50)
        public bool EnableCombatTextFeedback { get; set; } = true;
        public bool EnableLoginReminder { get; set; } = true;

        public static JobConfig Load(string path)
        {
            JobConfig cfg;
            if (!File.Exists(path))
            {
                cfg = new JobConfig();
            }
            else
            {
                try
                {
                    cfg = JsonConvert.DeserializeObject<JobConfig>(File.ReadAllText(path)) ?? new JobConfig();
                }
                catch
                {
                    cfg = new JobConfig();
                }
            }

            cfg.DefaultMaxJobsPerPlayer = Math.Clamp(cfg.DefaultMaxJobsPerPlayer, 1, 5);
            cfg.LeaveJobCooldownMinutes = Math.Clamp(cfg.LeaveJobCooldownMinutes, 1, 120);
            cfg.TaskCooldownMinutes = Math.Clamp(cfg.TaskCooldownMinutes, 1, 60);
            cfg.LevelBonusPercent = Math.Clamp(cfg.LevelBonusPercent, 0.0, 5.0);

            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            }
            catch { }

            return cfg;
        }
    }
}
