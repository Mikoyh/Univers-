/*
 * ==============================================================================
 * EconomyCommands.cs - Perintah Chat Pemain & Admin untuk Sistem Ekonomi
 * ==============================================================================
 */

using System;
using System.Linq;
using TShockAPI;

namespace Skynomi.Economy
{
    public static class EconomyCommands
    {
        public static void Initialize()
        {
            Commands.ChatCommands.Add(new Command("skynomi.eco.user", HandleBalance, "balance", "bal", "money", "uang")
            {
                HelpText = "Cek saldo dompet, tabungan bank, dan limit dompet kasta (/bal)"
            });

            Commands.ChatCommands.Add(new Command("skynomi.eco.user", HandlePay, "pay", "kirim", "transfer")
            {
                HelpText = "Kirim saldo Credits ke pemain lain (/pay <nama> <jumlah>)"
            });

            Commands.ChatCommands.Add(new Command("skynomi.eco.user", HandleBank, "bank")
            {
                HelpText = "Cek informasi tabungan bank dan menu transaksi (/bank)"
            });

            Commands.ChatCommands.Add(new Command("skynomi.eco.user", HandleDeposit, "deposit", "depo", "setor")
            {
                HelpText = "Setor saldo dari dompet ke tabungan bank (/deposit <jumlah|all>)"
            });

            Commands.ChatCommands.Add(new Command("skynomi.eco.user", HandleWithdraw, "withdraw", "tarik")
            {
                HelpText = "Tarik saldo dari tabungan bank ke dompet (/withdraw <jumlah|all>)"
            });

            Commands.ChatCommands.Add(new Command("skynomi.eco.user", HandleBalTop, "baltop", "ecotop", "topmoney")
            {
                HelpText = "Lihat daftar pemain terkaya di server (/baltop)"
            });

            Commands.ChatCommands.Add(new Command("skynomi.eco.admin", HandleAdminEco, "eco", "economy")
            {
                HelpText = "Perintah staff kelola ekonomi (/eco give|take|set <player> <amount>)"
            });
        }

        private static void HandleBalance(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu harus login terlebih dahulu!");
                return;
            }

            var acc = EconomyModule.Database.GetOrCreateAccount(player.Account.Name);

            player.SendInfoMessage("[i:73] [c/38bdf8:=== SKYNOMI FINANCIAL STATUS ===]");
            player.SendSuccessMessage($"[c/fbbf24:Dompet Aktif :] {EconomyApi.Format(acc.Balance, true)}");
            player.SendSuccessMessage($"[c/34d399:Tabungan Bank:] {EconomyApi.Format(acc.BankBalance, true)}");
            player.SendInfoMessage($"[c/94a3b8:Limit Dompet :] {EconomyApi.Format(acc.MaxWalletLimit, true)} (Naikkan kasta untuk memperluas)");
            player.SendInfoMessage("[c/cbd5e1:Gunakan /deposit untuk menabung atau /pay untuk transfer.]");
        }

        private static void HandlePay(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu harus login terlebih dahulu!");
                return;
            }

            if (args.Parameters.Count < 2)
            {
                player.SendErrorMessage("[ FORMAT ] Gunakan: /pay <nama_player> <jumlah>");
                return;
            }

            string targetName = args.Parameters[0];
            if (!long.TryParse(args.Parameters[1], out long amount) || amount <= 0)
            {
                player.SendErrorMessage("[ ERROR ] Jumlah transfer harus berupa bilangan bulat positif!");
                return;
            }

            if (amount < EconomyModule.Config.MinimumPayAmount)
            {
                player.SendErrorMessage($"[ ERROR ] Minimal transfer adalah {EconomyApi.Format(EconomyModule.Config.MinimumPayAmount, true)}!");
                return;
            }

            var targets = TSPlayer.FindByNameOrID(targetName);
            if (targets.Count == 0)
            {
                player.SendErrorMessage($"[ ERROR ] Pemain '{targetName}' tidak ditemukan!");
                return;
            }
            if (targets.Count > 1)
            {
                player.SendErrorMessage("[ ERROR ] Ditemukan lebih dari satu pemain dengan nama serupa!");
                return;
            }

