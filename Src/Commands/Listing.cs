using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System.Collections.Generic;
using UrpUnturnov.Systems;
using UrpUnturnov.Data;
using UnityEngine;

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

        public const ushort MARKET_GUI_ID = 25000;  
        public const short MARKET_GUI_KEY = 25001;  

        private static HashSet<CSteamID> playersWithMarketGUIOpen = new HashSet<CSteamID>();
        private static Dictionary<CSteamID, string> playerSearchTerms = new Dictionary<CSteamID, string>();
        private static Dictionary<CSteamID, string> playerCategories = new Dictionary<CSteamID, string>();
        private static Dictionary<CSteamID, int> playerPages = new Dictionary<CSteamID, int>();
        private static Dictionary<CSteamID, Dictionary<int, int>> playerQuantitySelections = new Dictionary<CSteamID, Dictionary<int, int>>();
        private static Dictionary<CSteamID, bool> playerViewingMyListings = new Dictionary<CSteamID, bool>();
        private static Dictionary<CSteamID, int> playerSelectedListing = new Dictionary<CSteamID, int>();
        private static bool eventHandlersRegistered = false;

        static ListingCommand()
        {
            RegisterEventHandlers();
        }

        private static void RegisterEventHandlers()
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
        }

        private static string FormatPrice(decimal price)
        {
            return price.ToString("N0");
        }
        
        private static void UpdateCategorySelection(UnturnedPlayer player, string selectedCategory, bool isMyListings)
        {
            string[] categories = { "MyListings", "All", "Weapons", "Ammo", "Medical", "Food", "Clothing", "Other" };
            
            foreach (string cat in categories)
            {
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"{cat}Selected", false);
            }
            
            if (isMyListings)
            {
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "MyListingsSelected", true);
            }
            else
            {
                string panelName = char.ToUpper(selectedCategory[0]) + selectedCategory.Substring(1);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"{panelName}Selected", true);
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

            if (buttonName.StartsWith("ListingRowButton_"))
            {
                int index = int.Parse(buttonName.Split('_')[1]);
    
                var listings = viewingMyListings 
                    ? MarketManager.GetPlayerListings(uPlayer.CSteamID, currentPage, 6) 
                    : MarketManager.GetListings(currentCategory, currentPage, 6, currentSearch);
    
                if (listings == null || listings.Count == 0)
                {
                    UnturnedChat.Say(uPlayer, "No listings available", Color.red);
                    return;
                }
    
                if (index >= listings.Count)
                {
                    UnturnedChat.Say(uPlayer, $"Invalid listing index {index}, only {listings.Count} available", Color.red);
                    return;
                }
    
                var selectedListing = listings[index];
                playerSelectedListing[uPlayer.CSteamID] = index;
    
                int quantity = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
    
                UpdateDetailPanel(uPlayer, selectedListing, quantity);
                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, uPlayer.SteamPlayer().transportConnection, true, "InfoHeader", true);
    
                return;
            }

            if (buttonName == "DetailBuyButton")
            {
                if (!playerSelectedListing.ContainsKey(uPlayer.CSteamID) || playerSelectedListing[uPlayer.CSteamID] == -1)
                {
                    UnturnedChat.Say(uPlayer, "Please select a listing first", Color.red);
                    return;
                }
                
                int index = playerSelectedListing[uPlayer.CSteamID];
                var listings = viewingMyListings ? MarketManager.GetPlayerListings(uPlayer.CSteamID, currentPage, 6) : MarketManager.GetListings(currentCategory, currentPage, 6, currentSearch);
                
                if (index < listings.Count)
                {
                    var listing = listings[index];
                    int quantity = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
                    BuyItem(uPlayer, listing.Id, (byte)quantity);
                }
                return;
            }

            if (buttonName == "DetailQtyUp")
            {
                if (!playerSelectedListing.ContainsKey(uPlayer.CSteamID) || playerSelectedListing[uPlayer.CSteamID] == -1)
                {
                    return;
                }
                
                int index = playerSelectedListing[uPlayer.CSteamID];
                var listings = viewingMyListings ? MarketManager.GetPlayerListings(uPlayer.CSteamID, currentPage, 6) : MarketManager.GetListings(currentCategory, currentPage, 6, currentSearch);
                
                if (index < listings.Count)
                {
                    var listing = listings[index];
                    int currentQty = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
                    int newQty = Mathf.Min(currentQty + 1, listing.Quantity);
                    SetPlayerQuantitySelection(uPlayer.CSteamID, index, newQty);
                    UpdateDetailPanel(uPlayer, listing, newQty);
                }
                return;
            }

            if (buttonName == "DetailQtyDown")
            {
                if (!playerSelectedListing.ContainsKey(uPlayer.CSteamID) || playerSelectedListing[uPlayer.CSteamID] == -1)
                {
                    return;
                }
                
                int index = playerSelectedListing[uPlayer.CSteamID];
                var listings = viewingMyListings ? MarketManager.GetPlayerListings(uPlayer.CSteamID, currentPage, 6) : MarketManager.GetListings(currentCategory, currentPage, 6, currentSearch);
                
                if (index < listings.Count)
                {
                    var listing = listings[index];
                    int currentQty = GetPlayerQuantitySelection(uPlayer.CSteamID, index);
                    int newQty = Mathf.Max(currentQty - 1, 1);
                    SetPlayerQuantitySelection(uPlayer.CSteamID, index, newQty);
                    UpdateDetailPanel(uPlayer, listing, newQty);
                }
                return;
            }

            switch (buttonName)
            {
                case "CloseButton":
                    CloseMarketGUI(uPlayer);
                    break;
                case "PrevButton":
                    if (currentPage > 1)
                    {
                        UpdateMarketGUI(uPlayer, currentCategory, currentPage - 1, currentSearch, viewingMyListings);
                    }
                    break;
                case "NextButton":
                    int totalPages = viewingMyListings ? MarketManager.GetPlayerListingsTotalPages(uPlayer.CSteamID, 6) : MarketManager.GetTotalPages(currentCategory, 6, currentSearch);
                    if (currentPage < totalPages)
                    {
                        UpdateMarketGUI(uPlayer, currentCategory, currentPage + 1, currentSearch, viewingMyListings);
                    }
                    break;
                case "CategoryMyListings":
                    UpdateCategorySelection(uPlayer, "all", true);
                    UpdateMarketGUI(uPlayer, "all", 1, currentSearch, true);
                    break;
                case "CategoryAll":
                    UpdateCategorySelection(uPlayer, "all", false);
                    UpdateMarketGUI(uPlayer, "all", 1, currentSearch, false);
                    break;
                case "CategoryWeapons":
                    UpdateCategorySelection(uPlayer, "weapons", false);
                    UpdateMarketGUI(uPlayer, "weapons", 1, currentSearch, false);
                    break;
                case "CategoryAmmo":
                    UpdateCategorySelection(uPlayer, "ammo", false);
                    UpdateMarketGUI(uPlayer, "ammo", 1, currentSearch, false);
                    break;
                case "CategoryMedical":
                    UpdateCategorySelection(uPlayer, "medical", false);
                    UpdateMarketGUI(uPlayer, "medical", 1, currentSearch, false);
                    break;
                case "CategoryFood":
                    UpdateCategorySelection(uPlayer, "food", false);
                    UpdateMarketGUI(uPlayer, "food", 1, currentSearch, false);
                    break;
                case "CategoryClothing":
                    UpdateCategorySelection(uPlayer, "clothing", false);
                    UpdateMarketGUI(uPlayer, "clothing", 1, currentSearch, false);
                    break;
                case "CategoryOther":
                    UpdateCategorySelection(uPlayer, "other", false);
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

        private static void UpdateDetailPanel(UnturnedPlayer player, MarketListing listing, int selectedQuantity)
        {
            
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailItemName", listing.ItemName);
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailItemID", $"ITEM ID #{listing.ItemId}");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailSeller", GetDisplayName(listing.SellerName, listing.SellerId));
            
            var duration = MarketManager.GetListingDuration(listing.Id);
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailDuration", duration);
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailAvailable", $"{listing.Quantity} units");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailUnitPrice", $"${FormatPrice(listing.Price)}");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailPricePerUnit", $"${FormatPrice(listing.Price)}");
            
            decimal totalCost = listing.Price * selectedQuantity;
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailTotalCost", $"${FormatPrice(totalCost)}");
            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "DetailQuantity", selectedQuantity.ToString());
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

                uint playerExperience = player.Experience;
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "BalanceText", $"${FormatPrice(playerExperience)}");

                EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "InfoHeader", false);
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "SearchInput", searchTerm);

                UpdateMarketGUI(player, category, page, searchTerm, false);
                UpdateCategorySelection(player, category, false);

                UnturnedChat.Say(player, "Market GUI opened", Color.cyan);
                player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Default);
            }
            catch (System.Exception)
            {
                UnturnedChat.Say(player, "Failed to open market GUI, Please try again", Color.red);
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
                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "BalanceText", $"${FormatPrice(playerExperience)}");

                var listings = viewMyListings ? MarketManager.GetPlayerListings(player.CSteamID, page, 6) : MarketManager.GetListings(category, page, 6, searchTerm);
                int totalPages = viewMyListings ? MarketManager.GetPlayerListingsTotalPages(player.CSteamID, 6) : MarketManager.GetTotalPages(category, 6, searchTerm);

                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PageInfo", $"Page {page} of {totalPages}");
                if (totalPages > 1)
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PageInfo", true);
                }
                else
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PageInfo", false);
                }

                if (listings.Count == 0)
                {
                    string noItemsMessage;
                    if (viewMyListings)
                    {
                        noItemsMessage = "You have no items listed in the market.";
                    }
                    else
                    {
                        noItemsMessage = string.IsNullOrEmpty(searchTerm) 
                            ? "No items currently listed in the market." 
                            : $"No items found matching '{searchTerm}'.";
                    }
                    
                    EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NoItemsText", noItemsMessage);
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NoItemsText", true);
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ListingsPanel", false);

                    for (int i = 0; i < 6; i++)
                    {
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ListingRow_{i}", false);
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemIcon_{i}", false);
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ClockIcon_{i}", false);
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"PersonIcon_{i}", false);
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"DivBarTop_{i}", false);
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"DivBarBottom_{i}", false);
                    }
                }
                else
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NoItemsText", false);
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ListingsPanel", true);

                    for (int i = 0; i < 6; i++)
                    {
                        if (i < listings.Count)
                        {
                            var listing = listings[i];
                            
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemName_{i}", listing.ItemName);
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemStock_{i}", $"{listing.Quantity} Left");
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemSeller_{i}", GetDisplayName(listing.SellerName, listing.SellerId));
                            
                            var duration = MarketManager.GetListingDuration(listing.Id);
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemTime_{i}", duration);
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemPrice_{i}", $"${FormatPrice(listing.Price)}");
                            
                            // Set the item icon URL
                            string iconUrl = $"https://icons.splitzservers.win/_{listing.ItemId}.png";
                            EffectManager.sendUIEffectImageURL(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemIcon_{i}", iconUrl);
                            
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ListingRow_{i}", true);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemIcon_{i}", true);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ClockIcon_{i}", true);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"PersonIcon_{i}", true);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"DivBarTop_{i}", true);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"DivBarBottom_{i}", true);
                        }
                        else
                        {
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ListingRow_{i}", false);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemIcon_{i}", false);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ClockIcon_{i}", false);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"PersonIcon_{i}", false);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"DivBarTop_{i}", false);
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"DivBarBottom_{i}", false);
                        }
                    }
                }

                if (page > 1)
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PrevButton", true);
                }
                else
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PrevButton", false);
                }

                if (page < totalPages)
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NextButton", true);
                }
                else
                {
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NextButton", false);
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

            if (quantity > listing.Quantity)
            {
                UnturnedChat.Say(player, $"Only {listing.Quantity} available! You tried to buy {quantity}", Color.red);
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
                listing.Quantity -= quantity;
                
                if (listing.Quantity <= 0)
                {
                    MarketManager.RemoveListing(listingId);
                }

                player.Player.inventory.sendStorage();

                UnturnedChat.Say(player, $"Successfully purchased {quantity}x {listing.ItemName} for ${FormatPrice(listing.Price * quantity)}!", Color.green);
                
                string currentCategory = playerCategories.ContainsKey(player.CSteamID) ? playerCategories[player.CSteamID] : "all";
                string currentSearch = playerSearchTerms.ContainsKey(player.CSteamID) ? playerSearchTerms[player.CSteamID] : "";
                int currentPage = playerPages.ContainsKey(player.CSteamID) ? playerPages[player.CSteamID] : 1;
                bool viewingMyListings = playerViewingMyListings.ContainsKey(player.CSteamID) && playerViewingMyListings[player.CSteamID];
                
                UpdateMarketGUI(player, currentCategory, currentPage, currentSearch, viewingMyListings);
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