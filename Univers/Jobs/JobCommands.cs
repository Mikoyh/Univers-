/*
 * =====================================================================
 *  JobCommands.cs - Interactive player chat commands for Jobs
 * =====================================================================
 */

using System;
using System.Linq;
using System.Collections.Generic;
using TShockAPI;

namespace Univers.Jobs
{
    public static class JobCommands
    {
        public static void Register()
        {
            Commands.ChatCommands.Add(new Command("univers.job", HandleJobCommand, "job", "jobs", "pekerjaan")
            {
                HelpText = "Kelola pekerjaan server (/job join, /job leave, /job stats, /job list, /job top)"
            });
        }

        private static void HandleJobCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("[c/ef4444:Kamu harus login akun TShock terlebih dahulu untuk menggunakan sistem Jobs!]");
                return;
            }

            var profile = JobModule.Database.GetOrCreateProfile(player.Account.Name);
            if (profile == null) return;

            if (args.Parameters.Count == 0)
            {
                ShowJobStatus(player, profile);
                return;
            }

            string subCmd = args.Parameters[0].ToLowerInvariant();

            switch (subCmd)
            {
                case "list":
                case "daftar":
                    ShowAvailableJobs(player);
                    break;

                case "join":
                case "masuk":
                case "pilih":
                    if (args.Parameters.Count < 2)
                    {
                        player.SendInfoMessage("[c/38bdf8:Penggunaan:] /job join <miner|fisher|hunter|lumberjack|farmer>");
                        return;
                    }
                    HandleJoinJob(player, profile, args.Parameters[1].ToLowerInvariant());
                    break;

                case "leave":
                case "keluar":
                    if (args.Parameters.Count < 2)
                    {
                        player.SendInfoMessage("[c/38bdf8:Penggunaan:] /job leave <nama_job>");
                        return;
                    }
                    HandleLeaveJob(player, profile, args.Parameters[1].ToLowerInvariant());
                    break;

                case "stats":
                case "status":
                case "info":
                    ShowPlayerJobStats(player, profile);
                    break;

                case "top":
                case "leaderboard":
                    ShowTopWorkers(player);
                    break;

                default:
                    ShowJobStatus(player, profile);
                    break;
            }
        }

        private static void ShowJobStatus(TSPlayer player, JobUserProfile profile)
        {
            player.SendInfoMessage("=== [c/f59e0b:UNIVERS JOBS & VOCATION] ===");
            int maxJobs = JobModule.GetMaxAllowedJobsForPlayer(player);

            if (profile.ActiveJobs.Count == 0)
            {
                player.SendWarningMessage("[c/fbbf24:Kamu belum memiliki pekerjaan aktif!]");
                player.SendInfoMessage($"Kapasitas Slot Job Kamu: [c/4ade80:{maxJobs} Pekerjaan] (Kasta Rank)");
                player.SendInfoMessage("Ketik [c/38bdf8:/job list] untuk melihat lowongan & [c/38bdf8:/job join <nama>] untuk mulai bekerja!");
                return;
            }

            player.SendInfoMessage($"Pekerjaan Aktif: [c/38bdf8:{string.Join(", ", profile.ActiveJobs.Select(j => j.ToUpper()))}] ({profile.ActiveJobs.Count}/{maxJobs} Slot)");

            foreach (var jobId in profile.ActiveJobs)
            {
                var shift = profile.GetOrCreateShift(jobId);
                int lvl = profile.GetJobLevel(jobId);

                if (shift.IsOnCooldown)
                {
                    var rem = shift.ShiftCooldownUntil - DateTime.UtcNow;
                    player.SendWarningMessage($"* [{jobId.ToUpper()}] Level {lvl}: [c/fbbf24:SEDANG COOLDOWN ({rem.Minutes}m {rem.Seconds:00}s lagi)]");
                    player.SendInfoMessage($"  Kamu akan diberi tahu otomatis di chat setelah cooldown selesai.");
                }
                else
                {
                    player.SendSuccessMessage($"* [{jobId.ToUpper()}] Level {lvl}: [c/4ade80:SHIFT SIAP DIKERJAKAN!]");
                    player.SendInfoMessage($"  Target Misi: [c/e2e8f0:{shift.TargetName}] ({shift.CurrentCount}/{shift.RequiredCount}x)");
                    player.SendInfoMessage($"  Upah Selesai: {JobModule.FormatCoinsShort(shift.RewardCoins)} + [c/38bdf8:{shift.RewardExp} EXP]");
                }
            }

            player.SendInfoMessage("[c/94a3b8:Perintah: /job join <nama>, /job leave <nama>, /job stats, /job list]");
        }

        private static void ShowAvailableJobs(TSPlayer player)
        {
            player.SendInfoMessage("=== [c/38bdf8:LOWONGAN PEKERJAAN SERVER] ===");
            player.SendInfoMessage("[c/f59e0b:1. MINER]      - Nambang Stone, Copper, Iron, Gold hingga Hellstone & Chlorophyte");
            player.SendInfoMessage("[c/06b6d4:2. FISHER]     - Tangkap Bass, Tuna, Peti Harta (Crates), hingga Jackpot Golden Carp");
            player.SendInfoMessage("[c/ef4444:3. HUNTER]     - Basmi Slime, Zombie, Underworld Demon, hingga Chaos Elemental");
            player.SendInfoMessage("[c/10b981:4. LUMBERJACK] - Tebang kayu hutan, pinus salju, mahoni jungle, & pearlwood");
            player.SendInfoMessage("[c/84cc16:5. FARMER]     - Panen herbal Daybloom, Moonglow, Waterleaf, Fireblossom");
            player.SendInfoMessage("[c/94a3b8:Gunakan /job join <nama_job> untuk melamar pekerjaan!]");
        }

        private static void HandleJoinJob(TSPlayer player, JobUserProfile profile, string jobName)
        {
            string[] validJobs = { "miner", "fisher", "hunter", "lumberjack", "farmer" };
            if (!validJobs.Contains(jobName))
            {
                player.SendErrorMessage($"[c/ef4444:Pekerjaan '{jobName}' tidak valid! Pilihan: {string.Join(", ", validJobs)}]");
                return;
            }

            if (profile.HasJob(jobName))
            {
                player.SendWarningMessage($"[c/fbbf24:Kamu sudah bekerja sebagai {jobName.ToUpper()}!]");
                return;
            }

            var elapsedSinceLeave = DateTime.UtcNow - profile.LastJobLeaveTime;
            if (elapsedSinceLeave.TotalMinutes < JobModule.Config.LeaveJobCooldownMinutes)
            {
                var remaining = TimeSpan.FromMinutes(JobModule.Config.LeaveJobCooldownMinutes) - elapsedSinceLeave;
                player.SendErrorMessage($"[c/ef4444:Kamu baru saja keluar dari pekerjaan! Tunggu {remaining.Minutes}m {remaining.Seconds:00}s lagi untuk memilih pekerjaan baru.]");
                return;
            }

            int maxJobs = JobModule.GetMaxAllowedJobsForPlayer(player);
            if (profile.ActiveJobs.Count >= maxJobs)
            {
                player.SendErrorMessage($"[c/ef4444:Batas maksimal pekerjaanmu saat ini adalah {maxJobs} job!]");
                if (maxJobs == 1)
                {
                    player.SendInfoMessage("[c/fbbf24:Tips: Naikkan rank kasta kamu melalui /rankup untuk membuka slot dobel job (2 atau lebih)!]");
                    player.SendInfoMessage("[c/94a3b8:Gunakan /job leave <nama> jika ingin berganti pekerjaan sekarang.]");
                }
                return;
            }

            profile.ActiveJobs.Add(jobName);
            if (!profile.JobLevels.ContainsKey(jobName)) profile.JobLevels[jobName] = 1;
            if (!profile.JobExp.ContainsKey(jobName)) profile.JobExp[jobName] = 0;

            var shift = profile.GetOrCreateShift(jobName);

            JobModule.Database.SaveProfile(profile);
            player.SendSuccessMessage($"[c/4ade80:Sukses!] Kamu resmi bergabung sebagai [c/f59e0b:{jobName.ToUpper()}]!");
            player.SendInfoMessage($"[c/38bdf8:Shift Pertamamu:] {shift.TargetName} ({shift.CurrentCount}/{shift.RequiredCount}x) | Upah: {JobModule.FormatCoinsShort(shift.RewardCoins)} + {shift.RewardExp} EXP.");
        }

        private static void HandleLeaveJob(TSPlayer player, JobUserProfile profile, string jobName)
        {
            if (!profile.HasJob(jobName))
            {
                player.SendWarningMessage($"[c/fbbf24:Kamu tidak sedang bekerja sebagai {jobName}!]");
                return;
            }

            profile.ActiveJobs.Remove(jobName);
            profile.LastJobLeaveTime = DateTime.UtcNow;
            JobModule.Database.SaveProfile(profile);

            player.SendSuccessMessage($"[c/94a3b8:Kamu telah keluar dari pekerjaan {jobName.ToUpper()}.]");
            player.SendWarningMessage($"[c/fbbf24:PERHATIAN: Kamu harus menunggu 30 menit cooldown sebelum dapat melamar pekerjaan baru!]");
            player.SendInfoMessage("[c/94a3b8:Progres level dan akumulasi EXP pekerjaanmu tetap tersimpan aman di database.]");
        }

        private static void ShowPlayerJobStats(TSPlayer player, JobUserProfile profile)
        {
            player.SendInfoMessage($"=== [c/38bdf8:STATISTIK KARIR {player.Name}] ===");
            player.SendInfoMessage($"Total Penghasilan: {JobModule.FormatCoinsShort(profile.TotalCoinsEarned)} | Total Selesai: {profile.TotalActionsCount:N0} tindakan");

            foreach (var job in profile.ActiveJobs)
            {
                int lvl = profile.GetJobLevel(job);
                long exp = profile.GetJobExp(job);
                long needed = (long)(150 * Math.Pow(lvl, 1.45));
                double bonus = (lvl - 1) * JobModule.Config.LevelBonusPercent;
                var shift = profile.GetOrCreateShift(job);

                player.SendInfoMessage($"* [c/f59e0b:{job.ToUpper()}] : Level {lvl} ({exp}/{needed} EXP) | [c/a3e635:Bonus +{bonus:0.#}% Gaji]");
                if (shift.IsOnCooldown)
                {
                    var rem = shift.ShiftCooldownUntil - DateTime.UtcNow;
                    player.SendInfoMessage($"  Status Shift: [c/fbbf24:Istirahat ({rem.Minutes}m {rem.Seconds:00}s)]");
                }
                else
                {
                    player.SendInfoMessage($"  Status Shift: [c/4ade80:Aktif] -> {shift.TargetName} ({shift.CurrentCount}/{shift.RequiredCount})");
                }
            }
        }

        private static void ShowTopWorkers(TSPlayer player)
        {
            var topWorkers = JobModule.Database.GetTopEarners(5).ToList();
            player.SendInfoMessage("=== [c/f59e0b:TOP 5 PEKERJA TERKAYA SERVER] ===");

            int rank = 1;
            foreach (var w in topWorkers)
            {
                player.SendInfoMessage($"#{rank} [c/38bdf8:{w.AccountName}] - {JobModule.FormatCoinsShort(w.TotalCoinsEarned)} ({w.TotalActionsCount:N0} tindakan)");
                rank++;
            }
        }
    }
}
