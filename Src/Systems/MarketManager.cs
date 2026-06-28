using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using MySql.Data.MySqlClient;
using SDG.Unturned;
using UrpUnturnov.Data;
using Steamworks;
using URPUnturnov;

namespace UrpUnturnov.Systems
{
    public static class MarketManager
    {
        private const string TableName = "urp_flea_listings";

        private static string GetItemCategoryFromAsset(ushort itemId)
        {
            var asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            if (asset == null) return "other";

            switch (asset.type)
            {
                case EItemType.GUN:
                case EItemType.MELEE:
                    return "weapons";
                case EItemType.MAGAZINE:
                    return "ammo";
                case EItemType.MEDICAL:
                    return "medical";
                case EItemType.FOOD:
                    return "food";
                case EItemType.SHIRT:
                case EItemType.PANTS:
                case EItemType.HAT:
                case EItemType.BACKPACK:
                case EItemType.VEST:
                case EItemType.MASK:
                case EItemType.GLASSES:
                    return "clothing";
                default:
                    return "other";
            }
        }


        public static int AddListing(MarketListing listing, int expiryDays = 1)
        {
            if (string.IsNullOrEmpty(listing.Category))
                listing.Category = GetItemCategoryFromAsset(listing.ItemId);

            listing.ListedAt = DateTime.UtcNow;
            expiryDays = Math.Max(1, Math.Min(365, expiryDays));
            var expiresAt = listing.ListedAt.AddDays(expiryDays);

            const string sql = @"INSERT INTO " + TableName + @" (item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, status, expires_at)
                VALUES (@item_id, @item_name, @category, @price, @seller_name, @seller_id, @quality, @state, @quantity, @listed_at, 'active', @expires_at)";

            var parameters = new[]
            {
                new MySqlParameter("@item_id", listing.ItemId),
                new MySqlParameter("@item_name", listing.ItemName ?? ""),
                new MySqlParameter("@category", listing.Category ?? "other"),
                new MySqlParameter("@price", listing.Price),
                new MySqlParameter("@seller_name", listing.SellerName ?? ""),
                new MySqlParameter("@seller_id", listing.SellerId),
                new MySqlParameter("@quality", listing.Quality),
                new MySqlParameter("@state", listing.State ?? (object)DBNull.Value) { MySqlDbType = MySqlDbType.Blob },
                new MySqlParameter("@quantity", listing.Quantity),
                new MySqlParameter("@listed_at", listing.ListedAt),
                new MySqlParameter("@expires_at", expiresAt)
            };

            var id = DatabaseManager.Instance.ExecuteInsertAndReturnId(sql, parameters);
            if (id > 0)
            {
                listing.Id = id;
                return listing.Id;
            }
            return -1;
        }

        public static List<MarketListing> GetListings(string category = "all", int page = 1, int itemsPerPage = 10, string orderBy = "id ASC")
        {
            return GetListings(category, page, itemsPerPage, "", orderBy);
        }

        public static List<MarketListing> GetListings(string category, int page, int itemsPerPage, string searchTerm, string orderBy = "id ASC")
        {
            var offset = (page - 1) * itemsPerPage;
            string sql;
            MySqlParameter[] parameters;

            if (string.IsNullOrEmpty(orderBy))
                orderBy = "id ASC";

            if (!string.IsNullOrEmpty(searchTerm))
            {
                sql = "SELECT id, item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, expires_at, status FROM " + TableName +
                    " WHERE status = 'active' AND quantity > 0 AND item_name LIKE @search ORDER BY " + orderBy + " LIMIT @limit OFFSET @offset";
                parameters = new[]
                {
                    new MySqlParameter("@search", "%" + searchTerm + "%"),
                    new MySqlParameter("@limit", itemsPerPage),
                    new MySqlParameter("@offset", offset)
                };
            }
            else if (category != "all")
            {
                sql = "SELECT id, item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, expires_at, status FROM " + TableName +
                    " WHERE status = 'active' AND quantity > 0 AND category = @category ORDER BY " + orderBy + " LIMIT @limit OFFSET @offset";
                parameters = new[]
                {
                    new MySqlParameter("@category", category),
                    new MySqlParameter("@limit", itemsPerPage),
                    new MySqlParameter("@offset", offset)
                };
            }
            else
            {
                sql = "SELECT id, item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, expires_at, status FROM " + TableName +
                    " WHERE status = 'active' AND quantity > 0 ORDER BY " + orderBy + " LIMIT @limit OFFSET @offset";
                parameters = new[]
                {
                    new MySqlParameter("@limit", itemsPerPage),
                    new MySqlParameter("@offset", offset)
                };
            }

            var dt = DatabaseManager.Instance.ExecuteQuery(sql, parameters);
            return DataTableToListings(dt);
        }

