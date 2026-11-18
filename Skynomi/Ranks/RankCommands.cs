using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TShockAPI;
using Skynomi.Exp;
using Skynomi.Economy;
using Skynomi.Modules;

namespace Skynomi.Ranks
{
    public static class RankCommands
    {
        public static void Initialize()
        {
            Commands.ChatCommands.Add(new Command("skynomi.rank.use", CmdRank, "rank", "pangkat")
            {
                HelpText = "Melihat status rank, limit uang dompet, playtime, boss personal, jobs, dan element mastery."
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.use", CmdRankUp, "rankup", "naikrank")
            {
                HelpText = "Mencoba naik rank ke kasta selanjutnya (memeriksa Level, Playtime, Bosskill, Jobs, Tags/Combos, dan Koin)."
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.use", CmdRanks, "ranks", "daftarrank")
            {
                HelpText = "Melihat semua 9 tingkatan kasta Cosmic dan batas saldo dompetnya."
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.use", CmdBenefits, "rankbenefits", "benefit")
            {
                HelpText = "Melihat rekap seluruh benefit kasta server: /benefit [rankIndex]"
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.use", CmdBosses, "bosses", "mybosses")
            {
                HelpText = "Melihat daftar boss Terraria yang telah kamu kalahkan secara personal."
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.admin", CmdSetRank, "setrank")
            {
                HelpText = "Admin: /setrank <player> <rankIndex> - Mengubah rank pemain secara paksa."
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.admin", CmdAddBoss, "giveboss", "addboss")
            {
                HelpText = "Admin: /giveboss <player> <bossId> - Memberikan catatan boss personal ke pemain."
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.admin", CmdSetJobStatus, "setjobstatus")
            {
                HelpText = "Admin: /setjobstatus <player> <hasJob:true/false> <completedCount>"
            });

            Commands.ChatCommands.Add(new Command("skynomi.rank.admin", CmdSetElementStats, "setelemstats")
            {
                HelpText = "Admin: /setelemstats <player> <tagsCount> <combosCount>"
            });
        }

        public static string FormatPlaytimeString(long totalMinutes)
        {
            long days = totalMinutes / 1440;
            long hours = (totalMinutes % 1440) / 60;
            long mins = totalMinutes % 60;

            var parts = new List<string>();
            if (days > 0) parts.Add($"{days} Hari");
            if (hours > 0) parts.Add($"{hours} Jam");
            if (mins > 0 || parts.Count == 0) parts.Add($"{mins} Menit");

            return string.Join(" ", parts);
        }

        private static void CmdRank(CommandArgs args)
        {
            var player = args.Player;
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("Kamu harus login akun TShock untuk melihat data rank!");
                return;
            }

            var data = RankModule.Database.GetOrCreate(player.Account.Name);
            var rank = RankModule.Config.GetRankByIndex(data.RankIndex);
            int level = ExpApi.GetLevel(player);
            long minutes = data.PlaytimeSeconds / 60;
            string formattedPlaytime = FormatPlaytimeString(minutes);
            long currentCredits = EconomyApi.GetBalance(player.Account.Name);
            long currentBank = EconomyApi.GetBankBalance(player.Account.Name);

            player.SendMessage($"=== STATUS KASTA COSMIC: {rank.Name} {rank.Prefix} ===", Color.Gold);
            player.SendMessage($"• Level Karakter   : Lv. {level} / Cap Lv. {rank.MaxLevelCap} (Server Scale 1-1000)", Color.Yellow);
            player.SendMessage($"• Dompet Shard     : {EconomyApi.FormatColored(currentCredits)} / Limit {EconomyApi.FormatColored(rank.MaxMoneyLimitShards)} (Bank: {EconomyApi.FormatColored(currentBank)})", Color.Goldenrod);
            player.SendMessage($"• EXP Multiplier   : {rank.ExpMultiplier:F2}x Bonus EXP Server", Color.GreenYellow);
            player.SendMessage($"• Total Playtime   : {formattedPlaytime} ({minutes}m / {data.PlaytimeSeconds}s)", Color.Aqua);
            player.SendMessage($"• Boss Personal    : {data.DefeatedBosses.Count} Boss Telah Dikalahkan", Color.LightGreen);

            // Status Job
            if (data.HasJobSelected)
            {
                player.SendMessage($"• Status Jobs      : Aktif Mendaftar ({data.JobsCompleted} Misi Selesai)", Color.Orange);
            }
            else
            {
                player.SendMessage($"• Status Jobs      : Belum Mendaftar Job (Bebas Syarat Pekerjaan)", Color.Gray);
            }

            // Status Element Tag & Combo
            player.SendMessage($"• Element Mastery  : {data.ElementTagsCount} Tags Dipicu | {data.ElementCombosCount} Dual Combos Berhasil", Color.MediumPurple);
            player.SendMessage($"• Akses Item Tier  : Maksimal Tier {rank.MaxItemTierAllowed}", Color.LightPink);
            player.SendMessage($"Ketik /rankup untuk mencoba naik rank atau /benefit untuk melihat rekap keuntungan!", Color.LightYellow);
        }

        private static void CmdRankUp(CommandArgs args)
        {
            var player = args.Player;
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("[ ERROR ] Kamu harus login untuk melakukan /rankup!");
                return;
            }

            var data = RankModule.Database.GetOrCreate(player.Account.Name);
            int nextIndex = data.RankIndex + 1;
            var targetRank = RankModule.Config.Ranks.FirstOrDefault(r => r.Index == nextIndex);

            if (targetRank == null)
            {
                player.SendSuccessMessage($"[ MAX RANK ] Selamat! Kamu sudah berada di kasta tertinggi semesta: {RankModule.Config.GetRankByIndex(data.RankIndex).Name}!");
                return;
            }

            int currentLevel = ExpApi.GetLevel(player);
            long currentMinutes = data.PlaytimeSeconds / 60;
            long currentCredits = EconomyApi.GetBalance(player.Account.Name);

            // 1. Syarat Level (Skala 1-1000, dimulai ramah Lv. 30 untuk Mortal)
            bool levelPass = currentLevel >= targetRank.MinLevel;

            // 2. Syarat Playtime (Menit aktif)
            bool playtimePass = currentMinutes >= targetRank.MinPlaytimeMinutes;

            // 3. Syarat Bosskill Personal
            var missingBosses = targetRank.RequiredBosses
                .Where(b => !data.DefeatedBosses.Contains(b))
                .ToList();
            bool bossesPass = missingBosses.Count == 0;

            // 4. Syarat Jobs (KONDISIONAL: jika player tidak memilih/daftar job, syarat batal & exempt)
            bool jobsPass = true;
            if (data.HasJobSelected)
            {
                jobsPass = data.JobsCompleted >= targetRank.MinJobsCompleted;
            }

            // 5. Syarat Element Tags & Combos
            bool tagsPass = data.ElementTagsCount >= targetRank.MinElementTags;
            bool combosPass = data.ElementCombosCount >= targetRank.MinElementCombos;

            // 6. Syarat Biaya Uang Skynomi Shard
            bool moneyPass = currentCredits >= targetRank.RankUpCostShards;

            if (levelPass && playtimePass && bossesPass && jobsPass && tagsPass && combosPass && moneyPass)
            {
                // Potong saldo Skynomi Shards via EconomyApi
                if (!EconomyApi.TryDebit(player.Account.Name, targetRank.RankUpCostShards, "RankUp"))
                {
                    player.SendErrorMessage($"[ X ] Gagal memotong saldo Shard! Saldo dompetmu tidak mencukupi ({EconomyApi.FormatColored(targetRank.RankUpCostShards)} dibutuhkan).");
                    return;
                }

                // Perbarui batas dompet akun ekonomi sesuai kasta baru
                EconomyApi.SetWalletLimit(player.Account.Name, targetRank.MaxMoneyLimitShards);

                // SUKSES NAIK RANK!
                data.RankIndex = targetRank.Index;
                data.LastRankUpDate = DateTime.UtcNow;
                RankModule.Database.Update(data);

                // Pemicu Event
                RankApi.InvokeRankUp(player, player.Account.Name, targetRank.Index - 1, targetRank.Index);

                player.SendSuccessMessage($"[ + SELAMAT! ] Kamu resmi naik rank ke: {targetRank.Name} {targetRank.Prefix}!");
                player.SendInfoMessage($"Batas Dompet Baru: {EconomyApi.FormatColored(targetRank.MaxMoneyLimitShards)} | Tier Item: Hingga Tier {targetRank.MaxItemTierAllowed}!");
                player.SendInfoMessage($"Kapasitas Level Terbuka hingga: Lv. {targetRank.MaxLevelCap}! EXP Multiplier: {targetRank.ExpMultiplier:F2}x!");
                player.SendData(PacketTypes.CreateCombatTextExtended, $"RANK UP! {targetRank.Prefix}", (int)Color.Gold.PackedValue, player.X, player.Y - 20);

                if (RankModule.Config.BroadcastRankUp)
                {
                    TSPlayer.All.SendMessage($"[ SERVER ] {player.Name} telah naik kasta ke {targetRank.Name}! Mari beri selamat!", Color.Goldenrod);
                }
            }
            else
            {
                string curPlaytime = FormatPlaytimeString(currentMinutes);
                string reqPlaytime = FormatPlaytimeString(targetRank.MinPlaytimeMinutes);
                string curMoneyStr = EconomyApi.FormatColored(currentCredits);
                string reqMoneyStr = EconomyApi.FormatColored(targetRank.RankUpCostShards);

                player.SendErrorMessage($"[ X SYARAT BELUM CUKUP UNTUK NAIK KE {targetRank.Name} ]:");
                player.SendMessage($"  • Level EXP (Skala 1-1000): {currentLevel}/{targetRank.MinLevel} " + (levelPass ? "[ OK ]" : "[ X ]"), levelPass ? Color.Green : Color.Red);
                player.SendMessage($"  • Total Playtime : {curPlaytime} / {reqPlaytime} " + (playtimePass ? "[ OK ]" : "[ X ]"), playtimePass ? Color.Green : Color.Red);
                player.SendMessage($"  • Biaya Shard    : {curMoneyStr} / {reqMoneyStr} " + (moneyPass ? "[ OK ]" : "[ X ]"), moneyPass ? Color.Green : Color.Red);

                if (targetRank.RequiredBosses.Count > 0)
                {
                    player.SendMessage($"  • Boss Wajib ({targetRank.RequiredBosses.Count} Boss): " + (bossesPass ? "[ OK: SEMUA KALAH ]" : $"[ X BELUM LENGKAP ({missingBosses.Count} Tersisa): {string.Join(", ", missingBosses)}]"), bossesPass ? Color.Green : Color.Red);
                }

                // Info Jobs
                if (data.HasJobSelected)
                {
                    player.SendMessage($"  • Pengerjaan Jobs : {data.JobsCompleted}/{targetRank.MinJobsCompleted} Misi " + (jobsPass ? "[ OK ]" : "[ X ]"), jobsPass ? Color.Green : Color.Red);
                }
                else
                {
                    player.SendMessage($"  • Pengerjaan Jobs : Bebas Syarat [ OK: TIDAK DAFTAR JOB ]", Color.LightGreen);
                }

                // Info Element Tags & Combos
                if (targetRank.MinElementTags > 0)
                {
                    player.SendMessage($"  • Element Tags Terpicu : {data.ElementTagsCount}/{targetRank.MinElementTags}x " + (tagsPass ? "[ OK ]" : "[ X ]"), tagsPass ? Color.Green : Color.Red);
                }
                if (targetRank.MinElementCombos > 0)
                {
                    player.SendMessage($"  • Element Combos Terpicu : {data.ElementCombosCount}/{targetRank.MinElementCombos}x " + (combosPass ? "[ OK ]" : "[ X ]"), combosPass ? Color.Green : Color.Red);
                }
            }
        }

        private static void CmdBenefits(CommandArgs args)
        {
            int targetIdx = -1;
            if (args.Parameters.Count > 0 && int.TryParse(args.Parameters[0], out int parsed))
            {
                targetIdx = parsed;
            }

            if (targetIdx >= 0)
            {
                var r = RankModule.Config.GetRankByIndex(targetIdx);
                args.Player.SendMessage($"=== REKAP BENEFIT KASTA COSMIC: {r.Name} {r.Prefix} ===", Color.Gold);
                args.Player.SendMessage($"• Max Level Cap : Lv. {r.MaxLevelCap} | Multiplier EXP: {r.ExpMultiplier:F2}x", Color.Yellow);
                args.Player.SendMessage($"• Limit Dompet  : {EconomyApi.FormatColored(r.MaxMoneyLimitShards)} | Biaya: {EconomyApi.FormatColored(r.RankUpCostShards)}", Color.Goldenrod);
                args.Player.SendMessage($"• Item Tier Max : Tier {r.MaxItemTierAllowed}", Color.LightPink);
                if (r.Benefits != null && r.Benefits.Count > 0)
                {
                    args.Player.SendMessage("  Keuntungan (Benefits):", Color.LightGreen);
                    foreach (var b in r.Benefits)
                    {
                        args.Player.SendMessage($"    [ + ] {b}", Color.White);
                    }
                }
                if (r.Constraints != null && r.Constraints.Count > 0)
                {
                    args.Player.SendMessage("  Batasan (Constraints):", Color.Coral);
                    foreach (var c in r.Constraints)
                    {
                        args.Player.SendMessage($"    [ - ] {c}", Color.LightGray);
                    }
                }
            }
            else
            {
                args.Player.SendMessage("=== REKAP 9 TINGKATAN KASTA COSMIC UNIVERS ===", Color.Cyan);
                foreach (var r in RankModule.Config.Ranks.OrderBy(x => x.Index))
                {
                    string lim = RankModule.FormatMoneyString(r.MaxMoneyLimitCopper);
                    args.Player.SendMessage($"{r.Prefix} {r.Name}: Cap Lv.{r.MaxLevelCap} | Dompet {lim} | {r.ExpMultiplier:F2}x EXP | Ketik /benefit {r.Index} untuk detail", Color.LightYellow);
                }
            }
        }

        private static void CmdRanks(CommandArgs args)
        {
            args.Player.SendMessage("=== DAFTAR 9 KASTA COSMIC SERVER UNIVERS ===", Color.Cyan);
            foreach (var r in RankModule.Config.Ranks.OrderBy(x => x.Index))
            {
                string playtimeReq = FormatPlaytimeString(r.MinPlaytimeMinutes);
                string moneyLim = RankModule.FormatMoneyString(r.MaxMoneyLimitCopper);
                string cost = RankModule.FormatMoneyString(r.RankUpCostCopper);
                args.Player.SendMessage($"{r.Prefix} {r.Name} | Lv.{r.MinLevel}-{r.MaxLevelCap} | Dompet: {moneyLim} (Biaya: {cost}) | {playtimeReq} | {r.RequiredBosses.Count} Boss | Tier {r.MaxItemTierAllowed}", Color.White);
            }
        }

        private static void CmdBosses(CommandArgs args)
        {
            var player = args.Player;
            if (!player.IsLoggedIn) return;

            var data = RankModule.Database.GetOrCreate(player.Account.Name);
            player.SendMessage("=== DAFTAR BOSS PERSONAL YANG TELAH DIKALAHKAN ===", Color.Gold);
            if (data.DefeatedBosses.Count == 0)
            {
                player.SendMessage("Kamu belum mengalahkan satupun boss secara personal! Ikutlah berburu boss bersama kawan server!", Color.Gray);
            }
            else
            {
                player.SendMessage(string.Join(", ", data.DefeatedBosses), Color.LightGreen);
            }
        }

        private static void CmdSetRank(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Format: /setrank <player> <rankIndex:0-8>");
                return;
            }

            var target = TShock.Players.FirstOrDefault(p => p != null && p.Name.Equals(args.Parameters[0], StringComparison.OrdinalIgnoreCase));
            if (target == null || !target.IsLoggedIn)
            {
                args.Player.SendErrorMessage("Pemain tidak ditemukan atau belum login!");
                return;
            }

            if (!int.TryParse(args.Parameters[1], out int rankIdx))
            {
                args.Player.SendErrorMessage("Index rank harus berupa angka 0-8!");
                return;
            }

            var data = RankModule.Database.GetOrCreate(target.Account.Name);
            data.RankIndex = rankIdx;
            RankModule.Database.Update(data);

            args.Player.SendSuccessMessage($"Berhasil mengubah rank {target.Name} ke index {rankIdx} ({RankModule.Config.GetRankByIndex(rankIdx).Name})!");
            target.SendSuccessMessage($"[ ADMIN ] Rank kamu diatur ke {RankModule.Config.GetRankByIndex(rankIdx).Name} oleh admin!");
        }

        private static void CmdAddBoss(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Format: /giveboss <player> <bossKey>");
                return;
            }

            var target = TShock.Players.FirstOrDefault(p => p != null && p.Name.Equals(args.Parameters[0], StringComparison.OrdinalIgnoreCase));
            if (target == null || !target.IsLoggedIn) return;

            string bossKey = args.Parameters[1];
            var data = RankModule.Database.GetOrCreate(target.Account.Name);
            if (!data.DefeatedBosses.Contains(bossKey))
            {
                data.DefeatedBosses.Add(bossKey);
                RankModule.Database.Update(data);
                args.Player.SendSuccessMessage($"Berhasil menambahkan boss '{bossKey}' ke catatan {target.Name}!");
            }
        }

        private static void CmdSetJobStatus(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Format: /setjobstatus <player> <hasJob:true/false> <completedCount>");
                return;
            }

            var target = TShock.Players.FirstOrDefault(p => p != null && p.Name.Equals(args.Parameters[0], StringComparison.OrdinalIgnoreCase));
            if (target == null || !target.IsLoggedIn) return;

            if (bool.TryParse(args.Parameters[1], out bool hasJob) && int.TryParse(args.Parameters[2], out int count))
            {
                var data = RankModule.Database.GetOrCreate(target.Account.Name);
                data.HasJobSelected = hasJob;
                data.JobsCompleted = count;
                RankModule.Database.Update(data);
                args.Player.SendSuccessMessage($"Berhasil mengatur status job {target.Name}: HasJob={hasJob}, Completed={count}");
            }
        }

        private static void CmdSetElementStats(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Format: /setelemstats <player> <tagsCount> <combosCount>");
                return;
            }

            var target = TShock.Players.FirstOrDefault(p => p != null && p.Name.Equals(args.Parameters[0], StringComparison.OrdinalIgnoreCase));
            if (target == null || !target.IsLoggedIn) return;

            if (int.TryParse(args.Parameters[1], out int tags) && int.TryParse(args.Parameters[2], out int combos))
            {
                var data = RankModule.Database.GetOrCreate(target.Account.Name);
                data.ElementTagsCount = tags;
                data.ElementCombosCount = combos;
                RankModule.Database.Update(data);
                args.Player.SendSuccessMessage($"Berhasil mengatur stats elemen {target.Name}: Tags={tags}, Combos={combos}");
            }
        }
    }
}
