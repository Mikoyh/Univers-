/*
 * =====================================================================
 *  HousingDatabase.cs - LiteDB Persistence (Connection=shared) untuk Kavling RP
 *  Namespace: Univers.Housing (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace Univers.Housing
{
    public class HousingPlot
    {
        [BsonId]
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
        public string WorldId { get; set; } = "lobby";
        public string DistrictName { get; set; } = "kota";

        public string CustomName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public int MinX { get; set; }
        public int MinY { get; set; }
        public int MaxX { get; set; }
        public int MaxY { get; set; }

        public int SpawnX { get; set; }
        public int SpawnY { get; set; }

        public bool HasSign { get; set; } = false;
        public int SignX { get; set; }
        public int SignY { get; set; }

        public long PriceShards { get; set; } = 1000;
        public long ForSaleByOwnerPrice { get; set; } = 0;
        public int Level { get; set; } = 1;

        public string OwnerAccount { get; set; } = string.Empty;
        public string OwnerDisplayName { get; set; } = string.Empty;

        public List<string> CoOwners { get; set; } = new();
        public List<string> Members { get; set; } = new();
        public List<string> BannedPlayers { get; set; } = new();
        public List<string> LikedBy { get; set; } = new();

        public int LikesCount { get; set; } = 0;
        public long VisitsCount { get; set; } = 0;

        // RP Flags
        public bool IsPublicVisit { get; set; } = true;
        public bool FlagAllowGuestInteract { get; set; } = true;
        public bool FlagAllowGuestBuild { get; set; } = false;
        public bool FlagAllowGuestChests { get; set; } = false;
        public bool FlagAllowPvp { get; set; } = false;
        public bool FlagCozyRegen { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime PurchasedAtUtc { get; set; } = DateTime.MinValue;

        public bool IsOwned => !string.IsNullOrWhiteSpace(OwnerAccount);

        public string EffectiveTitle => !string.IsNullOrWhiteSpace(CustomName)
            ? CustomName
            : (IsOwned ? $"Rumah {OwnerDisplayName}" : $"Kavling {Code}");

        public bool ContainsTile(int tx, int ty)
        {
            return tx >= MinX && tx <= MaxX && ty >= MinY && ty <= MaxY;
        }

        public bool IsOwner(string accountName)
        {
            return IsOwned && OwnerAccount.Equals(accountName, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCoOwner(string accountName)
        {
            return CoOwners != null && CoOwners.Any(x => x.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsMember(string accountName)
        {
            return Members != null && Members.Any(x => x.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsBanned(string accountName)
        {
            return BannedPlayers != null && BannedPlayers.Any(x => x.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        }

        public bool CanBuild(string accountName)
        {
            if (FlagAllowGuestBuild && IsOwned && !IsBanned(accountName)) return true;
            return IsOwner(accountName) || IsCoOwner(accountName) || IsMember(accountName);
        }

        public bool CanOpenChests(string accountName)
        {
            if (FlagAllowGuestChests && IsOwned && !IsBanned(accountName)) return true;
            return IsOwner(accountName) || IsCoOwner(accountName);
        }

        public bool CanInteract(string accountName)
        {
            if (CanBuild(accountName)) return true;
            return FlagAllowGuestInteract && IsOwned && !IsBanned(accountName);
        }

        public bool CanEnter(string accountName)
        {
            if (IsOwner(accountName) || IsCoOwner(accountName) || IsMember(accountName)) return true;
            if (IsBanned(accountName)) return false;
            return !IsOwned || IsPublicVisit;
        }
    }

    public class HousingDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<HousingPlot> _plots;
        private readonly object _lock = new();

        public HousingDatabase(string dbPath)
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _plots = _db.GetCollection<HousingPlot>("housing_plots");
            _plots.EnsureIndex(x => x.Code, true);
            _plots.EnsureIndex(x => x.WorldId);
            _plots.EnsureIndex(x => x.OwnerAccount);
        }

        public List<HousingPlot> GetAllPlots(string? worldId = null)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(worldId))
                {
                    return _plots.FindAll().OrderBy(p => p.Id).ToList();
                }

                return _plots.Find(p => p.WorldId.Equals(worldId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.Id)
                    .ToList();
            }
        }

        public HousingPlot? GetPlotByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            lock (_lock)
            {
                return _plots.FindOne(p => p.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
            }
        }

        public HousingPlot? GetPlotBySign(string worldId, int signTopX, int signTopY)
        {
            lock (_lock)
            {
                return _plots.FindOne(p =>
                    p.WorldId.Equals(worldId, StringComparison.OrdinalIgnoreCase) &&
                    p.HasSign &&
                    p.SignX == signTopX &&
                    p.SignY == signTopY);
            }
        }

        public HousingPlot? GetPlotAt(string worldId, int tx, int ty)
        {
            lock (_lock)
            {
                return _plots.Find(p => p.WorldId.Equals(worldId, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(p => p.ContainsTile(tx, ty));
            }
        }

        public List<HousingPlot> GetOwnedPlots(string accountName, string? worldId = null)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return new List<HousingPlot>();
            lock (_lock)
            {
                var q = _plots.Find(p => p.OwnerAccount.Equals(accountName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(worldId))
                {
                    q = q.Where(p => p.WorldId.Equals(worldId, StringComparison.OrdinalIgnoreCase));
                }
                return q.OrderBy(p => p.Id).ToList();
            }
        }

        public HousingPlot? FindVisitTarget(string query, string worldId)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            string q = query.Trim();
            lock (_lock)
            {
                var worldPlots = _plots.Find(p => p.WorldId.Equals(worldId, StringComparison.OrdinalIgnoreCase)).ToList();

                return worldPlots.FirstOrDefault(p => p.Code.Equals(q, StringComparison.OrdinalIgnoreCase))
                    ?? worldPlots.FirstOrDefault(p => p.IsOwned && (p.OwnerAccount.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                                                                     p.OwnerDisplayName.Equals(q, StringComparison.OrdinalIgnoreCase)))
                    ?? worldPlots.FirstOrDefault(p => p.CustomName.Equals(q, StringComparison.OrdinalIgnoreCase))
                    ?? worldPlots.FirstOrDefault(p => p.CustomName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                                      (p.IsOwned && p.OwnerDisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public HousingPlot? GetFirstUnownedPlot(string worldId)
        {
            lock (_lock)
            {
                return _plots.Find(p =>
                        p.WorldId.Equals(worldId, StringComparison.OrdinalIgnoreCase) &&
                        (p.OwnerAccount == null || p.OwnerAccount == ""))
                    .OrderBy(p => p.Id)
                    .FirstOrDefault();
            }
        }

        public List<HousingPlot> GetTopLikedPlots(string worldId, int limit = 10)
        {
            lock (_lock)
            {
                return _plots.Find(p =>
                        p.WorldId.Equals(worldId, StringComparison.OrdinalIgnoreCase) &&
                        p.OwnerAccount != null && p.OwnerAccount != "")
                    .OrderByDescending(p => p.LikesCount)
                    .ThenByDescending(p => p.VisitsCount)
                    .Take(limit)
                    .ToList();
            }
        }

        public void SavePlot(HousingPlot plot)
        {
            lock (_lock)
            {
                if (plot.Id == 0)
                {
                    plot.Id = _plots.Insert(plot).AsInt32;
                }
                else
                {
                    _plots.Upsert(plot);
                }
            }
        }

        public bool DeletePlot(int id)
        {
            lock (_lock)
            {
                return _plots.Delete(id);
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