        public static List<MarketListing> GetPlayerListings(CSteamID steamID, int page = 1, int itemsPerPage = 10, string orderBy = "id ASC")
        {
            var offset = (page - 1) * itemsPerPage;
            if (string.IsNullOrEmpty(orderBy))
                orderBy = "id ASC";

            var sql = "SELECT id, item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, expires_at, status FROM " + TableName +
                " WHERE seller_id = @seller_id AND status = 'active' ORDER BY " + orderBy + " LIMIT @limit OFFSET @offset";
            var parameters = new[]
            {
                new MySqlParameter("@seller_id", steamID.m_SteamID),
                new MySqlParameter("@limit", itemsPerPage),
                new MySqlParameter("@offset", offset)
            };
            var dt = DatabaseManager.Instance.ExecuteQuery(sql, parameters);
            return DataTableToListings(dt);
        }

        public static int GetPlayerListingsTotalPages(CSteamID steamID, int itemsPerPage = 10)
        {
            var sql = "SELECT COUNT(*) FROM " + TableName + " WHERE seller_id = @seller_id AND status = 'active'";
            var result = DatabaseManager.Instance.ExecuteScalar(sql, new MySqlParameter("@seller_id", steamID.m_SteamID));
            var count = result != null ? Convert.ToInt32(result) : 0;
            return (int)Math.Ceiling((double)count / itemsPerPage);
        }

        public static string GetListingDuration(int listingId)
        {
            var listing = GetListing(listingId);
            return GetListingTimeLeftOrCompleted(listing);
        }




        public static string GetListingTimeLeftOrCompleted(MarketListing listing)
        {
            if (listing == null) return "Unknown";


            if (listing.Status != "active")
                return "Sold";


            if (!listing.ExpiresAt.HasValue)
                return "24 hrs";

            var left = listing.ExpiresAt.Value - DateTime.UtcNow;
            if (left.TotalMinutes < 1) return "Expired";
            if (left.TotalMinutes < 60) return $"{(int)left.TotalMinutes} mins left";
            if (left.TotalHours < 24) return $"{(int)left.TotalHours} hrs left";
            var days = (int)left.TotalDays;
            return days == 1 ? "1 day left" : days + " days left";
        }

        public static MarketListing GetListing(int id)
        {
            var sql = "SELECT id, item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, expires_at, status FROM " + TableName + " WHERE id = @id";
            var dt = DatabaseManager.Instance.ExecuteQuery(sql, new MySqlParameter("@id", id));
            var list = DataTableToListings(dt);
            return list.Count > 0 ? list[0] : null;
        }




        public static List<MarketListing> GetClaimableListings(ulong sellerSteamId)
        {
            var sql = "SELECT id, item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, expires_at, status FROM " + TableName +
                " WHERE seller_id = @seller_id AND status = 'active' AND quantity > 0 AND expires_at < NOW() ORDER BY id ASC";
            var dt = DatabaseManager.Instance.ExecuteQuery(sql, new MySqlParameter("@seller_id", sellerSteamId));
            return DataTableToListings(dt);
        }




        public static List<MarketListing> GetListingsThatJustExpired()
        {
            var sql = "SELECT id, item_id, item_name, category, price, seller_name, seller_id, quality, state, quantity, listed_at, expires_at, status FROM " + TableName +
                " WHERE status = 'active' AND quantity > 0 AND expires_at < NOW() AND expiry_logged_at IS NULL ORDER BY id ASC";
            var dt = DatabaseManager.Instance.ExecuteQuery(sql);
            return DataTableToListings(dt);
        }




        public static void MarkExpiryLogged(int id)
        {
            DatabaseManager.Instance.ExecuteNonQuery("UPDATE " + TableName + " SET expiry_logged_at = @at WHERE id = @id",
                new MySqlParameter("@at", DateTime.UtcNow), new MySqlParameter("@id", id));
        }




        public static bool MarkListingClaimed(int id)
        {
            var sql = "UPDATE " + TableName + " SET status = 'claimed', completed_at = @completed_at, quantity = 0 WHERE id = @id";
            var rows = DatabaseManager.Instance.ExecuteNonQuery(sql, new MySqlParameter("@completed_at", DateTime.UtcNow), new MySqlParameter("@id", id));
            return rows > 0;
        }

        public static bool CancelListing(int id, ulong sellerId)
        {
            var listing = GetListing(id);
            if (listing == null) return false;

            if (listing.SellerId != sellerId) return false;

            var sql = "UPDATE " + TableName + " SET status = 'cancelled', completed_at = @completed_at, quantity = 0 WHERE id = @id";
            var rows = DatabaseManager.Instance.ExecuteNonQuery(sql,
                new MySqlParameter("@completed_at", DateTime.UtcNow),
                new MySqlParameter("@id", id));

            return rows > 0;
        }




