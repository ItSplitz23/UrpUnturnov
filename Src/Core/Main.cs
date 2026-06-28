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
using UrpUnturnov.Logging;
using UrpUnturnov.Systems;

namespace URPUnturnov
{
    public class MainClass : RocketPlugin<MainConfig>
    {
        public static MainClass Instance { get; private set; }


        public static WebhookLogger LoggingCreated { get; private set; }

        public static WebhookLogger LoggingPurchased { get; private set; }

        public static WebhookLogger LoggingExpired { get; private set; }

        public static WebhookLogger LoggingClaimed { get; private set; }

        public static WebhookLogger LoggingError { get; private set; }

        private static readonly List<string> mainWebhookData = new List<string>();
        private static Timer TimerSendMainWebhookData;
        private static Timer TimerExpiryCheck;

        public GuiManager GuiManager { get; private set; }

        protected override void Load()
        {
            Instance = this;
            try
            {
                Logger.Log("Loading URP-FleaMarket");
                UrpUnturnov.Commands.ListingCommand.RegisterEventHandlers();

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

                var cfg = Configuration.Instance;
                var defaultLogUrl = string.IsNullOrWhiteSpace(cfg.LoggingWebhookUrl) ? cfg.MainWebhookUrl : cfg.LoggingWebhookUrl;
                var interval = cfg.LoggingWebhookSendInterval > 0 ? cfg.LoggingWebhookSendInterval : 15000;
                var username = cfg.LoggingWebhookUsername ?? "URP Flea Market Logs";
                var avatar = cfg.LoggingWebhookAvatarUrl ?? "https://i.imgur.com/6Me4Gy9.png";

                LoggingCreated = CreateLogger(cfg.LoggingWebhookEnabled, cfg.WebhookUrlCreated ?? "", defaultLogUrl, username, avatar, interval);
                LoggingPurchased = CreateLogger(cfg.LoggingWebhookEnabled, cfg.WebhookUrlPurchased ?? "", defaultLogUrl, username, avatar, interval);
                LoggingExpired = CreateLogger(cfg.LoggingWebhookEnabled, cfg.WebhookUrlExpired ?? "", defaultLogUrl, username, avatar, interval);
                LoggingClaimed = CreateLogger(cfg.LoggingWebhookEnabled, cfg.WebhookUrlClaimed ?? "", defaultLogUrl, username, avatar, interval);
                LoggingError = CreateLogger(cfg.LoggingWebhookEnabled, cfg.WebhookUrlError ?? "", defaultLogUrl, username, avatar, interval);

                TimerExpiryCheck = new Timer(60000);
                TimerExpiryCheck.Elapsed += (s, e) => CheckAndLogExpiredListings();
                TimerExpiryCheck.AutoReset = true;
                TimerExpiryCheck.Start();

                TimerSendMainWebhookData = new Timer();
                TimerSendMainWebhookData.Elapsed += (sender, e) => WebhookSendData(mainWebhookData, Configuration.Instance.MainWebhookUrl);
                TimerSendMainWebhookData.Interval = Configuration.Instance.WebhookSendInterval;
                TimerSendMainWebhookData.Enabled = true;

                Logger.Log("URP-FleaMarket has been loaded");
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
                TimerExpiryCheck?.Stop();
                TimerExpiryCheck?.Dispose();
                LoggingCreated?.Shutdown();
                LoggingPurchased?.Shutdown();
                LoggingExpired?.Shutdown();
                LoggingClaimed?.Shutdown();
                LoggingError?.Shutdown();
                GuiManager?.Shutdown();

                UrpUnturnov.Commands.ListingCommand.CloseAllMarketGUIs();
                UrpUnturnov.Commands.ListingCommand.UnregisterEventHandlers();

                Logger.Log("URP-FleaMarket has been unloaded");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error while unloading: {ex}");
            }
        }

        private static WebhookLogger CreateLogger(bool enabled, string url, string defaultUrl, string username, string avatar, int interval)
        {
            var u = string.IsNullOrWhiteSpace(url) ? defaultUrl : url;
            if (!enabled || string.IsNullOrWhiteSpace(u)) return null;
            return new WebhookLogger(new WebhookConfig { Enabled = true, WebhookUrl = u, Username = username, AvatarUrl = avatar, SendInterval = interval });
        }

        private static void CheckAndLogExpiredListings()
        {
            try
            {
                var expired = MarketManager.GetListingsThatJustExpired();
                if (expired == null || expired.Count == 0) return;
                foreach (var listing in expired)
                {
                    var fields = new Dictionary<string, string>
                    {
                        { "Listing ID", listing.Id.ToString() },
                        { "Seller", listing.SellerName ?? "" },
                        { "Seller SteamID", listing.SellerId.ToString() },
                        { "Item", listing.ItemName ?? "" },
                        { "Item ID", listing.ItemId.ToString() },
                        { "Quantity", listing.Quantity.ToString() },
                        { "Price", listing.Price.ToString("N0") }
                    };
                    LoggingExpired?.LogMessage("Listing expired", fields, WebhookLogLevel.Warning);
                    MarketManager.MarkExpiryLogged(listing.Id);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Expiry check: {ex.Message}");
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