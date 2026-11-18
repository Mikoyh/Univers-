using System;
using Microsoft.Xna.Framework;
using TShockAPI;
using Skynomi.Modules;

namespace Skynomi.Exp
{
    public static class ExpCommands
    {
        public static void Initialize()
        {
            Commands.ChatCommands.Add(new Command("skynomi.exp.use", ExpCommand, "exp", "level", "lvl", "myexp")
            {
                HelpText = "Melihat level dan progres EXP kamu saat ini."
            });

            Commands.ChatCommands.Add(new Command("skynomi.exp.use", TopLevelCommand, "toplvl", "exptop", "leveltop")
            {
                HelpText = "Melihat papan peringkat (leaderboard) level tertinggi di server."
            });

            Commands.ChatCommands.Add(new Command("skynomi.exp.admin", ExpAdminCommand, "expadmin", "leveladmin")
            {
                HelpText = "Perintah admin untuk mengatur atau menambah EXP pemain."
            });
        }

        private static void ExpCommand(CommandArgs args)
        {
            if (!args.Player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("Kamu harus login akun untuk melihat level & EXP!");
                return;
            }

            var data = ExpModule.Database.GetOrCreate(args.Player.Account.Name);
            long req = ExpModule.Config.CalculateRequiredExp(data.Level);
            int badgeId = ExpModule.Config.GetBadgeIdForLevel(data.Level);
            string hex = ExpModule.Config.GetLevelColorForLevel(data.Level);

            double pct = Math.Min(100.0, ((double)data.CurrentExp / req) * 100.0);
            string progressBar = GenerateProgressBar(pct);

            args.Player.SendMessage($"=== [c/{hex}:STATUS LEVEL & EXP] ===", Color.Gold);
            args.Player.SendMessage($"Pemain: [c/ffffff:{args.Player.Name}]  |  Badge: [i:{badgeId}] Level [c/{hex}:{data.Level}]", Color.White);
            args.Player.SendMessage($"Progres: {progressBar} {pct:0.0}%", Color.Cyan);
            args.Player.SendMessage($"EXP Saat Ini: [c/22d3ee:{data.CurrentExp:N0}] / [c/f59e0b:{req:N0}] EXP", Color.LightGray);
            args.Player.SendMessage($"Total EXP Karir: {data.TotalExp:N0}  |  Monster Dibunuh: {data.MobsKilled:N0}  |  Boss: {data.BossesKilled:N0}", Color.LightSteelBlue);
        }

        private static void TopLevelCommand(CommandArgs args)
        {
            var topList = ExpModule.Database.GetTopPlayers(10);

            args.Player.SendMessage("=== [c/fbbf24:PAPAN PERINGKAT TOP 10 LEVEL SERVER] ===", Color.Gold);
            int rank = 1;
            foreach (var item in topList)
            {
                int bId = ExpModule.Config.GetBadgeIdForLevel(item.Level);
                string col = ExpModule.Config.GetLevelColorForLevel(item.Level);
                args.Player.SendMessage($"#{rank}. [i:{bId}] [c/{col}:Lv.{item.Level}] [c/ffffff:{item.AccountName}] - {item.TotalExp:N0} EXP (Kills: {item.MobsKilled})", Color.White);
                rank++;
            }
        }

        private static void ExpAdminCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0 || args.Parameters[0].ToLower() == "help")
            {
                args.Player.SendMessage("=== Skynomi EXP Admin Commands ===", Color.Red);
                args.Player.SendMessage("/expadmin add <player> <amount> - Menambah EXP pemain", Color.Yellow);
                args.Player.SendMessage("/expadmin set <player> <level>  - Mengatur level pemain langsung", Color.Yellow);
                args.Player.SendMessage("/expadmin reload                - Muat ulang ExpConfig.json", Color.Yellow);
                return;
            }

            string sub = args.Parameters[0].ToLower();
            switch (sub)
            {
                case "reload":
                    ExpConfig.Read();
                    args.Player.SendSuccessMessage("[Skynomi.Exp] Konfigurasi EXP berhasil dimuat ulang!");
                    break;

                case "add":
                    if (args.Parameters.Count < 3 || !int.TryParse(args.Parameters[2], out int addExp))
                    {
                        args.Player.SendErrorMessage("Format: /expadmin add <player> <jumlah_exp>");
                        return;
                    }
                    string target = args.Parameters[1];
                    var tPlayers = TSPlayer.FindByNameOrID(target);
                    if (tPlayers.Count == 0)
                    {
                        args.Player.SendErrorMessage($"Player '{target}' tidak ditemukan di server.");
                        return;
                    }
                    var tPlayer = tPlayers[0];
                    var data = ExpModule.Database.GetOrCreate(tPlayer.Account.Name);
                    ExpModule.AddExp(tPlayer, data, addExp, true);
                    args.Player.SendSuccessMessage($"[Skynomi.Exp] Berhasil menambahkan {addExp:N0} EXP ke {tPlayer.Name}!");
                    break;

                case "set":
                    if (args.Parameters.Count < 3 || !int.TryParse(args.Parameters[2], out int setLevel))
                    {
                        args.Player.SendErrorMessage("Format: /expadmin set <player> <1-1000>");
                        return;
                    }
                    setLevel = Math.Clamp(setLevel, 1, 1000);
                    string sTarget = args.Parameters[1];
                    var sPlayers = TSPlayer.FindByNameOrID(sTarget);
                    if (sPlayers.Count == 0)
                    {
                        args.Player.SendErrorMessage($"Player '{sTarget}' tidak ditemukan.");
                        return;
                    }
                    var sPlayer = sPlayers[0];
                    var sData = ExpModule.Database.GetOrCreate(sPlayer.Account.Name);
                    sData.Level = setLevel;
                    sData.CurrentExp = 0;
                    ExpModule.Database.Save(sData);
                    args.Player.SendSuccessMessage($"[Skynomi.Exp] Berhasil mengatur level {sPlayer.Name} ke Level {setLevel} / 1000!");
                    sPlayer.SendMessage($"[Skynomi.Exp] Level kamu telah diatur oleh Admin ke Level {setLevel}!", Color.Green);
                    break;
            }
        }

        private static string GenerateProgressBar(double percent)
        {
            int totalBars = 10;
            int filled = (int)Math.Round((percent / 100.0) * totalBars);
            filled = Math.Clamp(filled, 0, totalBars);
            string bar = "";
            for (int i = 0; i < filled; i++) bar += "█";
            for (int i = filled; i < totalBars; i++) bar += "░";
            return $"[{bar}]";
        }
    }
}