        public static void UpdateListingQuantityAfterClaim(int id, int remainingQuantity)
        {
            if (remainingQuantity <= 0)
                MarkListingClaimed(id);
            else
                DatabaseManager.Instance.ExecuteNonQuery("UPDATE " + TableName + " SET quantity = @quantity WHERE id = @id",
                    new MySqlParameter("@quantity", (byte)remainingQuantity), new MySqlParameter("@id", id));
        }




        public static bool RemoveListing(int id)
        {
            var sql = "UPDATE " + TableName + " SET status = 'sold_out', completed_at = @completed_at, quantity = 0 WHERE id = @id";
            var rows = DatabaseManager.Instance.ExecuteNonQuery(sql, new MySqlParameter("@completed_at", DateTime.UtcNow), new MySqlParameter("@id", id));
            return rows > 0;
        }




        public static bool DecrementListingQuantity(int id, byte amount)
        {
            var listing = GetListing(id);
            if (listing == null) return false;
            var newQty = (int)listing.Quantity - amount;
            if (newQty <= 0)
            {
                var sql = "UPDATE " + TableName + " SET status = 'sold_out', completed_at = @completed_at, quantity = 0 WHERE id = @id";
                DatabaseManager.Instance.ExecuteNonQuery(sql, new MySqlParameter("@completed_at", DateTime.UtcNow), new MySqlParameter("@id", id));
                return false;
            }
            var updateSql = "UPDATE " + TableName + " SET quantity = @quantity WHERE id = @id";
            DatabaseManager.Instance.ExecuteNonQuery(updateSql, new MySqlParameter("@quantity", (byte)newQty), new MySqlParameter("@id", id));
            return true;
        }

        public static int GetTotalPages(string category = "all", int itemsPerPage = 10)
        {
            return GetTotalPages(category, itemsPerPage, "");
        }

        public static int GetTotalPages(string category, int itemsPerPage, string searchTerm)
        {
            string sql;
            MySqlParameter[] parameters;

            if (!string.IsNullOrEmpty(searchTerm))
            {
                sql = "SELECT COUNT(*) FROM " + TableName + " WHERE status = 'active' AND quantity > 0 AND item_name LIKE @search";
                parameters = new[] { new MySqlParameter("@search", "%" + searchTerm + "%") };
            }
            else if (category != "all")
            {
                sql = "SELECT COUNT(*) FROM " + TableName + " WHERE status = 'active' AND quantity > 0 AND category = @category";
                parameters = new[] { new MySqlParameter("@category", category) };
            }
            else
            {
                sql = "SELECT COUNT(*) FROM " + TableName + " WHERE status = 'active' AND quantity > 0";
                parameters = null;
            }

            var result = DatabaseManager.Instance.ExecuteScalar(sql, parameters);
            var count = result != null ? Convert.ToInt32(result) : 0;
            return (int)Math.Ceiling((double)count / itemsPerPage);
        }

        private static DateTime AsUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc) return dt;
            if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        private static List<MarketListing> DataTableToListings(DataTable dt)
        {
            var list = new List<MarketListing>();
            if (dt == null || dt.Rows.Count == 0) return list;

            foreach (DataRow row in dt.Rows)
            {
                var stateObj = row["state"];
                byte[] state = null;
                if (stateObj != null && stateObj != DBNull.Value && stateObj is byte[] bytes)
                    state = bytes;

                list.Add(new MarketListing
                {
                    Id = Convert.ToInt32(row["id"]),
                    ItemId = Convert.ToUInt16(row["item_id"]),
                    ItemName = row["item_name"]?.ToString() ?? "",
                    Category = row["category"]?.ToString() ?? "other",
                    Price = Convert.ToDecimal(row["price"]),
                    SellerName = row["seller_name"]?.ToString() ?? "",
                    SellerId = Convert.ToUInt64(row["seller_id"]),
                    Quality = Convert.ToByte(row["quality"]),
                    State = state,
                    Quantity = Convert.ToByte(row["quantity"]),
                    ListedAt = row["listed_at"] != DBNull.Value ? AsUtc(Convert.ToDateTime(row["listed_at"])) : DateTime.UtcNow,
                    ExpiresAt = row.Table.Columns.Contains("expires_at") && row["expires_at"] != null && row["expires_at"] != DBNull.Value ? (DateTime?)AsUtc(Convert.ToDateTime(row["expires_at"])) : null,
                    Status = row.Table.Columns.Contains("status") ? row["status"]?.ToString() ?? "active" : "active"
                });
            }
            return list;
        }
    }
}