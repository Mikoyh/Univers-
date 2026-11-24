/*
 * =====================================================================
 *  DiscordDatabase.cs - Database LiteDB Thread-Safe Link Akun Discord
 *  Namespace: Univers.Discord (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using LiteDB;

namespace Univers.Discord
{
    public class DiscordLinkRecord
    {
        [BsonId]
        public string AccountName { get; set; } = string.Empty;

        public string DiscordUserId { get; set; } = string.Empty;
        public string DiscordUsername { get; set; } = string.Empty;
        public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
        public bool IsVerified { get; set; } = true;
    }

    public class DiscordDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<DiscordLinkRecord> _links;
        private readonly object _dbLock = new();

        public DiscordDatabase(string dbPath)
        {
            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _links = _db.GetCollection<DiscordLinkRecord>("discord_links");
            _links.EnsureIndex(x => x.AccountName);
            _links.EnsureIndex(x => x.DiscordUserId);
        }

        public DiscordLinkRecord? GetByAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return null;

            lock (_dbLock)
            {
                return _links.FindById(accountName)
                    ?? _links.FindOne(x => x.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public DiscordLinkRecord? GetByDiscordId(string discordUserId)
        {
            if (string.IsNullOrWhiteSpace(discordUserId)) return null;

            lock (_dbLock)
            {
                return _links.FindOne(x => x.DiscordUserId == discordUserId);
            }
        }

        public void UpsertLink(DiscordLinkRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.AccountName) || string.IsNullOrWhiteSpace(record.DiscordUserId))
            {
                return;
            }

            lock (_dbLock)
            {
                // Pastikan 1 Discord ID hanya terikat ke 1 akun TShock (cegah duplikat)
                var oldByDiscord = _links.FindOne(x => x.DiscordUserId == record.DiscordUserId);
                if (oldByDiscord != null && !oldByDiscord.AccountName.Equals(record.AccountName, StringComparison.OrdinalIgnoreCase))
                {
                    _links.Delete(oldByDiscord.AccountName);
                }

                _links.Upsert(record);
            }
        }

        public bool UnlinkByAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return false;

            lock (_dbLock)
            {
                var found = _links.FindById(accountName)
                    ?? _links.FindOne(x => x.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase));
                if (found == null) return false;
                return _links.Delete(found.AccountName);
            }
        }

        public bool UnlinkByDiscordId(string discordUserId, out string removedAccount)
        {
            removedAccount = string.Empty;
            if (string.IsNullOrWhiteSpace(discordUserId)) return false;

            lock (_dbLock)
            {
                var found = _links.FindOne(x => x.DiscordUserId == discordUserId);
                if (found == null) return false;
                removedAccount = found.AccountName;
                return _links.Delete(found.AccountName);
            }
        }

        public int GetTotalVerifiedCount()
        {
            lock (_dbLock)
            {
                return _links.Count();
            }
        }

        public List<DiscordLinkRecord> GetAllLinks()
        {
            lock (_dbLock)
            {
                return _links.FindAll().ToList();
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
