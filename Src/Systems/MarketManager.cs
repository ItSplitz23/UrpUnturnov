using System.Collections.Generic;
using System.Linq;
using SDG.Unturned;
using UrpUnturnov.Data;
using Steamworks;

namespace UrpUnturnov.Systems
{
    public static class MarketManager
    {
        private static List<MarketListing> _listings = new List<MarketListing>();
        private static int _nextId = 1;

        public static int AddListing(MarketListing listing)
        {
            listing.Id = _nextId++;
            listing.ListedAt = System.DateTime.Now;
            _listings.Add(listing);
            return listing.Id;
        }

        public static List<MarketListing> GetListings(string category = "all", int page = 1, int itemsPerPage = 10)
        {
            return GetListings(category, page, itemsPerPage, "");
        }

        public static List<MarketListing> GetListings(string category, int page, int itemsPerPage, string searchTerm)
        {
            var filteredListings = _listings.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                string lowerSearchTerm = searchTerm.ToLower();
                filteredListings = filteredListings.Where(l => 
                    l.ItemName.ToLower().Contains(lowerSearchTerm)
                );
            }
            else
            {
                if (category != "all")
                {
                    filteredListings = filteredListings.Where(l => GetItemCategory(l.ItemId) == category);
                }
            }

            return filteredListings
                .OrderBy(l => l.Id)
                .Skip((page - 1) * itemsPerPage)
                .Take(itemsPerPage)
                .ToList();
        }

        public static List<MarketListing> GetPlayerListings(CSteamID steamID, int page = 1, int itemsPerPage = 10)
        {
            return _listings
                .Where(l => l.SellerId == steamID.m_SteamID)
                .OrderBy(l => l.Id)
                .Skip((page - 1) * itemsPerPage)
                .Take(itemsPerPage)
                .ToList();
        }

        public static int GetPlayerListingsTotalPages(CSteamID steamID, int itemsPerPage = 10)
        {
            var count = _listings.Count(l => l.SellerId == steamID.m_SteamID);
            return (int)System.Math.Ceiling((double)count / itemsPerPage);
        }

        public static string GetListingDuration(int listingId)
        {
            var listing = _listings.FirstOrDefault(l => l.Id == listingId);
            if (listing == null) return "Unknown";

            var timeElapsed = System.DateTime.Now - listing.ListedAt;
            var daysListed = (int)timeElapsed.TotalDays;
            
            if (daysListed == 0)
                return "Today";
            else if (daysListed == 1)
                return "1 day ago";
            else
                return $"{daysListed} days ago";
        }

        public static MarketListing GetListing(int id)
        {
            return _listings.FirstOrDefault(l => l.Id == id);
        }

        public static bool RemoveListing(int id)
        {
            var listing = _listings.FirstOrDefault(l => l.Id == id);
            if (listing != null)
            {
                _listings.Remove(listing);
                return true;
            }
            return false;
        }

        public static int GetTotalPages(string category = "all", int itemsPerPage = 10)
        {
            return GetTotalPages(category, itemsPerPage, "");
        }

        public static int GetTotalPages(string category, int itemsPerPage, string searchTerm)
        {
            var filteredListings = _listings.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                string lowerSearchTerm = searchTerm.ToLower();
                filteredListings = filteredListings.Where(l => 
                    l.ItemName.ToLower().Contains(lowerSearchTerm)
                );
            }
            else
            {
                if (category != "all")
                {
                    filteredListings = filteredListings.Where(l => GetItemCategory(l.ItemId) == category);
                }
            }

            var count = filteredListings.Count();
            return (int)System.Math.Ceiling((double)count / itemsPerPage);
        }

        private static string GetItemCategory(ushort itemId)
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
    }
}