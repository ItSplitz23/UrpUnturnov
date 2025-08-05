using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UrpUnturnov.Systems;

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
            
            DisplayListings(player, category, page);
        }
        
        private void DisplayListings(UnturnedPlayer player, string category, int page)
        {
            var listings = MarketManager.GetListings(category, page);
            int totalPages = MarketManager.GetTotalPages(category);

            UnturnedChat.Say(player, $"=== FLEA MARKET - {category.ToUpper()} (Page {page}/{totalPages}) ===", UnityEngine.Color.yellow);
            
            if (listings.Count == 0)
            {
                UnturnedChat.Say(player, "No items currently listed in the market.", UnityEngine.Color.gray);
            }
            else
            {
                UnturnedChat.Say(player, "ID | Item Name | Price | Seller | Condition | Qty", UnityEngine.Color.gray);
                UnturnedChat.Say(player, "---------------------------------------------------", UnityEngine.Color.gray);
                
                foreach (var listing in listings)
                {
                    string condition = $"{(listing.Quality / 100.0 * 100):F0}%";
                    UnturnedChat.Say(player, $"{listing.Id:D3} | {listing.ItemName} | ${listing.Price:F2} | {listing.SellerName} | {condition} | {listing.Quantity}x", UnityEngine.Color.white);
                }
            }
            
            UnturnedChat.Say(player, "---------------------------------------------------", UnityEngine.Color.gray);
            UnturnedChat.Say(player, "Use /buy <id> to purchase an item", UnityEngine.Color.cyan);
            UnturnedChat.Say(player, "Categories: all, weapons, ammo, medical, food, clothing, other", UnityEngine.Color.cyan);
        }
    }
}