            var targetPlayer = targets[0];
            if (targetPlayer.Index == player.Index)
            {
                player.SendErrorMessage("[ ERROR ] Kamu tidak bisa mentransfer Shard ke dirimu sendiri!");
                return;
            }

            var senderAcc = EconomyModule.Database.GetOrCreateAccount(player.Account.Name);
            if (senderAcc.Balance < amount)
            {
                player.SendErrorMessage($"[ ERROR ] Saldo dompetmu tidak mencukupi! Saldo: {EconomyApi.Format(senderAcc.Balance, true)}");
                return;
            }

            var receiverAcc = EconomyModule.Database.GetOrCreateAccount(targetPlayer.Account.Name);

            // Validasi limit dompet penerima
            if (EconomyModule.Config.EnableWalletCap && receiverAcc.MaxWalletLimit > 0)
            {
                long remainingSpace = receiverAcc.MaxWalletLimit - receiverAcc.Balance;
                if (remainingSpace <= 0 && !EconomyModule.Config.RouteExcessToBank)
                {
                    player.SendErrorMessage($"[ ERROR ] Dompet kasta {targetPlayer.Name} sudah penuh! Tidak dapat menerima transfer.");
                    return;
                }
            }

            // Eksekusi transfer
            senderAcc.Balance -= amount;
            senderAcc.TotalSpent += amount;
            EconomyModule.Database.SaveAccount(senderAcc);

            EconomyApi.TryCredit(receiverAcc.AccountName, amount, $"Pay from {player.Name}");

