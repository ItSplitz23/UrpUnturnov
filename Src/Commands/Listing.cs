using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System.Collections.Generic;
using UrpUnturnov.Systems;
using UnityEngine;

namespace UrpUnturnov.Commands
{
    public class ListingCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "listing";
        public string Help => "View flea market listings";
        public string Syntax => "/listing [category] [page]";
        public List<string> Aliases => new List<string> { "listings", "market" };
        public List<string> Permissions => new List<string> { "fleamarket.listing" };

        public const ushort MARKET_GUI_ID = 25000;  
        public const short MARKET_GUI_KEY = 25001;  

        private static HashSet<CSteamID> playersWithMarketGUIOpen = new HashSet<CSteamID>();
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
                Provider.onServerDisconnected += OnPlayerDisconnected;
                eventHandlersRegistered = true;
            }
        }

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            
            string category = "all";
            int page = 1;
            
            if (command.Length > 0)
                category = command[0].ToLower();
            
            if (command.Length > 1)
            {
                if (!int.TryParse(command[1], out page) || page < 1)
                    page = 1;
            }
            
            OpenMarketGUI(player, category, page);
        }

        private static void OnPlayerDisconnected(CSteamID steamID)
        {
            playersWithMarketGUIOpen.Remove(steamID);
        }

        private static void OnButtonClicked(Player player, string buttonName)
        {
            UnturnedPlayer uPlayer = UnturnedPlayer.FromPlayer(player);

            if (!playersWithMarketGUIOpen.Contains(uPlayer.CSteamID))
                return;

            if (buttonName.StartsWith("BuyButton_"))
            {
                int index = int.Parse(buttonName.Split('_')[1]);
                var listings = MarketManager.GetListings("all", 1);
                if (index < listings.Count)
                {
                    var listing = listings[index];
                    BuyItem(uPlayer, listing.Id, 1);
                }
                return;
            }

            switch (buttonName)
            {
                case "CloseButton":
                    CloseMarketGUI(uPlayer);
                    break;
                case "PrevButton":
                    // Handle previous page
                    break;
                case "NextButton":
                    // Handle next page
                    break;
                case "CategoryAll":
                    UpdateMarketGUI(uPlayer, "all", 1);
                    break;
                case "CategoryWeapons":
                    UpdateMarketGUI(uPlayer, "weapons", 1);
                    break;
                case "CategoryAmmo":
                    UpdateMarketGUI(uPlayer, "ammo", 1);
                    break;
                case "CategoryMedical":
                    UpdateMarketGUI(uPlayer, "medical", 1);
                    break;
                case "CategoryFood":
                    UpdateMarketGUI(uPlayer, "food", 1);
                    break;
                case "CategoryClothing":
                    UpdateMarketGUI(uPlayer, "clothing", 1);
                    break;
                case "CategoryOther":
                    UpdateMarketGUI(uPlayer, "other", 1);
                    break;
            }
        }

        private static void OpenMarketGUI(UnturnedPlayer player, string category, int page)
        {
            if (playersWithMarketGUIOpen.Contains(player.CSteamID))
            {
                UnturnedChat.Say(player, "Market GUI is already open", Color.yellow);
                return;
            }

            try
            {
                EffectManager.sendUIEffect(MARKET_GUI_ID, MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true);
                playersWithMarketGUIOpen.Add(player.CSteamID);

                UpdateMarketGUI(player, category, page);

                UnturnedChat.Say(player, "Market GUI opened", Color.cyan);
                player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Default);
            }
            catch (System.Exception ex)
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

                UnturnedChat.Say(player, "Market GUI closed!", Color.red);
                player.Player.disablePluginWidgetFlag(EPluginWidgetFlags.Modal);
                player.Player.enablePluginWidgetFlag(EPluginWidgetFlags.Default);
            }
            catch (System.Exception ex)
            {
            }
        }

        private static void UpdateMarketGUI(UnturnedPlayer player, string category, int page)
        {
            try
            {
                var listings = MarketManager.GetListings(category, page);
                int totalPages = MarketManager.GetTotalPages(category);

                EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "PageInfo", $"{page}/{totalPages}");

                if (listings.Count == 0)
                {
                    EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NoItemsText", "No items currently listed in the market.");
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "NoItemsText", true);
                    EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, "ListingsPanel", false);
    
                    for (int i = 0; i < 6; i++)
                    {
                        EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ListingRow_{i}", false);
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
                            string condition = $"{(listing.Quality / 100.0 * 100):F0}%";
                            
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemID_{i}", listing.Id.ToString());
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemName_{i}", listing.ItemName);
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemPrice_{i}", $"${listing.Price:F2}");
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemSeller_{i}", listing.SellerName);
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemCondition_{i}", condition);
                            EffectManager.sendUIEffectText(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ItemQuantity_{i}", $"{listing.Quantity}x");
                            
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ListingRow_{i}", true);
                        }
                        else
                        {
                            EffectManager.sendUIEffectVisibility(MARKET_GUI_KEY, player.SteamPlayer().transportConnection, true, $"ListingRow_{i}", false);
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
            catch (System.Exception ex)
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

            Item item = new Item(listing.ItemId, quantity, listing.Quality, listing.State);
            
            bool itemGiven = player.Player.inventory.tryAddItem(item, true);

            if (!itemGiven)
            {
                UnturnedChat.Say(player, "Failed to give you the item! Your inventory is full.", Color.red);
                return;
            }

            listing.Quantity -= quantity;
            
            if (listing.Quantity <= 0)
            {
                MarketManager.RemoveListing(listingId);
            }

            player.Player.inventory.sendStorage();

            UnturnedChat.Say(player, $"Successfully purchased {quantity}x {listing.ItemName} for ${(listing.Price * quantity):F2}!", Color.green);
            
            UpdateMarketGUI(player, "all", 1);
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
                Provider.onServerDisconnected -= OnPlayerDisconnected;
                eventHandlersRegistered = false;
            }
        }
    }
}