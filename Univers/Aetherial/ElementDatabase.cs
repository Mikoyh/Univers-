/*
 * =====================================================================
 *  ElementDatabase.cs - Persistent storage for player element profile
 * =====================================================================
 */

using System;
using LiteDB;

namespace Univers.Aetherial
{
    public class ElementUserProfile
    {
        [BsonId]
        public string AccountName { get; set; } = string.Empty;
        public string AssignedElement { get; set; } = string.Empty;
        // "IGNIS", "GLACIAL", "TORRENT", "VOLT", "BRAMBLE", "OMNIA"
        public int ElementLevel { get; set; } = 1; // Tingkatan level elemen (1 - 1000 sinkron Level Karakter Utama)
        public long ElementExp { get; set; } = 0;
        public DateTime AwakenedAt { get; set; } = DateTime.MinValue;
        public long TotalReactionsTriggered { get; set; } = 0;
    }

    public class ElementDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<ElementUserProfile> _profiles;

        public ElementDatabase(string dbPath)
        {
            _db = new LiteDatabase(dbPath);
            _profiles = _db.GetCollection<ElementUserProfile>("players_elements");
            _profiles.EnsureIndex(x => x.AccountName);
        }

        public ElementUserProfile GetOrCreateProfile(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return null!;

            var profile = _profiles.FindById(accountName);
            if (profile == null)
            {
                profile = new ElementUserProfile
                {
                    AccountName = accountName,
                    AssignedElement = "" // Not yet awakened
                };
                _profiles.Insert(profile);
            }
            return profile ?? new ElementUserProfile { AccountName = accountName, AssignedElement = "" };
        }

        public void SaveProfile(ElementUserProfile profile)
        {
            if (profile != null)
            {
                _profiles.Upsert(profile);
            }
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
