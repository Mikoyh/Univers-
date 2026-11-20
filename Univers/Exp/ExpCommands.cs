using System;
using System.Linq;
using Microsoft.Xna.Framework;
using TShockAPI;
using Univers.Modules;

namespace Univers.Exp
{
    public static class ExpCommands
    {
        public static void Initialize()
        {
            Commands.ChatCommands.Add(new Command("univers.exp.use", ExpCommand, "exp", "level", "lvl", "myexp")
            {
                HelpText = "Melihat level dan progres EXP kamu saat ini."
            });

            Commands.ChatCommands.Add(new Command("univers.exp.use", TopLevelCommand, "toplvl", "exptop", "leveltop")
            {
                HelpText = "Melihat papan peringkat (leaderboard) level tertinggi di server."
            });

            Commands.ChatCommands.Add(new Command("univers.exp.admin", ExpAdminCommand, "expadmin", "leveladmin")
            {
                HelpText = "Perintah admin untuk mengatur atau menambah EXP pemain."
            });
        }

        private static void ExpCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.Active || !player.IsLoggedIn)
            {
                args.Player?.SendErrorMessage("[ ERROR ] Kamu harus login akun untuk melihat level & EXP!");
                return;
            }

            var data = ExpModule.Database.GetOrCreate(player.Account.Name);
            long req = ExpModule.Config.CalculateRequiredExp(data.Level);
            int badgeId = ExpModule.Config.GetBadgeIdForLevel(data.Level);
            string hex = ExpModule.Config.GetLevelColorForLevel(data.Level);

            double pct = Math.Min(100.0, ((double)data.CurrentExp / req) * 100.0);
            string progressBar = GenerateProgressBar(pct);

