/*
 * =====================================================================
 *  ShopDatabase.cs - LiteDB Persistence untuk Display Fisik, AH, Claim & Log
 *  Namespace: Univers.Shop (.NET 9.0 / TShock 6.x)
 * =====================================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace Univers.Shop
{
    public enum PhysicalDisplayType
    {
        ServerShop = 0,
        ServerSellback = 1,
        PlayerBooth = 2
    }

    public enum ListingStatus
    {
        Active = 0,
        Sold = 1,
        Cancelled = 2,
        Expired = 3
    }

    public class PhysicalDisplayRecord
    {
        [BsonId]
        public int Id { get; set; }
        public PhysicalDisplayType DisplayType { get; set; } = PhysicalDisplayType.ServerShop;
        public int SignX { get; set; }
        public int SignY { get; set; }
        public bool HasFrame { get; set; } = false;
        public int FrameX { get; set; }
        public int FrameY { get; set; }

        // Untuk ServerShop
        public int ServerItemId { get; set; } = 0;
        public byte ServerItemPrefix { get; set; } = 0;
        public int ServerQuantity { get; set; } = 1;
        public long ServerPrice { get; set; } = 100;

        // Untuk PlayerBooth (Lapak)
        public int? ActiveListingId { get; set; } = null;

        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class MarketListing
    {
        [BsonId]
        public int Id { get; set; }
        public string SellerAccount { get; set; } = string.Empty;
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Stack { get; set; } = 1;
        public byte Prefix { get; set; } = 0;
        public long TotalPrice { get; set; }
        public ListingStatus Status { get; set; } = ListingStatus.Active;
        public int? BoothDisplayId { get; set; } = null;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(48);

        public string BuyerAccount { get; set; } = string.Empty;
        public DateTime? SoldAtUtc { get; set; } = null;
    }

    public class PendingClaimItem
    {
        [BsonId]
        public int Id { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Stack { get; set; } = 1;
        public byte Prefix { get; set; } = 0;
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class OfflineSaleNotice
    {
        [BsonId]
        public int Id { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class ShopTransactionLog
    {
        [BsonId]
        public int Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string BuyerAccount { get; set; } = string.Empty;
        public string SellerAccount { get; set; } = string.Empty;
        public int ItemId { get; set; }
        public int Stack { get; set; }
        public byte Prefix { get; set; }
        public long GrossShards { get; set; }
        public long TaxShards { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class ShopDatabase : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<PhysicalDisplayRecord> _displays;
        private readonly ILiteCollection<MarketListing> _listings;
        private readonly ILiteCollection<PendingClaimItem> _claims;
        private readonly ILiteCollection<OfflineSaleNotice> _notices;
        private readonly ILiteCollection<ShopTransactionLog> _logs;

        public ShopDatabase(string dbPath)
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            _displays = _db.GetCollection<PhysicalDisplayRecord>("shop_displays");
            _listings = _db.GetCollection<MarketListing>("market_listings");
            _claims = _db.GetCollection<PendingClaimItem>("pending_claims");
            _notices = _db.GetCollection<OfflineSaleNotice>("pending_notices");
            _logs = _db.GetCollection<ShopTransactionLog>("tx_logs");

            _displays.EnsureIndex(x => x.SignX);
            _displays.EnsureIndex(x => x.SignY);
            _listings.EnsureIndex(x => x.Status);
            _listings.EnsureIndex(x => x.SellerAccount);
            _claims.EnsureIndex(x => x.AccountName);
            _notices.EnsureIndex(x => x.AccountName);
        }

        // === Physical Displays (Sign + ItemFrame) ===
        public List<PhysicalDisplayRecord> GetAllDisplays() => _displays.FindAll().ToList();

        public PhysicalDisplayRecord? GetDisplayById(int id) => _displays.FindById(id);

        public PhysicalDisplayRecord? GetDisplayBySign(int signTopX, int signTopY)
        {
            return _displays.FindOne(x => x.SignX == signTopX && x.SignY == signTopY);
        }

        public PhysicalDisplayRecord? GetDisplayByFrame(int frameTopX, int frameTopY)
        {
            return _displays.FindOne(x => x.HasFrame && x.FrameX == frameTopX && x.FrameY == frameTopY);
        }

        public void SaveDisplay(PhysicalDisplayRecord record)
        {
            if (record.Id == 0)
            {
                record.Id = _displays.Insert(record).AsInt32;
            }
            else
            {
                _displays.Upsert(record);
            }
        }

        public bool DeleteDisplay(int id) => _displays.Delete(id);

        // === Market / Auction House Listings ===
        public int InsertListing(MarketListing listing)
        {
            int id = _listings.Insert(listing).AsInt32;
            listing.Id = id;
            return id;
        }

        public void UpdateListing(MarketListing listing)
        {
            _listings.Upsert(listing);
        }

        public MarketListing? GetListingById(int id) => _listings.FindById(id);

        public List<MarketListing> GetActiveListings()
        {
            return _listings.Find(x => x.Status == ListingStatus.Active)
                .OrderByDescending(x => x.Id)
                .ToList();
        }

        public List<MarketListing> GetActiveListingsBySeller(string accountName)
        {
            return _listings.Find(x => x.Status == ListingStatus.Active &&
                                       x.SellerAccount.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Id)
                .ToList();
        }

        public List<MarketListing> GetExpiredActiveListings(DateTime nowUtc)
        {
            return _listings.Find(x => x.Status == ListingStatus.Active && x.ExpiresAtUtc <= nowUtc).ToList();
        }

        // === Pending Claims ===
        public void AddPendingClaim(PendingClaimItem item)
        {
            int id = _claims.Insert(item).AsInt32;
            item.Id = id;
        }

        public List<PendingClaimItem> GetPendingClaims(string accountName)
        {
            return _claims.Find(x => x.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Id)
                .ToList();
        }

        public int GetPendingClaimCount(string accountName)
        {
            return _claims.Count(x => x.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        }

        public bool DeletePendingClaim(int id) => _claims.Delete(id);

        // === Offline Notices ===
        public void AddOfflineNotice(string accountName, string message)
        {
            _notices.Insert(new OfflineSaleNotice
            {
                AccountName = accountName,
                Message = message,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        public List<OfflineSaleNotice> PopOfflineNotices(string accountName)
        {
            var found = _notices.Find(x => x.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Id)
                .ToList();
            foreach (var n in found)
            {
                _notices.Delete(n.Id);
            }
            return found;
        }

        // === Audit Logs ===
        public void LogTransaction(ShopTransactionLog log)
        {
            _logs.Insert(log);
        }

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
