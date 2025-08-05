using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System.Collections.Generic;
using UrpUnturnov.Systems;

namespace UrpUnturnov.Commands
{
    public class BuyCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "buy";
        public string Help => "Buy item from flea market";
        public string Syntax => "/buy <listing_id> [quantity]";
        public List<string> Aliases => new List<string> { "purchase", "buyitem" };
        public List<string> Permissions => new List<string> { "fleamarket.buy" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            if (command.Length < 1)
            {
                UnturnedChat.Say(player, "Usage: /buy <listing_id> [quantity]", UnityEngine.Color.red);
                UnturnedChat.Say(player, "Use /listing to see available items", UnityEngine.Color.yellow);
                return;
            }

            if (!int.TryParse(command[0], out int listingId))
            {
                UnturnedChat.Say(player, "Invalid listing ID! Please enter a number.", UnityEngine.Color.red);
                return;
            }

            byte quantityToBuy = 1;
            if (command.Length > 1)
            {
                if (!byte.TryParse(command[1], out quantityToBuy) || quantityToBuy <= 0)
                {
                    UnturnedChat.Say(player, "Invalid quantity! Enter a number between 1-255", UnityEngine.Color.red);
                    return;
                }
            }

            var listing = MarketManager.GetListing(listingId);
            if (listing == null)
            {
                UnturnedChat.Say(player, $"Listing ID {listingId} not found!", UnityEngine.Color.red);
                return;
            }

            if (quantityToBuy > listing.Quantity)
            {
                UnturnedChat.Say(player, $"Only {listing.Quantity} available! You tried to buy {quantityToBuy}", UnityEngine.Color.red);
                return;
            }

            Item item = new Item(listing.ItemId, quantityToBuy, listing.Quality, listing.State);
            
            bool itemGiven = player.Player.inventory.tryAddItem(item, true);

            if (!itemGiven)
            {
                UnturnedChat.Say(player, "Failed to give you the item! Your inventory is full.", UnityEngine.Color.red);
                return;
            }

            listing.Quantity -= quantityToBuy;
            
            if (listing.Quantity <= 0)
            {
                MarketManager.RemoveListing(listingId);
            }

            player.Player.inventory.sendStorage();

            UnturnedChat.Say(player, $"Successfully purchased {quantityToBuy}x {listing.ItemName} for ${(listing.Price * quantityToBuy):F2}!", UnityEngine.Color.green);
            UnturnedChat.Say(player, $"Item has been added to your inventory.", UnityEngine.Color.cyan);
            
            if (listing.Quantity > 0)
            {
                UnturnedChat.Say(player, $"{listing.Quantity} still available in listing {listingId}", UnityEngine.Color.yellow);
            }
        }
    }
}