            player.SendMessage($"=== [c/{hex}:STATUS LEVEL & EXP] ===", Color.Gold);
            player.SendMessage($"Pemain: [c/ffffff:{player.Name}]  |  Badge: [i:{badgeId}] Level [c/{hex}:{data.Level}]", Color.White);
            player.SendMessage($"Progres: {progressBar} {pct:0.0}%", Color.Cyan);
            player.SendMessage($"EXP Saat Ini: [c/22d3ee:{data.CurrentExp:N0}] / [c/f59e0b:{req:N0}] EXP", Color.LightGray);
            player.SendMessage($"Total EXP Karir: {data.TotalExp:N0}  |  Monster Dibunuh: {data.MobsKilled:N0}  |  Boss: {data.BossesKilled:N0}", Color.LightSteelBlue);
        }

        private static void TopLevelCommand(CommandArgs args)
        {
            if (args.Player == null) return;
            var topList = ExpModule.Database.GetTopPlayers(10);

            args.Player.SendMessage("=== [c/fbbf24:PAPAN PERINGKAT TOP 10 LEVEL SERVER] ===", Color.Gold);
            int rank = 1;
            foreach (var item in topList)
            {
                int bId = ExpModule.Config.GetBadgeIdForLevel(item.Level);
                string col = ExpModule.Config.GetLevelColorForLevel(item.Level);
                args.Player.SendMessage($"#{rank}. [i:{bId}] [c/{col}:[][c/{col}:{item.Level}][c/{col}:]] [c/ffffff:{item.AccountName}] - {item.TotalExp:N0} EXP (Kills: {item.MobsKilled})", Color.White);
                rank++;
            }
        }

        private static void ExpAdminCommand(CommandArgs args)
        {
            if (args.Player == null) return;
            if (!args.Player.HasPermission("univers.exp.admin") && !args.Player.HasPermission("skynomi.exp.admin"))
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu tidak memiliki izin untuk menggunakan perintah ini!");
                return;
            }

            if (args.Parameters.Count == 0 || args.Parameters[0].ToLower() == "help")
            {
                args.Player.SendMessage("=== Univers EXP Admin Commands ===", Color.Red);
                args.Player.SendMessage("/expadmin add <player> <amount> - Menambah EXP pemain", Color.Yellow);
                args.Player.SendMessage("/expadmin set <player> <level>  - Mengatur level pemain & ElementLevel langsung", Color.Yellow);
                args.Player.SendMessage("/expadmin reload                - Muat ulang ExpConfig.json", Color.Yellow);
                return;
            }

            string sub = args.Parameters[0].ToLower();
            switch (sub)
            {
                case "reload":
                    ExpModule.Instance?.Reload(new TShockAPI.Hooks.ReloadEventArgs(args.Player));
                    args.Player.SendSuccessMessage("[ OK ] [Univers.Exp] Konfigurasi EXP berhasil dimuat ulang!");
                    break;

                case "add":
                    if (args.Parameters.Count < 3 || !int.TryParse(args.Parameters[2], out int addExp))
                    {
                        args.Player.SendErrorMessage("[ FORMAT ] /expadmin add <player> <jumlah_exp>");
                        return;
                    }
                    string target = args.Parameters[1];
                    var tPlayers = TSPlayer.FindByNameOrID(target);
                    if (tPlayers.Count == 0)
                    {
                        args.Player.SendErrorMessage($"[ ERROR ] Player '{target}' tidak ditemukan online di server.");
                        return;
                    }
                    var tPlayer = tPlayers[0];
                    if (!tPlayer.IsLoggedIn)
                    {
                        args.Player.SendErrorMessage($"[ ERROR ] Player '{tPlayer.Name}' belum login.");
                        return;
                    }
                    var data = ExpModule.Database.GetOrCreate(tPlayer.Account.Name);
                    ExpModule.AddExp(tPlayer, data, addExp, true);
                    args.Player.SendSuccessMessage($"[ OK ] [Univers.Exp] Berhasil menambahkan {addExp:N0} EXP ke {tPlayer.Name}!");
                    break;

                case "set":
                    if (args.Parameters.Count < 3 || !int.TryParse(args.Parameters[2], out int setLevel))
                    {
                        args.Player.SendErrorMessage("[ FORMAT ] /expadmin set <player> <1-1000>");
                        return;
                    }

                    setLevel = Math.Clamp(setLevel, 1, 1000);
                    string sTarget = args.Parameters[1];

                    TSPlayer? sPlayer = null;
                    string? accountName = null;

                    var sPlayers = TSPlayer.FindByNameOrID(sTarget);
                    if (sPlayers.Count > 0 && sPlayers[0].IsLoggedIn)
                    {
                        sPlayer = sPlayers[0];
                        accountName = sPlayer.Account.Name;
                    }
                    else
                    {
                        var matchedAccount = TShock.UserAccounts.GetUserAccountByName(sTarget);
                        if (matchedAccount != null)
                        {
                            accountName = matchedAccount.Name;
                            sPlayer = TShock.Players.FirstOrDefault(p =>
                                p != null && p.Active && p.IsLoggedIn &&
                                string.Equals(p.Account?.Name, accountName, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (string.IsNullOrEmpty(accountName))
                    {
                        args.Player.SendErrorMessage($"[ ERROR ] Akun/Player '{sTarget}' tidak ditemukan.");
                        return;
                    }

                    ExpApi.SetLevel(accountName, setLevel, sPlayer, true);

                    args.Player.SendSuccessMessage($"[ OK ] [Univers.Exp] Berhasil mengatur level {accountName} ke Level {setLevel} / 1000 (sinkron ElementLevel)!");
                    if (sPlayer != null && sPlayer.Active)
                    {
                        sPlayer.SendMessage($"[ INFO ] Level karakter & elemen kamu diatur oleh Admin ke Level {setLevel}!", Color.Green);
                    }
                    break;
            }
        }

        private static string GenerateProgressBar(double percent)
        {
            int totalBars = 10;
            int filled = (int)Math.Round((percent / 100.0) * totalBars);
            filled = Math.Clamp(filled, 0, totalBars);
            return "[" + new string('#', filled) + new string('-', totalBars - filled) + "]";
        }
    }
}
