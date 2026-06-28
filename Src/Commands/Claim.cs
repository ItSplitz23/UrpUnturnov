using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System.Collections.Generic;
using UrpUnturnov.Data;
using UrpUnturnov.Logging;
using UrpUnturnov.Systems;
using UnityEngine;
using URPUnturnov;

namespace UrpUnturnov.Commands
{
    public class ClaimCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "claim";
        public string Help => "Claim unsold items from your expired flea market listings";
        public string Syntax => "/claim";
        public List<string> Aliases => new List<string> { "claimlistings", "claimexpired" };
        public List<string> Permissions => new List<string> { "fleamarket.claim" };

        public void Execute(Rocket.API.IRocketPlayer caller, string[] command)
        {
            var player = (UnturnedPlayer)caller;
            var claimable = MarketManager.GetClaimableListings(player.CSteamID.m_SteamID);

            if (claimable == null || claimable.Count == 0)
            {
                UnturnedChat.Say(player, "You have no expired listings to claim.", Color.yellow);
                return;
            }

            int totalClaimed = 0;
            int listingsFullyClaimed = 0;
            int listingsPartial = 0;

            foreach (var listing in claimable)
            {
                int toGive = listing.Quantity;
                int given = 0;

                for (int i = 0; i < toGive; i++)
                {
                    var item = new Item(listing.ItemId, 1, listing.Quality, listing.State);
                    if (player.Player.inventory.tryAddItem(item, true))
                        given++;
                    else
                        break;
                }

                if (given > 0)
                {
                    totalClaimed += given;
                    if (given >= toGive)
                    {
                        MarketManager.MarkListingClaimed(listing.Id);
                        listingsFullyClaimed++;
                    }
                    else
                    {
                        MarketManager.UpdateListingQuantityAfterClaim(listing.Id, toGive - given);
                        listingsPartial++;
                    }
                }
            }

            player.Player.inventory.sendStorage();

            if (totalClaimed > 0 || listingsFullyClaimed > 0)
            {
                MainClass.LoggingClaimed?.LogMessage("Expired listings claimed", new Dictionary<string, string>
                {
                    { "Seller", player.DisplayName ?? player.CSteamID.ToString() },
                    { "Seller SteamID", player.CSteamID.m_SteamID.ToString() },
                    { "Listings claimed", listingsFullyClaimed.ToString() },
                    { "Total items returned", totalClaimed.ToString() }
                }, WebhookLogLevel.Info);
            }

            if (listingsPartial > 0)
                UnturnedChat.Say(player, $"Claimed items from {listingsFullyClaimed} expired listing(s). {listingsPartial} listing(s) still have items left (inventory was full). Run /claim again when you have space.", Color.yellow);
            else if (totalClaimed > 0)
                UnturnedChat.Say(player, $"Claimed {totalClaimed} item(s) from {listingsFullyClaimed} expired listing(s).", Color.green);
            else
                UnturnedChat.Say(player, "No items could be added (inventory full). Free space and run /claim again.", Color.red);
        }
    }
}
