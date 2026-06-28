using Rocket.API;
using Rocket.Unturned.Player;
using System.Collections.Generic;

namespace UrpUnturnov.Commands
{
    public class SellCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "sell";
        public string Help => "Open sell UI to list items on flea market";
        public string Syntax => "/sell";
        public List<string> Aliases => new List<string> { "sellitem" };
        public List<string> Permissions => new List<string> { "fleamarket.sell" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            ListingCommand.OpenMarketGUIWithModal(player);
        }
    }
}