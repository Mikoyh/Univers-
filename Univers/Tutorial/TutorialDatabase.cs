/*
 * =====================================================================
 *  TutorialDatabase.cs - Database LiteDB Thread-Safe Status Tutorial Player
 *  Namespace: Univers.Tutorial (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using LiteDB;

namespace Univers.Tutorial
{
    public class TutorialPlayerProfile
    {
        [BsonId]
        public string AccountName { get; set; } = string.Empty;

        public bool HasCompletedTutorial { get; set; } = false;
        public DateTime FirstJoinedTutorialUtc { get; set; } = DateTime.MinValue;
        public DateTime CompletedTutorialUtc { get; set; } = DateTime.MinValue;
        public string AwakenedElementSnapshot { get; set; } = string.Empty;
        public int TutorialVisits { get; set; } = 0;
    }

    public class TutorialDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<TutorialPlayerProfile> _profiles;
        private readonly object _dbLock = new();

        public TutorialDatabase(string dbPath)
        {
            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _profiles = _db.GetCollection<TutorialPlayerProfile>("tutorial_players");
            _profiles.EnsureIndex(x => x.AccountName);
        }

        public TutorialPlayerProfile GetOrCreateProfile(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                return new TutorialPlayerProfile();
            }

            lock (_dbLock)
            {
                var existing = _profiles.FindById(accountName);
                if (existing != null)
                {
                    return existing;
                }

                var created = new TutorialPlayerProfile
                {
                    AccountName = accountName,
                    HasCompletedTutorial = false,
                    FirstJoinedTutorialUtc = DateTime.UtcNow
                };
                _profiles.Insert(created);
                return created;
            }
        }

        public void SaveProfile(TutorialPlayerProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.AccountName)) return;

            lock (_dbLock)
            {
                _profiles.Upsert(profile);
            }
        }

        public void Dispose()
        {
            lock (_dbLock)
            {
                _db?.Dispose();
            }
        }
    }
}