            player.SendSuccessMessage($"[ OK ] Berhasil mengirim {EconomyApi.Format(amount, true)} kepada {targetPlayer.Name}!");
            targetPlayer.SendSuccessMessage($"[ + ] Kamu menerima transfer {EconomyApi.Format(amount, true)} dari {player.Name}!");
        }

        private static void HandleBank(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu harus login terlebih dahulu!");
                return;
            }

            var acc = EconomyModule.Database.GetOrCreateAccount(player.Account.Name);

            player.SendInfoMessage("[i:73] [c/34d399:=== SKYNOMI BANK CENTRAL ===]");
            player.SendInfoMessage($"[c/34d399:Total Tabungan :] {EconomyApi.Format(acc.BankBalance, true)}");
            player.SendInfoMessage($"[c/fbbf24:Saldo Dompet   :] {EconomyApi.Format(acc.Balance, true)} / {EconomyApi.Format(acc.MaxWalletLimit, true)}");
            player.SendInfoMessage("[c/cbd5e1:Perintah: /deposit <jumlah|all> untuk setor, /withdraw <jumlah|all> untuk tarik]");
        }

        private static void HandleDeposit(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu harus login terlebih dahulu!");
                return;
            }

            var acc = EconomyModule.Database.GetOrCreateAccount(player.Account.Name);

            long amount;
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLowerInvariant() == "all")
            {
                amount = acc.Balance;
            }
            else if (args.Parameters.Count == 0 || !long.TryParse(args.Parameters[0], out amount) || amount <= 0)
            {
                player.SendErrorMessage("[ FORMAT ] Gunakan: /deposit <jumlah|all>");
                return;
            }

            if (amount > acc.Balance)
            {
                player.SendErrorMessage($"[ ERROR ] Saldo dompet tidak cukup! Tersedia: {EconomyApi.Format(acc.Balance, true)}");
                return;
            }

            if (EconomyApi.DepositToBank(player.Account.Name, amount))
            {
                player.SendSuccessMessage($"[ OK ] Berhasil menyetor {EconomyApi.Format(amount, true)} ke Tabungan Bank!");
                player.SendInfoMessage($"[ BANK ] Sisa saldo dompet: {EconomyApi.Format(acc.Balance - amount, true)} | Tabungan: {EconomyApi.Format(acc.BankBalance + amount, true)}");
            }
        }

        private static void HandleWithdraw(CommandArgs args)
        {
            var player = args.Player;
            if (player == null || !player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("[ ERROR ] Kamu harus login terlebih dahulu!");
                return;
            }

            var acc = EconomyModule.Database.GetOrCreateAccount(player.Account.Name);

            long amount;
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLowerInvariant() == "all")
            {
                long space = Math.Max(0, acc.MaxWalletLimit - acc.Balance);
                amount = Math.Min(acc.BankBalance, space);
            }
            else if (args.Parameters.Count == 0 || !long.TryParse(args.Parameters[0], out amount) || amount <= 0)
            {
                player.SendErrorMessage("[ FORMAT ] Gunakan: /withdraw <jumlah|all>");
                return;
            }

            if (amount > acc.BankBalance)
            {
                player.SendErrorMessage($"[ ERROR ] Tabungan bank tidak cukup! Tersedia: {EconomyApi.Format(acc.BankBalance, true)}");
                return;
            }

            if (EconomyModule.Config.EnableWalletCap && acc.MaxWalletLimit > 0 && (acc.Balance + amount) > acc.MaxWalletLimit)
            {
                player.SendErrorMessage($"[ LIMIT ] Penarikan gagal! Kapasitas dompetmu ({EconomyApi.Format(acc.MaxWalletLimit, true)}) tidak mencukupi penarikan sejumlah {EconomyApi.Format(amount, true)}.");
                player.SendInfoMessage("[ INFO ] Tingkatkan kasta (/rankup) untuk menampung lebih banyak Shard di dompet aktif!");
                return;
            }

            if (EconomyApi.WithdrawFromBank(player.Account.Name, amount))
            {
                player.SendSuccessMessage($"[ OK ] Berhasil menarik {EconomyApi.Format(amount, true)} dari Tabungan Bank!");
                player.SendInfoMessage($"[ BANK ] Saldo dompet sekarang: {EconomyApi.Format(acc.Balance + amount, true)} | Sisa Tabungan: {EconomyApi.Format(acc.BankBalance - amount, true)}");
            }
        }

        private static void HandleBalTop(CommandArgs args)
        {
            var top = EconomyModule.Database.GetTopWealthy(10).ToList();
            args.Player.SendInfoMessage("[i:73] [c/38bdf8:=== TOP 10 KONGROMERAT SEMESTA UNIVERS ===]");

            int rank = 1;
            foreach (var acc in top)
            {
                long netWorth = acc.Balance + acc.BankBalance;
                args.Player.SendInfoMessage($"[c/fbbf24:{rank}.] [c/ffffff:{acc.AccountName}] - {EconomyApi.Format(netWorth, true)} (Dompet: {EconomyApi.Format(acc.Balance, true)})");
                rank++;
            }
        }

        private static void HandleAdminEco(CommandArgs args)
        {
            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage("[ FORMAT ] /eco <give|take|set> <player> <amount>");
                return;
            }

            string sub = args.Parameters[0].ToLowerInvariant();
            string targetName = args.Parameters[1];
            if (!long.TryParse(args.Parameters[2], out long amount) || amount < 0)
            {
                args.Player.SendErrorMessage("[ ERROR ] Jumlah harus berupa angka valid non-negatif!");
                return;
            }

            switch (sub)
            {
                case "give":
                    EconomyApi.TryCredit(targetName, amount, "Admin Give");
                    args.Player.SendSuccessMessage($"[ OK ] Memberikan {EconomyApi.Format(amount, true)} kepada akun {targetName}.");
                    break;
                case "take":
                    EconomyApi.TryDebit(targetName, amount, "Admin Take");
                    args.Player.SendSuccessMessage($"[ OK ] Menarik {EconomyApi.Format(amount, true)} dari akun {targetName}.");
                    break;
                case "set":
                    EconomyApi.SetBalance(targetName, amount);
                    args.Player.SendSuccessMessage($"[ OK ] Mengatur saldo dompet akun {targetName} menjadi {EconomyApi.Format(amount, true)}.");
                    break;
                default:
                    args.Player.SendErrorMessage("[ ERROR ] Sub-command tidak valid! Gunakan: give, take, atau set.");
                    break;
            }
        }
    }
}
