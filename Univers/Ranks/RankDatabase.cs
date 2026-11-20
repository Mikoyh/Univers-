using System;
using System.IO;
using System.Collections.Generic;
using LiteDB;
using TShockAPI;

namespace Skynomi.Ranks
{
    public class PlayerRankData
    {
        [BsonId]
        public int Id { get; set; }
        public string AccountName { get; set; } = "";

        public int RankIndex { get; set; } = 0;

        public long PlaytimeSeconds { get; set; } = 0;

        public List<string> DefeatedBosses { get; set; } = new List<string>();

        public bool HasJobSelected { get; set; } = false;
        public int JobsCompleted { get; set; } = 0;

        public int ElementTagsCount { get; set; } = 0;
        public int ElementCombosCount { get; set; } = 0;

        public DateTime FirstJoinDate { get; set; } = DateTime.UtcNow;

        public DateTime LastRankUpDate { get; set; } = DateTime.UtcNow;
    }

    public class RankDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<PlayerRankData> _collection;

        public RankDatabase()
        {
            string dbDir = Path.Combine(TShock.SavePath, "Skynomi");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            string dbPath = Path.Combine(dbDir, "skynomi_ranks.db");

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _collection = _db.GetCollection<PlayerRankData>("players_ranks");
            _collection.EnsureIndex(x => x.AccountName, true);
        }

        public PlayerRankData GetOrCreate(string accountName)
        {
            var data = _collection.FindOne(x => x.AccountName == accountName);
            if (data == null)
            {
                data = new PlayerRankData
                {
                    AccountName = accountName,
                    RankIndex = 0,
                    PlaytimeSeconds = 0,
                    DefeatedBosses = new List<string>(),
                    HasJobSelected = false,
                    JobsCompleted = 0,
                    ElementTagsCount = 0,
                    ElementCombosCount = 0,
                    FirstJoinDate = DateTime.UtcNow,
                    LastRankUpDate = DateTime.UtcNow
                };
                _collection.Insert(data);
            }
            return data;
        }

        public void Update(PlayerRankData data)
        {
            _collection.Update(data);
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
