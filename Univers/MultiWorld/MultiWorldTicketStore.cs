/*
 * =====================================================================
 *  MultiWorldTicketStore.cs - Shared LiteDB Transfer Tickets & Auth Bridge
 *  Namespace: Univers.MultiWorld (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using LiteDB;

namespace Univers.MultiWorld
{
    public class WorldTransferTicket
    {
        [BsonId]
        public int Id { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string GroupName { get; set; } = "default";
        public string ClientUuid { get; set; } = string.Empty;
        public bool HasWildcardPermission { get; set; }
        public string GroupPermissions { get; set; } = string.Empty;
        public string SscSnapshotJson { get; set; } = string.Empty;
        public string SourceWorldId { get; set; } = string.Empty;
        public string TargetWorldId { get; set; } = string.Empty;
        public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddMinutes(2);
    }

    public class MultiWorldTicketStore : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<WorldTransferTicket> _tickets;
        private readonly ConcurrentDictionary<string, WorldTransferTicket> _memoryTickets = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public MultiWorldTicketStore(string dbPath)
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _tickets = _db.GetCollection<WorldTransferTicket>("transfer_tickets");
            _tickets.EnsureIndex(x => x.PlayerName);
        }

        public void IssueTicket(
            string playerName,
            string accountName,
            string groupName,
            string uuid,
            string sourceWorldId,
            string targetWorldId,
            bool hasWildcardPermission = false,
            string groupPermissions = "",
            string sscSnapshotJson = "")
        {
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(targetWorldId)) return;

            var ticket = new WorldTransferTicket
            {
                PlayerName = playerName,
                AccountName = accountName,
                GroupName = string.IsNullOrWhiteSpace(groupName) ? "default" : groupName,
                ClientUuid = uuid ?? string.Empty,
                HasWildcardPermission = hasWildcardPermission,
                GroupPermissions = groupPermissions ?? string.Empty,
                SscSnapshotJson = sscSnapshotJson ?? string.Empty,
                SourceWorldId = sourceWorldId ?? string.Empty,
                TargetWorldId = targetWorldId,
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(3)
            };

            _memoryTickets[playerName] = ticket;

            lock (_lock)
            {
                try
                {
                    // Hapus tiket lama milik player ini agar selalu bersih
                    var old = _tickets.Find(x => x.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var item in old)
                    {
                        _tickets.Delete(item.Id);
                    }

                    _tickets.Insert(ticket);
                }
                catch
                {
                }
            }
        }

        public WorldTransferTicket? ConsumeTicket(string playerName, string expectedWorldId)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return null;

            DateTime now = DateTime.UtcNow;

            if (_memoryTickets.TryGetValue(playerName, out var memTicket) &&
                memTicket.TargetWorldId.Equals(expectedWorldId, StringComparison.OrdinalIgnoreCase) &&
                memTicket.ExpiresUtc >= now)
            {
                _memoryTickets.TryRemove(playerName, out _);
                lock (_lock)
                {
                    try
                    {
                        var old = _tickets.Find(x => x.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase)).ToList();
                        foreach (var item in old)
                        {
                            _tickets.Delete(item.Id);
                        }
                    }
                    catch
                    {
                    }
                }
                return memTicket;
            }

            lock (_lock)
            {
                try
                {
                    // Bersihkan tiket kedaluwarsa sekalian
                    var expired = _tickets.Find(x => x.ExpiresUtc < now).ToList();
                    foreach (var ex in expired)
                    {
                        _tickets.Delete(ex.Id);
                    }

                    var match = _tickets.Find(x =>
                            x.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase) &&
                            x.TargetWorldId.Equals(expectedWorldId, StringComparison.OrdinalIgnoreCase) &&
                            x.ExpiresUtc >= now)
                        .OrderByDescending(x => x.Id)
                        .FirstOrDefault();

                    if (match != null)
                    {
                        _tickets.Delete(match.Id);
                    }

                    return match;
                }
                catch
                {
                    return null;
                }
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
