using Rocket.Unturned.Player;
using Rocket.Unturned.Events;
using SDG.Unturned;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;

namespace UrpUnturnov.Systems
{
    public static class ClothingProtectionSystem
    {
        private static Dictionary<CSteamID, ClothingData> storedShirts = new Dictionary<CSteamID, ClothingData>();
        private static Dictionary<CSteamID, ClothingData> storedPants = new Dictionary<CSteamID, ClothingData>();

        private struct ClothingData
        {
            public ushort id;
            public byte quality;
            public byte[] state;
        }

        public static void Initialize()
        {
            PlayerLife.onPlayerDied += OnPlayerDied;
            UnturnedPlayerEvents.OnPlayerRevive += OnPlayerRevive;
        }

        public static void Shutdown()
        {
            PlayerLife.onPlayerDied -= OnPlayerDied;
            UnturnedPlayerEvents.OnPlayerRevive -= OnPlayerRevive;
        }

        private static void OnPlayerDied(PlayerLife sender, EDeathCause cause, ELimb limb, CSteamID instigator)
        {
            if (sender?.player?.clothing == null) return;

            UnturnedPlayer player = UnturnedPlayer.FromPlayer(sender.player);
            if (player == null) return;

            PlayerClothing clothing = sender.player.clothing;
            CSteamID steamId = sender.player.channel.owner.playerID.steamID;
            
            ushort shirtId = clothing.shirt;
            byte shirtQuality = clothing.shirtQuality;
            byte[] shirtState = clothing.shirtState;
            
            ushort pantsId = clothing.pants;
            byte pantsQuality = clothing.pantsQuality;
            byte[] pantsState = clothing.pantsState;

            if (shirtId != 0)
            {
                storedShirts[steamId] = new ClothingData { id = shirtId, quality = shirtQuality, state = shirtState };
            }
            
            if (pantsId != 0)
            {
                storedPants[steamId] = new ClothingData { id = pantsId, quality = pantsQuality, state = pantsState };
            }

            DropClothingContents(sender.player, clothing);
        }

        private static void OnPlayerRevive(UnturnedPlayer player, Vector3 position, byte angle)
        {
            if (player?.Player?.clothing == null) return;

            CSteamID steamId = player.CSteamID;
            PlayerClothing clothing = player.Player.clothing;
            
            if (storedShirts.ContainsKey(steamId))
            {
                var shirtData = storedShirts[steamId];
                clothing.askWearShirt(shirtData.id, shirtData.quality, shirtData.state, true);
                storedShirts.Remove(steamId);
            }
            
            if (storedPants.ContainsKey(steamId))
            {
                var pantsData = storedPants[steamId];
                clothing.askWearPants(pantsData.id, pantsData.quality, pantsData.state, true);
                storedPants.Remove(steamId);
            }
        }

        private static void DropClothingContents(Player player, PlayerClothing clothing)
        {
            Vector3 dropPosition = player.transform.position + Vector3.up * 0.5f;

            Items shirtItems = player.inventory.items[2];
            if (shirtItems?.items != null)
            {
                List<ItemJar> itemsToRemove = new List<ItemJar>();
                foreach (ItemJar item in shirtItems.items)
                {
                    if (item != null)
                    {
                        itemsToRemove.Add(item);
                        ItemManager.dropItem(new Item(item.item.id, item.item.amount, item.item.quality, item.item.state), 
                                           dropPosition, false, false, false);
                    }
                }
                
                foreach (ItemJar item in itemsToRemove)
                {
                    shirtItems.items.Remove(item);
                }
            }

            Items pantsItems = player.inventory.items[3];
            if (pantsItems?.items != null)
            {
                List<ItemJar> itemsToRemove = new List<ItemJar>();
                foreach (ItemJar item in pantsItems.items)
                {
                    if (item != null)
                    {
                        itemsToRemove.Add(item);
                        ItemManager.dropItem(new Item(item.item.id, item.item.amount, item.item.quality, item.item.state), 
                                           dropPosition, false, false, false);
                    }
                }
                
                foreach (ItemJar item in itemsToRemove)
                {
                    pantsItems.items.Remove(item);
                }
            }
        }
    }
}