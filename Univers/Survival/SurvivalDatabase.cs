/*
 * =====================================================================
 *  SurvivalDatabase.cs - LiteDB Thread-Safe Persistence (Connection=shared)
 *  Namespace: Univers.Survival (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.IO;
using LiteDB;

namespace Univers.Survival
{
    public class SurvivalProfile
    {
        [BsonId]
        public int Id { get; set; }

        public string AccountName { get; set; } = string.Empty;
        public double Hunger { get; set; } = 100.0;
        public double Sanity { get; set; } = 100.0;
        public double Temperature { get; set; } = 25.0;
        public double Wetness { get; set; } = 0.0;
        public bool ShowStatusHud { get; set; } = false;
        public int FoodsEaten { get; set; } = 0;
        public int ShadowEncounters { get; set; } = 0;
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public class SurvivalDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<SurvivalProfile> _profiles;
        private readonly object _lock = new();

        public SurvivalDatabase(string dbPath)
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _profiles = _db.GetCollection<SurvivalProfile>("survival_profiles");
            _profiles.EnsureIndex(x => x.AccountName, true);
        }

        public SurvivalProfile GetOrCreateProfile(string accountName, SurvivalConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                return new SurvivalProfile
                {
                    Hunger = cfg.StartingHunger,
                    Sanity = cfg.StartingSanity,
                    Temperature = cfg.IdealTemperature,
                    Wetness = 0.0,
                    ShowStatusHud = cfg.DefaultShowStatusHud
                };
            }

            lock (_lock)
            {
                var found = _profiles.FindOne(p => p.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    found.Hunger = Math.Clamp(found.Hunger, 0.0, cfg.MaxHunger);
                    found.Sanity = Math.Clamp(found.Sanity, 0.0, cfg.MaxSanity);
                    found.Temperature = Math.Clamp(found.Temperature, cfg.MinTemperature, cfg.MaxTemperature);
                    found.Wetness = Math.Clamp(found.Wetness, 0.0, 100.0);
                    return found;
                }

                var created = new SurvivalProfile
                {
                    AccountName = accountName,
                    Hunger = cfg.StartingHunger,
                    Sanity = cfg.StartingSanity,
                    Temperature = cfg.IdealTemperature,
                    Wetness = 0.0,
                    ShowStatusHud = cfg.DefaultShowStatusHud,
                    LastUpdatedUtc = DateTime.UtcNow
                };
                created.Id = _profiles.Insert(created).AsInt32;
                return created;
            }
        }

        public void SaveProfile(SurvivalProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.AccountName)) return;

            lock (_lock)
            {
                profile.LastUpdatedUtc = DateTime.UtcNow;
                if (profile.Id == 0)
                {
                    var existing = _profiles.FindOne(p => p.AccountName.Equals(profile.AccountName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        profile.Id = existing.Id;
                    }
                }
                _profiles.Upsert(profile);
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
