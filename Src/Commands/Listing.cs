using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UrpUnturnov.Systems;
using UrpUnturnov.Data;
using UnityEngine;
using URPUnturnov;
using UrpUnturnov.Logging;

namespace UrpUnturnov.Commands
{
    public class ListingCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "listing";
        public string Help => "View flea market listings";
        public string Syntax => "/listing [category] [page] [search]";
        public List<string> Aliases => new List<string> { "listings", "market" };
        public List<string> Permissions => new List<string> { "fleamarket.listing" };

        public const ushort MARKET_GUI_ID = 26000;
        public const short MARKET_GUI_KEY = 26001;

        private static HashSet<CSteamID> playersWithMarketGUIOpen = new HashSet<CSteamID>();
        private static Dictionary<CSteamID, string> playerSearchTerms = new Dictionary<CSteamID, string>();
        private static Dictionary<CSteamID, string> playerCategories = new Dictionary<CSteamID, string>();
        private static Dictionary<CSteamID, int> playerPages = new Dictionary<CSteamID, int>();
        private static Dictionary<CSteamID, Dictionary<int, int>> playerQuantitySelections = new Dictionary<CSteamID, Dictionary<int, int>>();
        private static Dictionary<CSteamID, bool> playerViewingMyListings = new Dictionary<CSteamID, bool>();
        private static Dictionary<CSteamID, int> playerSelectedListing = new Dictionary<CSteamID, int>();
        private static Dictionary<CSteamID, int> playerSelectedListingId = new Dictionary<CSteamID, int>();
        private static Dictionary<CSteamID, List<int>> playerListingIds = new Dictionary<CSteamID, List<int>>();
        private static Dictionary<CSteamID, List<MarketListing>> playerListingsCache = new Dictionary<CSteamID, List<MarketListing>>();
        private static Dictionary<CSteamID, string> playerSortOrders = new Dictionary<CSteamID, string>();


        private static Dictionary<CSteamID, string> playerModalItemName = new Dictionary<CSteamID, string>();
        private static Dictionary<CSteamID, decimal> playerModalPrice = new Dictionary<CSteamID, decimal>();
        private static Dictionary<CSteamID, int> playerModalStock = new Dictionary<CSteamID, int>();

        private static bool eventHandlersRegistered = false;

        private class InventoryItem
        {
            public ushort ItemId { get; set; }
            public string ItemName { get; set; }
            public byte Quality { get; set; }
            public byte[] State { get; set; }
            public int TotalQuantity { get; set; }
            public List<ItemLocation> Locations { get; set; }

            public InventoryItem()
            {
                Locations = new List<ItemLocation>();
            }
        }

        private class ItemLocation
        {
            public byte Page { get; set; }
            public byte Index { get; set; }
            public byte Amount { get; set; }
        }

        static ListingCommand()
        {
            RegisterEventHandlers();
        }

        public static void RegisterEventHandlers()
        {
            if (!eventHandlersRegistered)
            {
                EffectManager.onEffectButtonClicked += OnButtonClicked;
                EffectManager.onEffectTextCommitted += OnTextCommitted;
                Provider.onServerDisconnected += OnPlayerDisconnected;
                eventHandlersRegistered = true;
            }
        }

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            string category = "all";
            int page = 1;
            string searchTerm = "";

            if (command.Length > 0)
                category = command[0].ToLower();

            if (command.Length > 1)
            {
                if (!int.TryParse(command[1], out page) || page < 1)
                    page = 1;
            }

            if (command.Length > 2)
            {
                searchTerm = string.Join(" ", command, 2, command.Length - 2);
            }

