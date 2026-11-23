/*
 * =====================================================================
 *  MultiWorldSscStore.cs - Shared LiteDB SSC Character Persistence
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 *  Menyimpan snapshot karakter SSC lintas world (Master + Child) & crash-safe
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.IO;
using LiteDB;

namespace Univers.MultiWorld
{
    public class SscCharacterRecord
    {
        [BsonId]
        public string AccountKey { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string LastWorldId { get; set; } = string.Empty;
        public string SnapshotJson { get; set; } = string.Empty;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public class MultiWorldSscStore : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<SscCharacterRecord> _records;
        private readonly ConcurrentDictionary<string, SscCharacterRecord> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public MultiWorldSscStore(string dbPath)
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _records = _db.GetCollection<SscCharacterRecord>("ssc_characters");
            _records.EnsureIndex(x => x.AccountKey, true);
        }

        private static string NormalizeKey(string accountName)
        {
            return (accountName ?? string.Empty).Trim().ToLowerInvariant();
        }

        public void Save(string accountName, string playerName, string worldId, string snapshotJson)
        {
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(snapshotJson)) return;

            string key = NormalizeKey(accountName);
            var rec = new SscCharacterRecord
            {
                AccountKey = key,
                AccountName = accountName.Trim(),
                PlayerName = string.IsNullOrWhiteSpace(playerName) ? accountName.Trim() : playerName.Trim(),
                LastWorldId = worldId ?? string.Empty,
                SnapshotJson = snapshotJson,
                UpdatedUtc = DateTime.UtcNow
            };

            _cache[key] = rec;

            lock (_lock)
            {
                try
                {
                    _records.Upsert(rec);
                }
                catch
                {
                }
            }
        }

        public SscCharacterRecord? Get(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return null;

            string key = NormalizeKey(accountName);
            if (_cache.TryGetValue(key, out var cached) && !string.IsNullOrWhiteSpace(cached.SnapshotJson))
            {
                return cached;
            }

            lock (_lock)
            {
                try
                {
                    var found = _records.FindById(key);
                    if (found != null && !string.IsNullOrWhiteSpace(found.SnapshotJson))
                    {
                        _cache[key] = found;
                        return found;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try { _db?.Dispose(); } catch { }
            }
        }
    }
}
