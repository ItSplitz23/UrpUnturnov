using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System.Collections.Generic;
using UrpUnturnov.Data;
using UrpUnturnov.Systems;

namespace UrpUnturnov.Commands
{
    public class SellCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "sell";
        public string Help => "Sell item to flea market";
        public string Syntax => "/sell <price> [quantity]";
        public List<string> Aliases => new List<string> { "sellitem" };
        public List<string> Permissions => new List<string> { "fleamarket.sell" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            if (command.Length < 1)
            {
                UnturnedChat.Say(player, "Usage: /sell <price> [quantity]", UnityEngine.Color.red);
                UnturnedChat.Say(player, "Hold the item you want to sell", UnityEngine.Color.yellow);
                return;
            }

            if (!decimal.TryParse(command[0], out decimal price) || price <= 0)
            {
                UnturnedChat.Say(player, "Invalid price! Enter a number greater than 0", UnityEngine.Color.red);
                return;
            }

            byte quantity = 1;
            if (command.Length > 1)
            {
                if (!byte.TryParse(command[1], out quantity) || quantity <= 0)
                {
                    UnturnedChat.Say(player, "Invalid quantity! Enter a number between 1-255", UnityEngine.Color.red);
                    return;
                }
            }

            PlayerEquipment equipment = player.Player.equipment;
            if (equipment.itemID == 0)
            {
                UnturnedChat.Say(player, "You must be holding an item to sell it!", UnityEngine.Color.red);
                return;
            }

            ushort itemId = equipment.itemID;
            byte itemQuality = equipment.quality;
            byte[] itemState = equipment.state;

            ItemAsset itemAsset = Assets.find(EAssetType.ITEM, equipment.itemID) as ItemAsset;
            string itemName = itemAsset?.itemName ?? "Unknown Item";

            byte totalAvailable = 0;
            ushort targetItemId = equipment.itemID;
            
            Items[] inventoryPages = player.Player.inventory.items;
            if (inventoryPages != null)
            {
                for (byte pageIndex = 0; pageIndex < inventoryPages.Length; pageIndex++)
                {
                    Items currentPage = inventoryPages[pageIndex];
                    if (currentPage != null && currentPage.items != null)
                    {
                        for (int itemIndex = 0; itemIndex < currentPage.items.Count; itemIndex++)
                        {
                            ItemJar inventoryItem = currentPage.items[itemIndex];
                            if (inventoryItem != null && inventoryItem.item != null && inventoryItem.item.id == targetItemId)
                            {
                                totalAvailable += inventoryItem.item.amount;
                            }
                        }
                    }
                }
            }

            if (totalAvailable == 0)
            {
                UnturnedChat.Say(player, "No items found in inventory!", UnityEngine.Color.red);
                return;
            }

            if (quantity > totalAvailable)
            {
                UnturnedChat.Say(player, $"You only have {totalAvailable} of this item!", UnityEngine.Color.red);
                return;
            }

            var listing = new MarketListing
            {
                ItemId = equipment.itemID,
                ItemName = itemName,
                Price = price,
                SellerName = player.DisplayName,
                SellerId = player.CSteamID.m_SteamID,
                Quality = equipment.quality,
                State = equipment.state,
                Quantity = quantity
            };

            int listingId = MarketManager.AddListing(listing);

            byte itemsToRemove = quantity;
            
            player.Player.equipment.dequip();
            
            Items[] allPages = player.Player.inventory.items;
            if (allPages != null)
            {
                for (byte page = 0; page < allPages.Length && itemsToRemove > 0; page++)
                {
                    Items pageData = allPages[page];
                    if (pageData != null && pageData.items != null)
                    {
                        for (byte index = 0; index < pageData.items.Count && itemsToRemove > 0; index++)
                        {
                            ItemJar item = pageData.items[index];
                            if (item != null && item.item != null && item.item.id == itemId)
                            {
                                byte itemAmount = item.item.amount;
                                if (itemAmount <= itemsToRemove)
                                {
                                    player.Player.inventory.removeItem(page, index);
                                    itemsToRemove -= itemAmount;
                                    index--;
                                }
                                else
                                {
                                    player.Player.inventory.removeItem(page, index);
                                    itemsToRemove = 0;
                                }
                            }
                        }
                    }
                }
            }

            player.Player.inventory.sendStorage();

            UnturnedChat.Say(player, $"Listed {quantity}x {itemName} for ${price:F2} each (ID: {listingId})", UnityEngine.Color.green);
            UnturnedChat.Say(player, "Item has been removed from your inventory and added to the flea market!", UnityEngine.Color.cyan);
        }
    }
}