            OpenMarketGUI(player, category, page, searchTerm);
        }

        private static void OnPlayerDisconnected(CSteamID steamID)
        {
            playersWithMarketGUIOpen.Remove(steamID);
            playerSearchTerms.Remove(steamID);
            playerCategories.Remove(steamID);
            playerPages.Remove(steamID);
            playerQuantitySelections.Remove(steamID);
            playerViewingMyListings.Remove(steamID);
            playerSelectedListing.Remove(steamID);
            playerSelectedListingId.Remove(steamID);
            playerListingIds.Remove(steamID);
            playerListingsCache.Remove(steamID);
            playerSortOrders.Remove(steamID);

            playerModalItemName.Remove(steamID);
            playerModalPrice.Remove(steamID);
            playerModalStock.Remove(steamID);
        }

        private static string FormatPrice(decimal price)
        {
            return price.ToString("N0");
        }

        private static string GetRarityColorHex(EItemRarity rarity)
        {
            switch (rarity)
            {
                case EItemRarity.COMMON: return "#b0c3d0";
                case EItemRarity.UNCOMMON: return "#3a962f";
                case EItemRarity.RARE: return "#3264ff";
                case EItemRarity.EPIC: return "#a335ee";
                case EItemRarity.LEGENDARY: return "#ff8000";
                case EItemRarity.MYTHICAL: return "#ff55ff";
                default: return "#ffffff";
            }
        }

        private static string GetDisplayName(string name, ulong steamId)
        {
            string result = "";
            foreach (char c in name)
            {
                if (c >= 32 && c <= 126)
                {
                    result += c;
                }
            }

            if (string.IsNullOrWhiteSpace(result))
            {
                var steamPlayer = Provider.clients.Find(p => p.playerID.steamID.m_SteamID == steamId);
                if (steamPlayer != null)
                {
                    return steamPlayer.playerID.playerName;
                }
                return "Unknown Seller";
            }

            return result;
        }

        private static string GetWeaponAttachmentsText(byte[] state)
        {
            if (state == null || state.Length < 18)
                return "";

            List<string> attachments = new List<string>();

            ushort sightId = BitConverter.ToUInt16(state, 0);
            if (sightId != 0)
            {
                ItemAsset sightAsset = Assets.find(EAssetType.ITEM, sightId) as ItemAsset;
                if (sightAsset != null)
                {
                    attachments.Add($"- {sightAsset.itemName} (sight)");
                }
            }

            ushort tacticalId = BitConverter.ToUInt16(state, 2);
            if (tacticalId != 0)
            {
                ItemAsset tacticalAsset = Assets.find(EAssetType.ITEM, tacticalId) as ItemAsset;
                if (tacticalAsset != null)
                {
                    attachments.Add($"- {tacticalAsset.itemName} (tactical)");
                }
            }

            ushort gripId = BitConverter.ToUInt16(state, 4);
            if (gripId != 0)
            {
                ItemAsset gripAsset = Assets.find(EAssetType.ITEM, gripId) as ItemAsset;
                if (gripAsset != null)
                {
                    attachments.Add($"- {gripAsset.itemName} (grip)");
                }
            }

            ushort barrelId = BitConverter.ToUInt16(state, 6);
            if (barrelId != 0)
            {
                ItemAsset barrelAsset = Assets.find(EAssetType.ITEM, barrelId) as ItemAsset;
                if (barrelAsset != null)
                {
                    attachments.Add($"- {barrelAsset.itemName} (barrel)");
                }
            }

            ushort magazineId = BitConverter.ToUInt16(state, 8);
            if (magazineId != 0)
            {
                ItemAsset magazineAsset = Assets.find(EAssetType.ITEM, magazineId) as ItemAsset;
                if (magazineAsset != null)
                {
                    attachments.Add($"- {magazineAsset.itemName} (magazine)");
                }
            }

            if (attachments.Count == 0)
                return "";

            return string.Join("\n", attachments);
        }

        private static bool IsWeapon(ushort itemId)
        {
            ItemAsset asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            return asset != null && asset is ItemGunAsset;
        }

        private static string GetItemCategory(ushort itemId)
        {
            ItemAsset asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            if (asset == null) return "other";

            if (asset is ItemGunAsset) return "weapons";
            if (asset.type == EItemType.MAGAZINE) return "ammo";
            if (asset.type == EItemType.MEDICAL) return "medical";
            if (asset.type == EItemType.FOOD || asset.type == EItemType.WATER) return "food";
            if (asset.type == EItemType.SHIRT || asset.type == EItemType.PANTS || asset.type == EItemType.BACKPACK || 
                asset.type == EItemType.VEST || asset.type == EItemType.MASK || asset.type == EItemType.GLASSES || asset.type == EItemType.HAT) return "clothing";

            return "other";
        }

        private static List<InventoryItem> GetPlayerInventoryItems(UnturnedPlayer player, string category = "all", string search = "")
        {
            Dictionary<string, InventoryItem> itemMap = new Dictionary<string, InventoryItem>();

            Items[] inventoryPages = player.Player.inventory.items;
            if (inventoryPages != null)
            {
                for (byte pageIndex = 0; pageIndex < inventoryPages.Length; pageIndex++)
                {
                    Items currentPage = inventoryPages[pageIndex];
                    if (currentPage != null && currentPage.items != null)
                    {
                        for (byte itemIndex = 0; itemIndex < currentPage.items.Count; itemIndex++)
                        {
                            ItemJar inventoryItem = currentPage.items[itemIndex];
                            if (inventoryItem != null && inventoryItem.item != null)
                            {
                                ushort itemId = inventoryItem.item.id;
                                ItemAsset itemAsset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
                                if (itemAsset == null) continue;

                                string itemCategory = GetItemCategory(itemId);
                                if (category != "all" && itemCategory != category) continue;

                                string itemName = itemAsset.itemName;
                                if (!string.IsNullOrEmpty(search) && !itemName.ToLower().Contains(search.ToLower())) continue;

                                string key = itemId.ToString();
                                bool isWeapon = IsWeapon(itemId);

                                if (isWeapon && inventoryItem.item.state != null && inventoryItem.item.state.Length >= 18)
                                {
                                    key += "_" + BitConverter.ToString(inventoryItem.item.state, 0, 18);
                                }

                                if (!itemMap.ContainsKey(key))
                                {
                                    itemMap[key] = new InventoryItem
                                    {
                                        ItemId = itemId,
                                        ItemName = itemName,
                                        Quality = inventoryItem.item.quality,
                                        State = inventoryItem.item.state,
                                        TotalQuantity = 0
                                    };
                                }

                                itemMap[key].TotalQuantity += inventoryItem.item.amount;
                                itemMap[key].Locations.Add(new ItemLocation
                                {
                                    Page = pageIndex,
                                    Index = itemIndex,
                                    Amount = inventoryItem.item.amount
                                });
                            }
                        }
                    }
                }
            }

            return itemMap.Values.ToList();
        }

        private static void OnTextCommitted(Player player, string buttonName, string text)
        {
            UnturnedPlayer uPlayer = UnturnedPlayer.FromPlayer(player);

            if (!playersWithMarketGUIOpen.Contains(uPlayer.CSteamID))
                return;

            if (buttonName == "SearchInput")
            {
                playerSearchTerms[uPlayer.CSteamID] = text;
                string category = playerCategories.ContainsKey(uPlayer.CSteamID) ? playerCategories[uPlayer.CSteamID] : "all";
                bool viewingMyListings = playerViewingMyListings.ContainsKey(uPlayer.CSteamID) && playerViewingMyListings[uPlayer.CSteamID];
                UpdateMarketGUI(uPlayer, category, 1, text, viewingMyListings);
            }
            else if (buttonName == "NameInput")
            {
                playerModalItemName[uPlayer.CSteamID] = text;
                UpdateModalPreviewCard(uPlayer);
            }
            else if (buttonName == "PriceInput")
            {
                if (decimal.TryParse(text, out decimal price) && price > 0)
                {
                    playerModalPrice[uPlayer.CSteamID] = price;
                }
                else
                {
                    playerModalPrice[uPlayer.CSteamID] = 0;
                }
                UpdateModalPreviewCard(uPlayer);
            }
            else if (buttonName == "StockInput")
            {
                if (int.TryParse(text, out int stock) && stock > 0)
                {
                    playerModalStock[uPlayer.CSteamID] = stock;
                }
                else
                {
                    playerModalStock[uPlayer.CSteamID] = 0;
                }
                UpdateModalPreviewCard(uPlayer);
            }
        }

        private static void UpdateModalPreviewCard(UnturnedPlayer player)
        {
            string nameVal = playerModalItemName.ContainsKey(player.CSteamID) ? playerModalItemName[player.CSteamID] : "";
            decimal priceVal = playerModalPrice.ContainsKey(player.CSteamID) ? playerModalPrice[player.CSteamID] : 100m;
            int stockVal = playerModalStock.ContainsKey(player.CSteamID) ? playerModalStock[player.CSteamID] : 1;

            if (string.IsNullOrEmpty(nameVal))
                nameVal = "PREVIEW ITEM";

            ushort itemId = 0;
            var inventoryItems = GetPlayerInventoryItems(player, "all", nameVal);
            if (inventoryItems.Count > 0)
            {
                itemId = inventoryItems[0].ItemId;
            }
            else
            {
                var asset = Assets.find(EAssetType.ITEM).OfType<ItemAsset>().FirstOrDefault(x => x.itemName.IndexOf(nameVal, StringComparison.OrdinalIgnoreCase) >= 0);
                if (asset != null) itemId = asset.id;
            }

            var itemAsset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            string rarityHex = GetRarityColorHex(itemAsset?.rarity ?? EItemRarity.COMMON);
            string rarityStr = itemAsset?.rarity.ToString().ToLower() ?? "common";

            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CardName_Preview", $"<color={rarityHex}>{nameVal.ToUpper()}</color>");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CardPrice_Preview", $"${FormatPrice(priceVal)}");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CardStock_Preview", $"x{stockVal}");

            string iconUrl = $"https://icons.splitzservers.win/_{itemId}.png";
            EffectManager.sendUIEffectImageURL(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CardIcon_Preview", iconUrl);

            string borderUrl = $"https://icons.splitzservers.win/border_{rarityStr}.png";
            EffectManager.sendUIEffectImageURL(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CardBorder_Preview", borderUrl);
        }

        private static void OnButtonClicked(Player player, string buttonName)
        {
            UnturnedPlayer uPlayer = UnturnedPlayer.FromPlayer(player);

            if (!playersWithMarketGUIOpen.Contains(uPlayer.CSteamID))
                return;

            string currentCategory = playerCategories.ContainsKey(uPlayer.CSteamID) ? playerCategories[uPlayer.CSteamID] : "all";
            string currentSearch = playerSearchTerms.ContainsKey(uPlayer.CSteamID) ? playerSearchTerms[uPlayer.CSteamID] : "";
            int currentPage = playerPages.ContainsKey(uPlayer.CSteamID) ? playerPages[uPlayer.CSteamID] : 1;
            bool viewingMyListings = playerViewingMyListings.ContainsKey(uPlayer.CSteamID) && playerViewingMyListings[uPlayer.CSteamID];

            if (buttonName.StartsWith("CardBtn_"))
            {
                int index = int.Parse(buttonName.Split('_')[1]);
                if (!playerListingIds.ContainsKey(uPlayer.CSteamID) || index >= playerListingIds[uPlayer.CSteamID].Count)
                {
                    UnturnedChat.Say(uPlayer, "Please refresh the list (change page or category).", Color.red);
                    return;
                }
                int listingId = playerListingIds[uPlayer.CSteamID][index];
                if (listingId <= 0) return;

                MarketListing selectedListing = null;
                if (playerListingsCache.ContainsKey(uPlayer.CSteamID) && index < playerListingsCache[uPlayer.CSteamID].Count)
                {
                    var cached = playerListingsCache[uPlayer.CSteamID][index];
                    if (cached != null && cached.Id == listingId)
                        selectedListing = cached;
                }
                if (selectedListing == null)
                    selectedListing = MarketManager.GetListing(listingId);
                if (selectedListing == null)
                {
                    UnturnedChat.Say(uPlayer, "That listing is no longer available.", Color.red);
                    return;
                }

                playerSelectedListing[uPlayer.CSteamID] = index;
                playerSelectedListingId[uPlayer.CSteamID] = listingId;
                int quantity = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
                UpdateDetailPanel(uPlayer, selectedListing, quantity);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, uPlayer.SteamPlayer().transportConnection, true, "DetailPanel", true);
                return;
            }

            if (buttonName == "DetailBuyButton")
            {
                if (!playerSelectedListingId.ContainsKey(uPlayer.CSteamID) || playerSelectedListingId[uPlayer.CSteamID] <= 0)
                {
                    UnturnedChat.Say(uPlayer, "Please select a listing first", Color.red);
                    return;
                }
                int listingId = playerSelectedListingId[uPlayer.CSteamID];
                var listing = MarketManager.GetListing(listingId);
                if (listing == null)
                {
                    UnturnedChat.Say(uPlayer, "That listing is no longer available.", Color.red);
                    return;
                }
                int index = playerSelectedListing.ContainsKey(uPlayer.CSteamID) ? playerSelectedListing[uPlayer.CSteamID] : 0;
                int quantity = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
                BuyItem(uPlayer, listing.Id, (byte)quantity);
                return;
            }

            if (buttonName == "DetailCancelButton")
            {
                if (!playerSelectedListingId.ContainsKey(uPlayer.CSteamID) || playerSelectedListingId[uPlayer.CSteamID] <= 0)
                {
                    UnturnedChat.Say(uPlayer, "Please select a listing first", Color.red);
                    return;
                }
                int listingId = playerSelectedListingId[uPlayer.CSteamID];
                var listing = MarketManager.GetListing(listingId);
                if (listing == null)
                {
                    UnturnedChat.Say(uPlayer, "That listing is no longer available.", Color.red);
                    return;
                }

                if (listing.SellerId != uPlayer.CSteamID.m_SteamID)
                {
                    UnturnedChat.Say(uPlayer, "You can only cancel your own listings.", Color.red);
                    return;
                }

                CancelListing(uPlayer, listing);
                return;
            }

            if (buttonName == "QtyIncBtn")
            {
                if (!playerSelectedListingId.ContainsKey(uPlayer.CSteamID) || playerSelectedListingId[uPlayer.CSteamID] <= 0)
                {
                    return;
                }
                int index = playerSelectedListing.ContainsKey(uPlayer.CSteamID) ? playerSelectedListing[uPlayer.CSteamID] : 0;
                MarketListing listingForDisplay = null;
                if (playerListingsCache.ContainsKey(uPlayer.CSteamID) && index < playerListingsCache[uPlayer.CSteamID].Count)
                {
                    var cached = playerListingsCache[uPlayer.CSteamID][index];
                    if (cached != null && cached.Id == playerSelectedListingId[uPlayer.CSteamID])
                        listingForDisplay = cached;
                }
                if (listingForDisplay == null)
                    listingForDisplay = MarketManager.GetListing(playerSelectedListingId[uPlayer.CSteamID]);
                if (listingForDisplay == null) return;
                var listingForQty = MarketManager.GetListing(playerSelectedListingId[uPlayer.CSteamID]);
                if (listingForQty == null) return;
                int currentQty = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
                int newQty = Mathf.Min(currentQty + 1, listingForQty.Quantity);
                SetPlayerQuantitySelection(uPlayer.CSteamID, index, newQty);
                UpdateDetailPanel(uPlayer, listingForDisplay, newQty);
                return;
            }

            if (buttonName == "QtyDecBtn")
            {
                if (!playerSelectedListingId.ContainsKey(uPlayer.CSteamID) || playerSelectedListingId[uPlayer.CSteamID] <= 0)
                {
                    return;
                }
                int index = playerSelectedListing.ContainsKey(uPlayer.CSteamID) ? playerSelectedListing[uPlayer.CSteamID] : 0;
                MarketListing listingForDisplay = null;
                if (playerListingsCache.ContainsKey(uPlayer.CSteamID) && index < playerListingsCache[uPlayer.CSteamID].Count)
                {
                    var cached = playerListingsCache[uPlayer.CSteamID][index];
                    if (cached != null && cached.Id == playerSelectedListingId[uPlayer.CSteamID])
                        listingForDisplay = cached;
                }
                if (listingForDisplay == null)
                    listingForDisplay = MarketManager.GetListing(playerSelectedListingId[uPlayer.CSteamID]);
                if (listingForDisplay == null) return;
                int currentQty = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
                int newQty = Mathf.Max(currentQty - 1, 1);
                SetPlayerQuantitySelection(uPlayer.CSteamID, index, newQty);
                UpdateDetailPanel(uPlayer, listingForDisplay, newQty);
                return;
            }

            if (buttonName == "CloseButton")
            {
                CloseMarketGUI(uPlayer);
                return;
            }

            if (buttonName == "PrevPage")
            {
                if (currentPage > 1)
                {
                    UpdateMarketGUI(uPlayer, currentCategory, currentPage - 1, currentSearch, viewingMyListings);
                }
                return;
            }

            if (buttonName == "NextPage")
            {
                int totalPages = viewingMyListings ? MarketManager.GetPlayerListingsTotalPages(uPlayer.CSteamID, 12) : MarketManager.GetTotalPages(currentCategory, 12, currentSearch);
                if (currentPage < totalPages)
                {
                    UpdateMarketGUI(uPlayer, currentCategory, currentPage + 1, currentSearch, viewingMyListings);
                }
                return;
            }

            if (buttonName == "MyListingsButton")
            {
                UpdateMarketGUI(uPlayer, "all", 1, currentSearch, true);
                return;
            }

            if (buttonName == "SellButton")
            {
                playerModalItemName[uPlayer.CSteamID] = "";
                playerModalPrice[uPlayer.CSteamID] = 100m;
                playerModalStock[uPlayer.CSteamID] = 1;
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, uPlayer.SteamPlayer().transportConnection, true, "NameInput", "");
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, uPlayer.SteamPlayer().transportConnection, true, "PriceInput", "100");
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, uPlayer.SteamPlayer().transportConnection, true, "StockInput", "1");
                UpdateModalPreviewCard(uPlayer);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, uPlayer.SteamPlayer().transportConnection, true, "CreateListingModal", true);
                return;
            }

            if (buttonName == "ModalCloseButton" || buttonName == "ModalCancelButton")
            {
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, uPlayer.SteamPlayer().transportConnection, true, "CreateListingModal", false);
                return;
            }

            if (buttonName == "PublishButton")
            {
                PublishListing(uPlayer);
                return;
            }

            if (buttonName == "SortPrice")
            {
                string currentSort = playerSortOrders.ContainsKey(uPlayer.CSteamID) ? playerSortOrders[uPlayer.CSteamID] : "id ASC";
                string newSort = (currentSort == "price ASC") ? "price DESC" : "price ASC";
                playerSortOrders[uPlayer.CSteamID] = newSort;
                UpdateMarketGUI(uPlayer, currentCategory, 1, currentSearch, viewingMyListings);
                return;
            }

            if (buttonName == "SortNewest")
            {
                string currentSort = playerSortOrders.ContainsKey(uPlayer.CSteamID) ? playerSortOrders[uPlayer.CSteamID] : "id ASC";
                string newSort = (currentSort == "listed_at DESC") ? "listed_at ASC" : "listed_at DESC";
                playerSortOrders[uPlayer.CSteamID] = newSort;
                UpdateMarketGUI(uPlayer, currentCategory, 1, currentSearch, viewingMyListings);
                return;
            }

            if (buttonName == "SortEnding")
            {
                string currentSort = playerSortOrders.ContainsKey(uPlayer.CSteamID) ? playerSortOrders[uPlayer.CSteamID] : "id ASC";
                string newSort = (currentSort == "expires_at ASC") ? "expires_at DESC" : "expires_at ASC";
                playerSortOrders[uPlayer.CSteamID] = newSort;
                UpdateMarketGUI(uPlayer, currentCategory, 1, currentSearch, viewingMyListings);
                return;
            }


            switch (buttonName)
            {
                case "CatAll":
                    UpdateMarketGUI(uPlayer, "all", 1, currentSearch, false);
                    break;
                case "CatWeapons":
                    UpdateMarketGUI(uPlayer, "weapons", 1, currentSearch, false);
                    break;
                case "CatAmmo":
                    UpdateMarketGUI(uPlayer, "ammo", 1, currentSearch, false);
                    break;
                case "CatMedical":
                    UpdateMarketGUI(uPlayer, "medical", 1, currentSearch, false);
                    break;
                case "CatFood":
                    UpdateMarketGUI(uPlayer, "food", 1, currentSearch, false);
                    break;
                case "CatClothing":
                    UpdateMarketGUI(uPlayer, "clothing", 1, currentSearch, false);
                    break;
                case "CatTools":
                    UpdateMarketGUI(uPlayer, "tools", 1, currentSearch, false);
                    break;
                case "CatVehicles":
                    UpdateMarketGUI(uPlayer, "vehicles", 1, currentSearch, false);
                    break;
                case "CatMisc":
                    UpdateMarketGUI(uPlayer, "other", 1, currentSearch, false);
                    break;
            }
        }

        private static int GetPlayerQuantitySelection(CSteamID steamID, int listingIndex)
        {
            if (!playerQuantitySelections.ContainsKey(steamID))
                playerQuantitySelections[steamID] = new Dictionary<int, int>();

            if (!playerQuantitySelections[steamID].ContainsKey(listingIndex))
                playerQuantitySelections[steamID][listingIndex] = 1;

            return playerQuantitySelections[steamID][listingIndex];
        }

        private static void SetPlayerQuantitySelection(CSteamID steamID, int listingIndex, int quantity)
        {
            if (!playerQuantitySelections.ContainsKey(steamID))
                playerQuantitySelections[steamID] = new Dictionary<int, int>();

            playerQuantitySelections[steamID][listingIndex] = quantity;
        }

        private static void ShowToast(UnturnedPlayer player, string message, bool success)
        {
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ToastText", message);
            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ToastBG", true);
            MainClass.Instance.StartCoroutine(HideToastAfterDelay(player, 3f));
        }

        private static System.Collections.IEnumerator HideToastAfterDelay(UnturnedPlayer player, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (playersWithMarketGUIOpen.Contains(player.CSteamID))
            {
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ToastBG", false);
            }
        }

        private static void PublishListing(UnturnedPlayer player)
        {
            string nameVal = playerModalItemName.ContainsKey(player.CSteamID) ? playerModalItemName[player.CSteamID] : "";
            decimal priceVal = playerModalPrice.ContainsKey(player.CSteamID) ? playerModalPrice[player.CSteamID] : 0m;
            int stockVal = playerModalStock.ContainsKey(player.CSteamID) ? playerModalStock[player.CSteamID] : 0;

            if (string.IsNullOrEmpty(nameVal))
            {
                ShowToast(player, "Item name cannot be empty!", false);
                return;
            }

            if (priceVal <= 0)
            {
                ShowToast(player, "Price must be greater than 0!", false);
                return;
            }

            if (stockVal <= 0)
            {
                ShowToast(player, "Stock must be greater than 0!", false);
                return;
            }

            var inventoryItems = GetPlayerInventoryItems(player, "all", nameVal);
            if (inventoryItems.Count == 0)
            {
                ShowToast(player, "You do not have that item!", false);
                return;
            }

            var item = inventoryItems[0];
            if (item.TotalQuantity < stockVal)
            {
                ShowToast(player, $"You only have {item.TotalQuantity}x of this item!", false);
                return;
            }

            var listing = new MarketListing
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                Category = GetItemCategory(item.ItemId),
                Price = priceVal,
                SellerName = player.DisplayName,
                SellerId = player.CSteamID.m_SteamID,
                Quality = item.Quality,
                State = item.State,
                Quantity = (byte)stockVal
            };

            var cfg = MainClass.Instance.Configuration.Instance;
            int expiryDays = cfg.ListingExpiryDaysDefault;

            if (cfg.ExpiryTiers != null && cfg.ExpiryTiers.Count > 0)
            {
                var orderedTiers = cfg.ExpiryTiers.OrderByDescending(t => t.Days).ToList();
                bool foundTier = false;
                var playerGroups = Rocket.Core.R.Permissions.GetGroups(player, false);
                foreach (var tier in orderedTiers)
                {
                    string groupId = tier.EffectiveGroupId;
                    if (string.IsNullOrEmpty(groupId)) continue;
                    foreach (var group in playerGroups)
                    {
                        if (group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase))
                        {
                            expiryDays = Math.Max(1, tier.Days);
                            foundTier = true;
                            break;
                        }
                    }
                    if (foundTier) break;
                }
            }

            int listingId = MarketManager.AddListing(listing, expiryDays);
            if (listingId <= 0)
            {
                ShowToast(player, "Failed to create listing. Try again.", false);
                return;
            }

            byte itemsToRemove = (byte)stockVal;
            foreach (var location in item.Locations)
            {
                if (itemsToRemove == 0) break;
                Items pageData = player.Player.inventory.items[location.Page];
                if (pageData != null && location.Index < pageData.items.Count)
                {
                    ItemJar jar = pageData.items[location.Index];
                    if (jar != null && jar.item != null)
                    {
                        byte removeAmount = (byte)Math.Min(itemsToRemove, jar.item.amount);
                        player.Player.inventory.removeItem(location.Page, location.Index);
                        if (jar.item.amount > removeAmount)
                        {
                            Item newItem = new Item(jar.item.id, (byte)(jar.item.amount - removeAmount), jar.item.quality, jar.item.state);
                            player.Player.inventory.tryAddItem(newItem, true);
                        }
                        itemsToRemove -= removeAmount;
                    }
                }
            }

            player.Player.inventory.sendStorage();
            ShowToast(player, $"Successfully listed {stockVal}x {item.ItemName}!", true);

            var fields = new Dictionary<string, string>
            {
                { "Listing ID", listingId.ToString() },
                { "Seller", player.DisplayName ?? player.CSteamID.ToString() },
                { "SteamID", player.CSteamID.m_SteamID.ToString() },
                { "Item", item.ItemName },
                { "Item ID", item.ItemId.ToString() },
                { "Quantity", stockVal.ToString() },
                { "Price/Unit", priceVal.ToString("N0") },
                { "Total", (priceVal * stockVal).ToString("N0") }
            };
            MainClass.LoggingCreated?.LogMessage("Listing Created", fields, WebhookLogLevel.Success);

            playerModalItemName.Remove(player.CSteamID);
            playerModalPrice.Remove(player.CSteamID);
            playerModalStock.Remove(player.CSteamID);

            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CreateListingModal", false);

            string currentCategory = playerCategories.ContainsKey(player.CSteamID) ? playerCategories[player.CSteamID] : "all";
            string currentSearch = playerSearchTerms.ContainsKey(player.CSteamID) ? playerSearchTerms[player.CSteamID] : "";
            int currentPage = playerPages.ContainsKey(player.CSteamID) ? playerPages[player.CSteamID] : 1;
            bool viewingMyListings = playerViewingMyListings.ContainsKey(player.CSteamID) && playerViewingMyListings[player.CSteamID];
            UpdateMarketGUI(player, currentCategory, currentPage, currentSearch, viewingMyListings);
        }

        private static void CancelListing(UnturnedPlayer player, MarketListing listing)
        {
            if (listing.SellerId != player.CSteamID.m_SteamID)
            {
                UnturnedChat.Say(player, "You can only cancel your own listings.", Color.red);
                return;
            }

            int itemsReturned = 0;
            for (int i = 0; i < listing.Quantity; i++)
            {
                Item item = new Item(listing.ItemId, 1, listing.Quality, listing.State);
                bool itemGiven = player.Player.inventory.tryAddItem(item, true);

                if (itemGiven)
                {
                    itemsReturned++;
                }
                else
                {
                    break;
                }
            }

            bool success = MarketManager.CancelListing(listing.Id, player.CSteamID.m_SteamID);

            if (success)
            {
                if (itemsReturned > 0)
                {
                    player.Player.inventory.sendStorage();
                    UnturnedChat.Say(player, $"Successfully cancelled listing for {listing.ItemName}. {itemsReturned} items returned to inventory.", Color.green);

                    if (itemsReturned < listing.Quantity)
                    {
                        UnturnedChat.Say(player, $"Warning: Only {itemsReturned} of {listing.Quantity} items returned (inventory full). {listing.Quantity - itemsReturned} items lost.", Color.yellow);
                    }
                }
                else
                {
                    UnturnedChat.Say(player, $"Successfully cancelled listing for {listing.ItemName}, but inventory was full. Items could not be returned.", Color.yellow);
                }

                var fields = new Dictionary<string, string>
                {
                    { "Listing ID", listing.Id.ToString() },
                    { "Seller", player.DisplayName ?? player.CSteamID.ToString() },
                    { "Seller SteamID", player.CSteamID.m_SteamID.ToString() },
                    { "Item", listing.ItemName },
                    { "Quantity Cancelled", listing.Quantity.ToString() },
                    { "Items Returned", itemsReturned.ToString() }
                };
                MainClass.LoggingPurchased?.LogMessage("Listing Cancelled", fields, WebhookLogLevel.Info);

                string currentCategory = playerCategories.ContainsKey(player.CSteamID) ? playerCategories[player.CSteamID] : "all";
                string currentSearch = playerSearchTerms.ContainsKey(player.CSteamID) ? playerSearchTerms[player.CSteamID] : "";
                int currentPage = playerPages.ContainsKey(player.CSteamID) ? playerPages[player.CSteamID] : 1;
                bool viewingMyListings = playerViewingMyListings.ContainsKey(player.CSteamID) && playerViewingMyListings[player.CSteamID];

                UpdateMarketGUI(player, currentCategory, currentPage, currentSearch, viewingMyListings);

                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailPanel", false);
            }
            else
            {
                UnturnedChat.Say(player, "Failed to cancel listing.", Color.red);
            }
        }

        private static void UpdateDetailPanel(UnturnedPlayer player, MarketListing listing, int selectedQuantity)
        {
            var asset = Assets.find(EAssetType.ITEM, listing.ItemId) as ItemAsset;
            string rarityHex = GetRarityColorHex(asset?.rarity ?? EItemRarity.COMMON);

            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailTitle", $"<color={rarityHex}>{listing.ItemName}</color>");

            string description = asset != null ? asset.itemDescription : "No description available.";
            string rarity = asset != null ? asset.rarity.ToString().ToUpper() : "COMMON";
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailDesc", $"<color={rarityHex}>{rarity}</color> - {description}");

            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SpecVal_0", GetDisplayName(listing.SellerName, listing.SellerId));

            var duration = MarketManager.GetListingTimeLeftOrCompleted(listing);
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SpecVal_1", duration);
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SpecVal_2", $"{listing.Quantity} units");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SpecVal_3", $"${FormatPrice(listing.Price)}");

            decimal totalCost = listing.Price * selectedQuantity;
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailQtyValue", selectedQuantity.ToString());

            string iconUrl = $"https://icons.splitzservers.win/_{listing.ItemId}.png";
            EffectManager.sendUIEffectImageURL(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailIcon", iconUrl);

            bool isOwnListing = listing.SellerId == player.CSteamID.m_SteamID;

            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SpecKey_2", isOwnListing ? "Qty to Cancel:" : "Stock Left:");

            if (isOwnListing)
            {
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailBuyButton", false);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailCancelButton", true);
            }
            else
            {
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailBuyButton", true);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailCancelButton", false);
            }
        }

        public static void OpenMarketGUIWithModal(UnturnedPlayer player)
        {
            if (playersWithMarketGUIOpen.Contains(player.CSteamID))
            {
                playerModalItemName[player.CSteamID] = "";
                playerModalPrice[player.CSteamID] = 100m;
                playerModalStock[player.CSteamID] = 1;
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NameInput", "");
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PriceInput", "100");
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "StockInput", "1");
                UpdateModalPreviewCard(player);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CreateListingModal", true);
                return;
            }

            OpenMarketGUI(player, "all", 1, "");
            playerModalItemName[player.CSteamID] = "";
            playerModalPrice[player.CSteamID] = 100m;
            playerModalStock[player.CSteamID] = 1;
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NameInput", "");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PriceInput", "100");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "StockInput", "1");
            UpdateModalPreviewCard(player);
            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CreateListingModal", true);
        }

        private static void OpenMarketGUI(UnturnedPlayer player, string category, int page, string searchTerm = "")
        {
            if (playersWithMarketGUIOpen.Contains(player.CSteamID))
            {
                UnturnedChat.Say(player, "Market GUI is already open", Color.yellow);
                return;
            }

            try
            {
                EffectAsset effectAsset = Assets.find(EAssetType.EFFECT, MARKET_GUI_ID) as EffectAsset;
                if (effectAsset != null)
                {
                    EffectManager.sendUIEffect(effectAsset.id, MARKET_GUI_KEY, player.Player.channel.owner.transportConnection, true);
                }
                else
                {
#pragma warning disable CS0618
                    EffectManager.sendUIEffect(MARKET_GUI_ID, MARKET_GUI_KEY, player.Player.channel.owner.transportConnection, true);
#pragma warning restore CS0618
                }

                playersWithMarketGUIOpen.Add(player.CSteamID);
                playerCategories[player.CSteamID] = category;
                playerSearchTerms[player.CSteamID] = searchTerm;
                playerPages[player.CSteamID] = page;
                playerQuantitySelections[player.CSteamID] = new Dictionary<int, int>();
                playerViewingMyListings[player.CSteamID] = false;
                playerSelectedListing[player.CSteamID] = -1;
                playerSelectedListingId[player.CSteamID] = 0;
                playerListingIds[player.CSteamID] = new List<int>(new int[12]);
                playerSortOrders[player.CSteamID] = "id ASC";

                uint playerExperience = player.Experience;
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CurrencyText", $"${FormatPrice(playerExperience)}");

                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailPanel", false);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CreateListingModal", false);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ToastBG", false);

                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SearchInput", searchTerm);

                UpdateMarketGUI(player, category, page, searchTerm, false);

                UnturnedChat.Say(player, "Market GUI opened", Color.cyan);
                player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Default);
            }
            catch (System.Exception ex)
            {
                UnturnedChat.Say(player, $"Failed to open market GUI: {ex.Message}", Color.red);
            }
        }

        private static void CloseMarketGUI(UnturnedPlayer player)
        {
            if (!playersWithMarketGUIOpen.Contains(player.CSteamID))
                return;

            try
            {
                EffectManager.askEffectClearByID(MARKET_GUI_ID, player.SteamPlayer().transportConnection);
                playersWithMarketGUIOpen.Remove(player.CSteamID);
                playerSearchTerms.Remove(player.CSteamID);
                playerCategories.Remove(player.CSteamID);
                playerPages.Remove(player.CSteamID);
                playerQuantitySelections.Remove(player.CSteamID);
                playerViewingMyListings.Remove(player.CSteamID);
                playerSelectedListing.Remove(player.CSteamID);
                playerSelectedListingId.Remove(player.CSteamID);
                playerListingIds.Remove(player.CSteamID);
                playerListingsCache.Remove(player.CSteamID);
                playerSortOrders.Remove(player.CSteamID);

                playerModalItemName.Remove(player.CSteamID);
                playerModalPrice.Remove(player.CSteamID);
                playerModalStock.Remove(player.CSteamID);

                UnturnedChat.Say(player, "Market GUI closed!", Color.red);
                player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Default);
            }
            catch (System.Exception)
            {
            }
        }

        private static void UpdateMarketGUI(UnturnedPlayer player, string category, int page, string searchTerm = "", bool viewMyListings = false)
        {
            try
            {
                playerCategories[player.CSteamID] = category;
                playerPages[player.CSteamID] = page;
                playerSearchTerms[player.CSteamID] = searchTerm;
                playerViewingMyListings[player.CSteamID] = viewMyListings;

                uint playerExperience = player.Experience;
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "CurrencyText", $"${FormatPrice(playerExperience)}");

                string sortOrder = playerSortOrders.ContainsKey(player.CSteamID) ? playerSortOrders[player.CSteamID] : "id ASC";

                string priceText = sortOrder.StartsWith("price") ? (sortOrder.EndsWith("ASC") ? "PRICE ▲" : "PRICE ▼") : "PRICE";
                string newestText = sortOrder.StartsWith("listed_at") ? (sortOrder.EndsWith("ASC") ? "NEWEST ▲" : "NEWEST ▼") : "NEWEST";
                string endingText = sortOrder.StartsWith("expires_at") ? (sortOrder.EndsWith("ASC") ? "ENDING ▲" : "ENDING ▼") : "ENDING";

                string priceColor = sortOrder.StartsWith("price") ? "#2ecc71" : "#ffffff";
                string newestColor = sortOrder.StartsWith("listed_at") ? "#2ecc71" : "#ffffff";
                string endingColor = sortOrder.StartsWith("expires_at") ? "#2ecc71" : "#ffffff";

                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SortPriceLbl", $"<color={priceColor}>{priceText}</color>");
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SortNewestLbl", $"<color={newestColor}>{newestText}</color>");
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SortEndingLbl", $"<color={endingColor}>{endingText}</color>");

                var allListings = viewMyListings ? MarketManager.GetPlayerListings(player.CSteamID, page, 12, sortOrder) : MarketManager.GetListings(category, page, 12, searchTerm, sortOrder);

                var listings = viewMyListings ? allListings : allListings.Where(l =>
                    l.Status == "active" &&
                    (!l.ExpiresAt.HasValue || l.ExpiresAt.Value > System.DateTime.UtcNow)
                ).ToList();

                int totalPages = viewMyListings ? MarketManager.GetPlayerListingsTotalPages(player.CSteamID, 12) : MarketManager.GetTotalPages(category, 12, searchTerm);
                if (totalPages <= 0) totalPages = 1;

                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PageText", $"{page}/{totalPages}");


                int totalCount = listings.Count; 
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ResultsCount", $"{totalCount} items found");

                var ids = new List<int>();
                var cacheList = new List<MarketListing>();
                for (int i = 0; i < 12; i++)
                {
                    ids.Add(i < listings.Count ? listings[i].Id : 0);
                    cacheList.Add(i < listings.Count ? listings[i] : null);
                }
                playerListingIds[player.CSteamID] = ids;
                playerListingsCache[player.CSteamID] = cacheList;

                for (int i = 0; i < 12; i++)
                {
                    if (i < listings.Count)
                    {
                        var listing = listings[i];

                        var asset = Assets.find(EAssetType.ITEM, listing.ItemId) as ItemAsset;
                        string rarityHex = GetRarityColorHex(asset?.rarity ?? EItemRarity.COMMON);
                        string rarityStr = asset?.rarity.ToString().ToLower() ?? "common";

                        EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"CardName_{i}", $"<color={rarityHex}>{listing.ItemName}</color>");
                        EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"CardStock_{i}", $"x{listing.Quantity}");
                        EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"CardPrice_{i}", $"${FormatPrice(listing.Price)}");

                        string iconUrl = $"https://icons.splitzservers.win/_{listing.ItemId}.png";
                        EffectManager.sendUIEffectImageURL(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"CardIcon_{i}", iconUrl);

                        string borderUrl = $"https://icons.splitzservers.win/border_{rarityStr}.png";
                        EffectManager.sendUIEffectImageURL(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"CardBorder_{i}", borderUrl);

                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"Card_{i}", true);
                    }
                    else
                    {
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"Card_{i}", false);
                    }
                }

                if (page > 1)
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PrevPage", true);
                }
                else
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PrevPage", false);
                }

                if (page < totalPages)
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NextPage", true);
                }
                else
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NextPage", false);
                }
            }
            catch (System.Exception)
            {
            }
        }

        private static void BuyItem(UnturnedPlayer player, int listingId, byte quantity)
        {
            var listing = MarketManager.GetListing(listingId);
            if (listing == null)
            {
                UnturnedChat.Say(player, $"Listing ID {listingId} not found!", Color.red);
                return;
            }

            if (listing.SellerId == player.CSteamID.m_SteamID)
            {
                UnturnedChat.Say(player, "You cannot buy your own listing.", Color.red);
                return;
            }

            if (quantity > listing.Quantity)
            {
                UnturnedChat.Say(player, $"Only {listing.Quantity} available! You tried to buy {quantity}", Color.red);
                return;
            }

            var totalCost = listing.Price * quantity;
            uint costUint = (uint)Math.Min((ulong)decimal.Truncate(totalCost), uint.MaxValue);
            if (costUint == 0)
            {
                UnturnedChat.Say(player, "Invalid price.", Color.red);
                return;
            }

            if (player.Experience < costUint)
            {
                UnturnedChat.Say(player, $"Not enough balance. You need ${FormatPrice(costUint)} (you have ${FormatPrice(player.Experience)}).", Color.red);
                return;
            }

            bool allItemsGiven = true;

            for (int i = 0; i < quantity; i++)
            {
                Item item = new Item(listing.ItemId, 1, listing.Quality, listing.State);
                bool itemGiven = player.Player.inventory.tryAddItem(item, true);

                if (!itemGiven)
                {
                    UnturnedChat.Say(player, $"Inventory full! Only gave you {i} out of {quantity} items.", Color.red);
                    allItemsGiven = false;
                    quantity = (byte)i;
                    break;
                }
            }

            if (allItemsGiven)
            {
                decimal feePercent = MainClass.Instance.Configuration.Instance.MarketFeePercent;
                decimal fee = totalCost * (feePercent / 100m);
                decimal sellerPayout = totalCost - fee;
                uint payoutUint = (uint)Math.Min((ulong)decimal.Truncate(sellerPayout), uint.MaxValue);

                if (fee > 0)
                    URPUnturnov.DatabaseManager.Instance.AddTotalTaxedMoney(fee);

                URPUnturnov.DatabaseManager.Instance.RecordTransaction(
                    listingId,
                    player.CSteamID.m_SteamID,
                    player.DisplayName ?? player.CSteamID.ToString(),
                    listing.SellerId,
                    listing.SellerName ?? "",
                    listing.ItemId,
                    listing.ItemName ?? "",
                    quantity,
                    totalCost,
                    fee,
                    sellerPayout);

                player.Experience -= costUint;

                var sellerPlayer = UnturnedPlayer.FromCSteamID(new CSteamID(listing.SellerId));
                if (sellerPlayer != null && payoutUint > 0)
                {
                    sellerPlayer.Experience = (uint)Math.Min((ulong)sellerPlayer.Experience + payoutUint, uint.MaxValue);
                    UnturnedChat.Say(sellerPlayer, $"You received ${FormatPrice(payoutUint)} from a sale ({quantity}x {listing.ItemName}).", Color.green);
                }

                MarketManager.DecrementListingQuantity(listingId, quantity);

                player.Player.inventory.sendStorage();

                var fields = new Dictionary<string, string>
                {
                    { "Listing ID", listingId.ToString() },
                    { "Buyer", player.DisplayName ?? player.CSteamID.ToString() },
                    { "Buyer SteamID", player.CSteamID.m_SteamID.ToString() },
                    { "Seller", listing.SellerName },
                    { "Seller SteamID", listing.SellerId.ToString() },
                    { "Item", listing.ItemName },
                    { "Quantity", quantity.ToString() },
                    { "Total", FormatPrice(totalCost) }
                };
                MainClass.LoggingPurchased?.LogMessage("Listing Purchased", fields, WebhookLogLevel.Success);

                UnturnedChat.Say(player, $"Successfully purchased {quantity}x {listing.ItemName} for ${FormatPrice(totalCost)}!", Color.green);

                string currentCategory = playerCategories.ContainsKey(player.CSteamID) ? playerCategories[player.CSteamID] : "all";
                string currentSearch = playerSearchTerms.ContainsKey(player.CSteamID) ? playerSearchTerms[player.CSteamID] : "";
                int currentPage = playerPages.ContainsKey(player.CSteamID) ? playerPages[player.CSteamID] : 1;
                bool viewingMyListings = playerViewingMyListings.ContainsKey(player.CSteamID) && playerViewingMyListings[player.CSteamID];

                UpdateMarketGUI(player, currentCategory, currentPage, currentSearch, viewingMyListings);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailPanel", false);
            }
        }

        public static void CloseAllMarketGUIs()
        {
            var playersToClose = new List<CSteamID>(playersWithMarketGUIOpen);
            foreach (var steamID in playersToClose)
            {
                var player = UnturnedPlayer.FromCSteamID(steamID);
                if (player != null)
                {
                    CloseMarketGUI(player);
                }
            }
        }

        public static void UnregisterEventHandlers()
        {
            if (eventHandlersRegistered)
            {
                EffectManager.onEffectButtonClicked -= OnButtonClicked;
                EffectManager.onEffectTextCommitted -= OnTextCommitted;
                Provider.onServerDisconnected -= OnPlayerDisconnected;
                eventHandlersRegistered = false;
            }
        }
    }
}