using Newtonsoft.Json;
using Rocket.API;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Timers;
using UnityEngine;
using static Rocket.Unturned.Events.UnturnedEvents;
using Logger = Rocket.Core.Logging.Logger;
using UrpUnturnov.Systems;

namespace URPUnturnov
{
    public class MainClass : RocketPlugin<MainConfig>
    {
        public static MainClass Instance { get; private set; }

        private static readonly List<string> mainWebhookData = new List<string>();
        private static Timer TimerSendMainWebhookData;
        
        public GuiManager GuiManager { get; private set; }

        protected override void Load()
        {
            Instance = this;
            try
            {
                Logger.Log("Loading URP-FleaMarket");

                try
                {
                    DatabaseManager.Initialize(
                        Configuration.Instance.DatabaseServer,
                        Configuration.Instance.DatabaseName,
                        Configuration.Instance.DatabaseUsername,
                        Configuration.Instance.DatabasePassword,
                        Configuration.Instance.DatabasePort
                    );
                    Logger.Log("Database initialized successfully");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to initialize Database Manager: {ex}");
                    return;
                }

                GuiManager = new GuiManager(this);
                GuiManager.Initialize();

                TimerSendMainWebhookData = new Timer();
                TimerSendMainWebhookData.Elapsed += (sender, e) => WebhookSendData(mainWebhookData, Configuration.Instance.MainWebhookUrl);
                TimerSendMainWebhookData.Interval = Configuration.Instance.WebhookSendInterval;
                TimerSendMainWebhookData.Enabled = true;

                Logger.Log("URP-FleaMarket has been loaded");
                SendMainWebhook("`URP-FleaMarket` has started");
            }
            catch (Exception ex)
            {
                Logger.LogError($"An error occurred while loading: {ex}");
            }
        }

        protected override void Unload()
        {
            try
            {
                TimerSendMainWebhookData?.Stop();
                GuiManager?.Shutdown();

                Logger.Log("URP-FleaMarket has been unloaded");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error while unloading: {ex}");
            }
        }

        public static void SendMainWebhook(string msgToSend)
        {
            mainWebhookData.Add(msgToSend);
        }

        private static void WebhookSendData(List<string> data, string webhookUrl)
        {
            if (data.Count != 0)
            {
                string totalString = "";
                foreach (string item in data)
                {
                    if ((totalString.Length + item.Length) < Instance.Configuration.Instance.WebhookCharMax)
                    {
                        totalString += "\n" + item;
                    }
                    else
                    {
                        InternalSendWebhook(totalString, webhookUrl);
                        totalString = item;
                    }
                }

                InternalSendWebhook(totalString, webhookUrl);
                data.Clear();
            }
        }

        private static void InternalSendWebhook(string msgToSend, string webhookUrl)
        {
            var config = Instance.Configuration.Instance;
            
            WebRequest adWebRequest = WebRequest.Create(webhookUrl);
            adWebRequest.ContentType = "application/json";
            adWebRequest.Method = "POST";
            
            using (var sw = new StreamWriter(adWebRequest.GetRequestStream()))
            {
                string json = JsonConvert.SerializeObject(new
                {
                    embeds = new[]
                    {
                        new
                        {
                            description = msgToSend,
                            color = config.DiscordDecimalColor,
                        }
                    },
                    username = config.DiscordUsername,
                    avatar_url = config.DiscordAvatarUrl,
                });
                sw.Write(json);
            }

            var response = adWebRequest.GetResponse();
        }
    }
}