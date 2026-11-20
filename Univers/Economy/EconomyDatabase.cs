/*
 * ==============================================================================
 * EconomyDatabase.cs - Database Thread-Safe LiteDB untuk Akun & Transaksi
 * ==============================================================================
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LiteDB;
using TShockAPI;

namespace Skynomi.Economy
{
    public class EconomyAccount
    {
        [BsonId]
        public string AccountName { get; set; } = string.Empty;

        public long Balance { get; set; } = 500;
        public long BankBalance { get; set; } = 0;
        public long MaxWalletLimit { get; set; } = 2500;
        public long TotalEarned { get; set; } = 500;
        public long TotalSpent { get; set; } = 0;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastTransactionUtc { get; set; } = DateTime.UtcNow;
    }

    public class EconomyDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<EconomyAccount> _accounts;
        private readonly object _lock = new object();

        public EconomyDatabase()
        {
            string dbDir = Path.Combine(TShock.SavePath, "Skynomi");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            string dbPath = Path.Combine(dbDir, "skynomi_economy.db");

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _accounts = _db.GetCollection<EconomyAccount>("economy_accounts");
            _accounts.EnsureIndex(x => x.AccountName, true);
        }

        public EconomyAccount GetOrCreateAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return new EconomyAccount();

            lock (_lock)
            {
                var acc = _accounts.FindById(accountName);
                if (acc == null)
                {
                    acc = new EconomyAccount
                    {
                        AccountName = accountName,
                        Balance = EconomyModule.Config.DefaultStartingBalance,
                        BankBalance = EconomyModule.Config.DefaultStartingBankBalance,
                        MaxWalletLimit = 2500, // Default mortal limit
                        TotalEarned = EconomyModule.Config.DefaultStartingBalance,
                        CreatedAtUtc = DateTime.UtcNow,
                        LastTransactionUtc = DateTime.UtcNow
                    };
                    _accounts.Insert(acc);
                }
                return acc;
            }
        }

        public void SaveAccount(EconomyAccount account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.AccountName)) return;

            lock (_lock)
            {
                account.LastTransactionUtc = DateTime.UtcNow;
                _accounts.Upsert(account);
            }
        }

        public IEnumerable<EconomyAccount> GetTopWealthy(int limit = 10)
        {
            lock (_lock)
            {
                return _accounts.Query()
                    .OrderByDescending(x => x.Balance + x.BankBalance)
                    .Limit(limit)
                    .ToEnumerable();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _db?.Dispose();
            }
        }
    }
}
