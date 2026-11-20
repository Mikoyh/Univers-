using System;
using System.IO;
using System.Collections.Generic;
using LiteDB;
using TShockAPI;

namespace Univers.Exp
{
    public class ExpDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<PlayerExpData> _collection;

        public ExpDatabase()
        {
            string dbDir = Path.Combine(TShock.SavePath, "Univers");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            string dbPath = Path.Combine(dbDir, "univers_exp.db");

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _collection = _db.GetCollection<PlayerExpData>("players_exp");
            _collection.EnsureIndex(x => x.AccountName, true);
        }

        public PlayerExpData GetOrCreate(string accountName)
        {
            var found = _collection.FindOne(x => x.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;

            var created = new PlayerExpData
            {
                AccountName = accountName,
                Level = 1,
                CurrentExp = 0,
                TotalExp = 0,
                LastChatExp = DateTime.MinValue,
                CreatedAt = DateTime.UtcNow
            };
            _collection.Insert(created);
            return created;
        }

        public void Save(PlayerExpData data)
        {
            _collection.Upsert(data);
        }

        public IEnumerable<PlayerExpData> GetTopPlayers(int count = 10)
        {
            return _collection.Query()
                .OrderByDescending(x => x.Level)
                .Limit(count)
                .ToEnumerable();
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }

    public class PlayerExpData
    {
        [BsonId]
        public string AccountName { get; set; } = string.Empty;
        public int Level { get; set; } = 1;
        public long CurrentExp { get; set; } = 0;
        public long TotalExp { get; set; } = 0;
        public int MessagesSent { get; set; } = 0;
        public int MobsKilled { get; set; } = 0;
        public int BossesKilled { get; set; } = 0;
        public DateTime LastChatExp { get; set; } = DateTime.MinValue;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
