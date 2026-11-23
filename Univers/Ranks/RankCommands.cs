using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using Univers.Exp;
using Skynomi.Economy;
using Univers.Modules;

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

            Commands.ChatCommands.Add(new Command("skynomi.rank.use", CmdBossContribution, "bosscontri", "contri", "bossdamage")
            {
                HelpText = "Melihat damage kontribusi kamu pada boss yang sedang aktif di dunia."
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

            player.SendMessage($"=== STATUS KASTA COSMIC: {rank.Prefix} ===", Color.Gold);
            player.SendMessage($"  Level Karakter   : {level} / Cap {rank.MaxLevelCap} (Skala 1-1000)", Color.Yellow);
            player.SendMessage($" - Dompet Shard    : {EconomyApi.FormatColored(currentCredits)} / Limit {EconomyApi.FormatColored(rank.MaxMoneyLimitShards)} (Bank: {EconomyApi.FormatColored(currentBank)})", Color.Goldenrod);
            player.SendMessage($" - EXP Multiplier  : {rank.ExpMultiplier:F2}x Bonus EXP Server", Color.GreenYellow);
            player.SendMessage($" - Total Playtime  : {formattedPlaytime} ({minutes}m / {data.PlaytimeSeconds}s)", Color.Aqua);
            player.SendMessage($" - Boss Personal   : {data.DefeatedBosses.Count} Boss Telah Dikalahkan", Color.LightGreen);

            if (data.HasJobSelected)
            {
                player.SendMessage($" - Status Jobs     : Aktif Mendaftar ({data.JobsCompleted} Misi Selesai)", Color.Orange);
            }
            else
            {
                player.SendMessage($" - Status Jobs     : Belum Mendaftar Job (Bebas Syarat Pekerjaan)", Color.Gray);
            }

            player.SendMessage($" - Element Mastery : {data.ElementTagsCount} Tags Dipicu | {data.ElementCombosCount} Dual Combos Berhasil", Color.MediumPurple);
            player.SendMessage($" - Akses Item Tier : Maksimal Tier {rank.MaxItemTierAllowed}", Color.LightPink);
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

            bool levelPass = currentLevel >= targetRank.MinLevel;
            bool playtimePass = currentMinutes >= targetRank.MinPlaytimeMinutes;

            var missingBosses = targetRank.RequiredBosses
                .Where(b => !data.DefeatedBosses.Contains(b))
                .ToList();
            bool bossesPass = missingBosses.Count == 0;

            bool jobsPass = true;
            if (data.HasJobSelected)
            {
                jobsPass = data.JobsCompleted >= targetRank.MinJobsCompleted;
            }

            bool tagsPass = data.ElementTagsCount >= targetRank.MinElementTags;
            bool combosPass = data.ElementCombosCount >= targetRank.MinElementCombos;
            bool moneyPass = currentCredits >= targetRank.RankUpCostShards;

            if (levelPass && playtimePass && bossesPass && jobsPass && tagsPass && combosPass && moneyPass)
            {
                if (!EconomyApi.TryDebit(player.Account.Name, targetRank.RankUpCostShards, "RankUp"))
                {
                    player.SendErrorMessage($"[ X ] Gagal memotong saldo Shard! Saldo dompetmu tidak mencukupi ({EconomyApi.FormatColored(targetRank.RankUpCostShards)} dibutuhkan).");
                    return;
                }

                EconomyApi.SetWalletLimit(player.Account.Name, targetRank.MaxMoneyLimitShards);

                data.RankIndex = targetRank.Index;
                data.LastRankUpDate = DateTime.UtcNow;
                RankModule.Database.Update(data);

                RankApi.InvokeRankUp(player, player.Account.Name, targetRank.Index - 1, targetRank.Index);

                player.SendSuccessMessage($"[ + SELAMAT! ] Kamu resmi naik rank ke: {targetRank.Prefix}!");
                player.SendInfoMessage($"Batas Dompet Baru: {EconomyApi.FormatColored(targetRank.MaxMoneyLimitShards)} | Tier Item: Hingga Tier {targetRank.MaxItemTierAllowed}!");
                player.SendInfoMessage($"Kapasitas Level Terbuka hingga: {targetRank.MaxLevelCap}! EXP Multiplier: {targetRank.ExpMultiplier:F2}x!");

                TSPlayer.All.SendData(
                    PacketTypes.CreateCombatTextExtended,
                    $"RANK UP! {targetRank.Name}",
                    (int)Color.Gold.PackedValue,
                    player.X,
                    player.Y - 24
                );

                SpawnRankUpFireworks(player);

                if (RankModule.Config.BroadcastRankUp)
                {
                    TSPlayer.All.SendMessage($"[ SERVER ] {player.Name} telah naik rank ke {targetRank.Prefix}! Mari beri selamat!", Color.Goldenrod);
                }
            }
            else
            {
                string curPlaytime = FormatPlaytimeString(currentMinutes);
                string reqPlaytime = FormatPlaytimeString(targetRank.MinPlaytimeMinutes);
                string curMoneyStr = EconomyApi.FormatColored(currentCredits);
                string reqMoneyStr = EconomyApi.FormatColored(targetRank.RankUpCostShards);

                player.SendErrorMessage($"[ X SYARAT BELUM CUKUP UNTUK NAIK KE {targetRank.Name} ]:");
                player.SendMessage($"  - Level EXP (Skala 1-1000): {currentLevel}/{targetRank.MinLevel} " + (levelPass ? "[ OK ]" : "[ X ]"), levelPass ? Color.Green : Color.Red);
                player.SendMessage($"  - Total Playtime : {curPlaytime} / {reqPlaytime} " + (playtimePass ? "[ OK ]" : "[ X ]"), playtimePass ? Color.Green : Color.Red);
                player.SendMessage($"  - Biaya Shard    : {curMoneyStr} / {reqMoneyStr} " + (moneyPass ? "[ OK ]" : "[ X ]"), moneyPass ? Color.Green : Color.Red);

                if (targetRank.RequiredBosses.Count > 0)
                {
                    player.SendMessage($"  - Boss Wajib ({targetRank.RequiredBosses.Count} Boss): " + (bossesPass ? "[ OK: SEMUA KALAH ]" : $"[ X BELUM LENGKAP ({missingBosses.Count} Tersisa): {string.Join(", ", missingBosses)}]"), bossesPass ? Color.Green : Color.Red);
                }

                if (data.HasJobSelected)
                {
                    player.SendMessage($"  - Pengerjaan Jobs : {data.JobsCompleted}/{targetRank.MinJobsCompleted} Misi " + (jobsPass ? "[ OK ]" : "[ X ]"), jobsPass ? Color.Green : Color.Red);
                }
                else
                {
                    player.SendMessage($"  - Pengerjaan Jobs : Bebas Syarat [ OK: TIDAK DAFTAR JOB ]", Color.LightGreen);
                }

                if (targetRank.MinElementTags > 0)
                {
                    player.SendMessage($"  - Element Tags Terpicu : {data.ElementTagsCount}/{targetRank.MinElementTags}x " + (tagsPass ? "[ OK ]" : "[ X ]"), tagsPass ? Color.Green : Color.Red);
                }
                if (targetRank.MinElementCombos > 0)
                {
                    player.SendMessage($"  - Element Combos Terpicu : {data.ElementCombosCount}/{targetRank.MinElementCombos}x " + (combosPass ? "[ OK ]" : "[ X ]"), combosPass ? Color.Green : Color.Red);
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
                args.Player.SendMessage($"=== REKAP BENEFIT KASTA COSMIC: {r.Prefix} ===", Color.Gold);
                args.Player.SendMessage($"  Max Level Cap : {r.MaxLevelCap} | Multiplier EXP: {r.ExpMultiplier:F2}x", Color.Yellow);
                args.Player.SendMessage($" - Limit Dompet  : {EconomyApi.FormatColored(r.MaxMoneyLimitShards)} | Biaya: {EconomyApi.FormatColored(r.RankUpCostShards)}", Color.Goldenrod);
                args.Player.SendMessage($" - Item Tier Max : Tier {r.MaxItemTierAllowed}", Color.LightPink);
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
                    args.Player.SendMessage($"{r.Prefix}: Cap {r.MaxLevelCap} | Dompet {lim} | {r.ExpMultiplier:F2}x EXP | /benefit {r.Index}", Color.LightYellow);
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
                args.Player.SendMessage($"{r.Prefix} | {r.MinLevel}-{r.MaxLevelCap} | Dompet: {moneyLim} (Biaya: {cost}) | {playtimeReq} | {r.RequiredBosses.Count} Boss | Tier {r.MaxItemTierAllowed}", Color.White);
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
            player.SendInfoMessage($"[ INFO ] Syarat /rankup: Minimal kontribusi damage ke boss adalah {RankModule.Config.MinBossContributionPercent:F0}% (Anti-Gendong). Ketik /contri saat boss aktif.");
        }

        private static void CmdBossContribution(CommandArgs args)
        {
            var player = args.Player;
            if (!player.IsLoggedIn)
            {
                player.SendErrorMessage("Kamu harus login akun TShock untuk melihat kontribusi boss.");
                return;
            }

            var fights = BossContributionTracker.ActiveFights.Values.ToList();
            if (fights.Count == 0)
            {
                player.SendInfoMessage("[ INFO ] Tidak ada pertarungan Boss yang sedang berlangsung saat ini.");
                return;
            }

            double minReq = RankModule.Config.MinBossContributionPercent;
            foreach (var f in fights)
            {
                long totalDmg = f.GetTotalDamage();
                long myDmg = f.PlayerDamage.TryGetValue(player.Account.Name, out long d) ? d : 0;
                double myPct = totalDmg > 0 ? ((double)myDmg / totalDmg * 100.0) : 0.0;

                player.SendMessage($"=== [ STATUS KONTRIBUSI: {f.BossDisplayName} ] ===", Color.Gold);
                player.SendInfoMessage($"Total Damage Terkumpul: {totalDmg:N0} DMG | Peserta: {f.PlayerDamage.Count}");

                string statusColor = myPct >= minReq ? "[c/22c55e:[ QUALIFIED (>= " + minReq.ToString("F0") + "%) ]]" : $"[c/ef4444:[ BUTUH {minReq - myPct:F1}% LAGI ]]";
                player.SendMessage($"Damage Kamu: {myDmg:N0} DMG ({myPct:F1}%) -> Status: {statusColor}", Color.White);

                var topContributors = f.PlayerDamage
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .ToList();

                player.SendInfoMessage("Top Damager:");
                int rankNum = 1;
                foreach (var top in topContributors)
                {
                    string pName = f.PlayerDisplayNames.TryGetValue(top.Key, out var n) ? n : top.Key;
                    double pct = totalDmg > 0 ? ((double)top.Value / totalDmg * 100.0) : 0.0;
                    string tag = rankNum == 1 ? "[ MVP ]" : $"[ #{rankNum} ]";
                    player.SendMessage($"  {tag} {pName}: {top.Value:N0} DMG ({pct:F1}%)", rankNum == 1 ? Color.Gold : Color.LightGray);
                    rankNum++;
                }
            }
        }

        private static bool TryResolveRankAccount(string inputName, out string accountName, out TSPlayer? onlinePlayer)
        {
            onlinePlayer = TShock.Players.FirstOrDefault(p =>
                p != null && p.Active && p.IsLoggedIn &&
                p.Name.Equals(inputName, StringComparison.OrdinalIgnoreCase));
            if (onlinePlayer?.Account != null)
            {
                accountName = onlinePlayer.Account.Name;
                return true;
            }

            var acc = TShock.UserAccounts.GetUserAccountByName(inputName);
            if (acc != null)
            {
                accountName = acc.Name;
                onlinePlayer = TShock.Players.FirstOrDefault(p =>
                    p != null && p.Active && p.IsLoggedIn &&
                    string.Equals(p.Account?.Name, acc.Name, StringComparison.OrdinalIgnoreCase));
                return true;
            }

            accountName = string.Empty;
            onlinePlayer = null;
            return false;
        }

        private static void CmdSetRank(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Format: /setrank <player> <rankIndex:0-8>");
                return;
            }

            if (!TryResolveRankAccount(args.Parameters[0], out string accountName, out TSPlayer? target))
            {
                args.Player.SendErrorMessage($"Pemain/akun '{args.Parameters[0]}' tidak ditemukan!");
                return;
            }

            if (!int.TryParse(args.Parameters[1], out int rankIdx))
            {
                args.Player.SendErrorMessage("Index rank harus berupa angka 0-8!");
                return;
            }

            rankIdx = Math.Clamp(rankIdx, 0, Math.Max(0, RankModule.Config.Ranks.Count - 1));
            var rankInfo = RankModule.Config.GetRankByIndex(rankIdx);

            var data = RankModule.Database.GetOrCreate(accountName);
            data.RankIndex = rankIdx;
            RankModule.Database.Update(data);
            EconomyApi.SetWalletLimit(accountName, rankInfo.MaxMoneyLimitShards);

            args.Player.SendSuccessMessage($"Berhasil mengubah rank {accountName} ke index {rankIdx} ({rankInfo.Name})!");
            target?.SendSuccessMessage($"[ ADMIN ] Rank kamu diatur ke {rankInfo.Name} oleh admin!");
        }

        private static void CmdAddBoss(CommandArgs args)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Format: /giveboss <player> <bossKey>");
                return;
            }

            if (!TryResolveRankAccount(args.Parameters[0], out string accountName, out TSPlayer? target))
            {
                args.Player.SendErrorMessage($"Pemain/akun '{args.Parameters[0]}' tidak ditemukan!");
                return;
            }

            string bossKey = args.Parameters[1];
            var data = RankModule.Database.GetOrCreate(accountName);
            if (!data.DefeatedBosses.Contains(bossKey))
            {
                data.DefeatedBosses.Add(bossKey);
                RankModule.Database.Update(data);
                args.Player.SendSuccessMessage($"Berhasil menambahkan boss '{bossKey}' ke catatan {accountName}!");
                target?.SendSuccessMessage($"[ ADMIN ] Catatan boss '{bossKey}' ditambahkan ke profilmu!");
            }
            else
            {
                args.Player.SendInfoMessage($"Akun {accountName} sudah memiliki catatan boss '{bossKey}'.");
            }
        }

        private static void CmdSetJobStatus(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Format: /setjobstatus <player> <hasJob:true/false> <completedCount>");
                return;
            }

            if (!TryResolveRankAccount(args.Parameters[0], out string accountName, out TSPlayer? target))
            {
                args.Player.SendErrorMessage($"Pemain/akun '{args.Parameters[0]}' tidak ditemukan!");
                return;
            }

            if (bool.TryParse(args.Parameters[1], out bool hasJob) && int.TryParse(args.Parameters[2], out int count))
            {
                var data = RankModule.Database.GetOrCreate(accountName);
                data.HasJobSelected = hasJob;
                data.JobsCompleted = count;
                RankModule.Database.Update(data);
                args.Player.SendSuccessMessage($"Berhasil mengatur status job {accountName}: HasJob={hasJob}, Completed={count}");
                target?.SendInfoMessage($"[ ADMIN ] Status job kamu diatur: HasJob={hasJob}, Completed={count}");
            }
            else
            {
                args.Player.SendErrorMessage("Format: /setjobstatus <player> <true/false> <angka>");
            }
        }

        private static void CmdSetElementStats(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("Format: /setelemstats <player> <tagsCount> <combosCount>");
                return;
            }

            if (!TryResolveRankAccount(args.Parameters[0], out string accountName, out TSPlayer? target))
            {
                args.Player.SendErrorMessage($"Pemain/akun '{args.Parameters[0]}' tidak ditemukan!");
                return;
            }

            if (int.TryParse(args.Parameters[1], out int tags) && int.TryParse(args.Parameters[2], out int combos))
            {
                var data = RankModule.Database.GetOrCreate(accountName);
                data.ElementTagsCount = tags;
                data.ElementCombosCount = combos;
                RankModule.Database.Update(data);
                args.Player.SendSuccessMessage($"Berhasil mengatur stats elemen {accountName}: Tags={tags}, Combos={combos}");
                target?.SendInfoMessage($"[ ADMIN ] Stats elemen kamu diatur: Tags={tags}, Combos={combos}");
            }
            else
            {
                args.Player.SendErrorMessage("Format: /setelemstats <player> <tagsCount> <combosCount>");
            }
        }

        private static void SpawnRankUpFireworks(TSPlayer player)
        {
            if (player == null || !player.Active || player.TPlayer == null) return;

            Vector2 top = player.TPlayer.Top;
            Vector2 center = player.TPlayer.Center;
            Vector2 bottom = player.TPlayer.Bottom;

            // 1. 4 Roket Kembang Api Warna-Warni menyebar ke langit (0 damage)
            int[] rockets =
            {
                ProjectileID.RocketFireworksBoxRed,
                ProjectileID.RocketFireworksBoxGreen,
                ProjectileID.RocketFireworksBoxBlue,
                ProjectileID.RocketFireworksBoxYellow
            };
            float[] rocketVx = { -3.2f, -1.1f, 1.1f, 3.2f };
            float[] rocketVy = { -9.5f, -10.5f, -10.5f, -9.5f };

            for (int i = 0; i < rockets.Length; i++)
            {
                SpawnCelebrationProjectile(top, new Vector2(rocketVx[i], rocketVy[i]), rockets[i]);
            }

            // 2. 2 Air Mancur Kembang Api Pelangi di kiri & kanan pemain selama ~3 detik
            SpawnCelebrationProjectile(bottom + new Vector2(-40f, -12f), Vector2.Zero, ProjectileID.FireworkFountainRainbow, 180);
            SpawnCelebrationProjectile(bottom + new Vector2(40f, -12f), Vector2.Zero, ProjectileID.FireworkFountainRainbow, 180);

            // 3. 6 Semburan Confetti meriah di sekitar pemain
            float[] confettiVx = { -6.0f, -3.5f, -1.2f, 1.2f, 3.5f, 6.0f };
            for (int i = 0; i < confettiVx.Length; i++)
            {
                SpawnCelebrationProjectile(center, new Vector2(confettiVx[i], -6.5f), ProjectileID.ConfettiGun);
            }
        }

        private static void SpawnCelebrationProjectile(Vector2 pos, Vector2 vel, int projId, int? timeLeftOverride = null)
        {
            int idx = Projectile.NewProjectile(Projectile.GetNoneSource(), pos, vel, projId, 0, 0f, Main.myPlayer);
            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                if (timeLeftOverride.HasValue)
                {
                    Main.projectile[idx].timeLeft = timeLeftOverride.Value;
                }
                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }
    }
}
