/*
 * ==============================================================================
 * EconomyApi.cs - Antarmuka API Statis untuk Integrasi Modul Lain
 * ==============================================================================
 */

using System;
using TShockAPI;

namespace Skynomi.Economy
{
    public static class EconomyApi
    {
        public static long GetBalance(string accountName)
        {
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);
            return acc.Balance;
        }

        public static long GetBankBalance(string accountName)
        {
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);
            return acc.BankBalance;
        }

        public static bool TryDebit(string accountName, long amount, string reason = "")
        {
            if (amount <= 0) return true;
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);
            if (acc.Balance < amount) return false;

            acc.Balance -= amount;
            acc.TotalSpent += amount;
            EconomyModule.Database.SaveAccount(acc);
            return true;
        }

        public static bool TryCredit(string accountName, long amount, string reason = "")
        {
            if (amount <= 0) return true;
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);

            long potentialNewBalance = acc.Balance + amount;
            long walletCap = acc.MaxWalletLimit;

            if (EconomyModule.Config.EnableWalletCap && walletCap > 0 && potentialNewBalance > walletCap)
            {
                if (EconomyModule.Config.RouteExcessToBank)
                {
                    long walletAllowed = Math.Max(0, walletCap - acc.Balance);
                    long excessToBank = amount - walletAllowed;

                    acc.Balance += walletAllowed;
                    acc.BankBalance += excessToBank;
                    acc.TotalEarned += amount;
                    EconomyModule.Database.SaveAccount(acc);

                    var tsPlayer = TShock.Players.FirstOrDefault(p => p != null && p.Account?.Name == accountName);
                    if (tsPlayer != null && excessToBank > 0)
                    {
                        tsPlayer.SendWarningMessage($"[ LIMIT ] Dompetmu telah mencapai kapasitas maksimal ({Format(walletCap)})!");
                        tsPlayer.SendInfoMessage($"[ BANK ] Kelebihan saldo +{Format(excessToBank)} dialihkan otomatis ke Tabungan Bank (/bank)!");
                    }
                    return true;
                }
                else
                {
                    // Clamp
                    acc.Balance = walletCap;
                    acc.TotalEarned += amount;
                    EconomyModule.Database.SaveAccount(acc);
                    return true;
                }
            }

            acc.Balance += amount;
            acc.TotalEarned += amount;
            EconomyModule.Database.SaveAccount(acc);
            return true;
        }

        public static void SetBalance(string accountName, long newBalance)
        {
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);
            acc.Balance = Math.Max(0, newBalance);
            EconomyModule.Database.SaveAccount(acc);
        }

        public static void SetWalletLimit(string accountName, long maxLimit)
        {
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);
            acc.MaxWalletLimit = maxLimit;
            EconomyModule.Database.SaveAccount(acc);
        }

        public static bool DepositToBank(string accountName, long amount)
        {
            if (amount <= 0) return false;
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);
            if (acc.Balance < amount) return false;

            acc.Balance -= amount;
            acc.BankBalance += amount;
            EconomyModule.Database.SaveAccount(acc);
            return true;
        }

        public static bool WithdrawFromBank(string accountName, long amount)
        {
            if (amount <= 0) return false;
            var acc = EconomyModule.Database.GetOrCreateAccount(accountName);
            if (acc.BankBalance < amount) return false;

            long walletCap = acc.MaxWalletLimit;
            if (EconomyModule.Config.EnableWalletCap && walletCap > 0 && (acc.Balance + amount) > walletCap)
            {
                return false; // Dompet tidak cukup menampung penarikan
            }

            acc.BankBalance -= amount;
            acc.Balance += amount;
            EconomyModule.Database.SaveAccount(acc);
            return true;
        }

        public static void EnforceWalletCap(TSPlayer player)
        {
            if (player == null || !player.Active || !player.IsLoggedIn) return;

            var acc = EconomyModule.Database.GetOrCreateAccount(player.Account.Name);
            long walletCap = acc.MaxWalletLimit;

            if (EconomyModule.Config.EnableWalletCap && walletCap > 0 && acc.Balance > walletCap)
            {
                long excess = acc.Balance - walletCap;
                acc.Balance = walletCap;

                if (EconomyModule.Config.RouteExcessToBank)
                {
                    acc.BankBalance += excess;
                    EconomyModule.Database.SaveAccount(acc);
                    player.SendWarningMessage($"[ LIMIT ] Saldo dompetmu melampaui batas kasta ({Format(walletCap)})!");
                    player.SendInfoMessage($"[ BANK ] Kelebihan saldo +{Format(excess)} tersimpan aman di Tabungan Bank (/bank)!");
                }
                else
                {
                    EconomyModule.Database.SaveAccount(acc);
                }
            }
        }

        public const string CosmicPurpleHex = "c084fc";

        public static string Format(long amount, bool withChatColor = false)
        {
            string symbol = EconomyModule.Config?.CurrencySymbol ?? "SHARD";
            string text;
            if (amount >= 1000000)
            {
                double m = amount / 1000000.0;
                text = $"{m:0.#}M {symbol}";
            }
            else if (amount >= 1000)
            {
                double k = amount / 1000.0;
                text = $"{k:0.#}k {symbol}";
            }
            else
            {
                text = $"{amount:N0} {symbol}";
            }

            if (withChatColor)
            {
                string hex = EconomyModule.Config?.CurrencyColorHex ?? CosmicPurpleHex;
                return $"[c/{hex}:{text}]";
            }
            return text;
        }

        public static string FormatColored(long amount) => Format(amount, true);
    }
}
