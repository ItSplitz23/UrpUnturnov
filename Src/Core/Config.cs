using System.Collections.Generic;
using System.Xml.Serialization;
using Rocket.API;

namespace URPUnturnov
{
    public class ExpiryTier
    {

        [XmlAttribute("GroupId")]
        public string GroupId { get; set; } = "";


        [XmlAttribute("PermissionSuffix")]
        public string PermissionSuffix { get; set; } = "";

        [XmlAttribute("Days")]
        public int Days { get; set; } = 1;


        [XmlIgnore]
        public string EffectiveGroupId => !string.IsNullOrEmpty(GroupId) ? GroupId : PermissionSuffix;
    }

    public class MainConfig : IRocketPluginConfiguration
    {

        public string DatabaseServer { get; set; } = "192.168.1.248";
        public string DatabaseName { get; set; } = "s14_Plugin";
        public string DatabaseUsername { get; set; } = "u14_6eZ6R1dAS8";
        public string DatabasePassword { get; set; } = "jHmwfv7itFd+cFo5X!g014ly";
        public int DatabasePort { get; set; } = 3306;


        public string MainWebhookUrl { get; set; } = "https://discord.com/api/webhooks/1396610731876552705/wxVDm3IQE1sv5UDnoqHr8HDl3mdJfLRaBsn3LlKe81bX6wEok_VWdAlXuZwBI5lB-BoK";
        public int WebhookCharMax { get; set; } = 2000;
        public int WebhookSendInterval { get; set; } = 15000; 


        public string DiscordUsername { get; set; } = "URP-Unturnov";
        public string DiscordAvatarUrl { get; set; } = "https://imgur.com/a/6Me4Gy9";
        public int DiscordDecimalColor { get; set; } = 65280;


        public bool LoggingWebhookEnabled { get; set; } = true;
        public string LoggingWebhookUrl { get; set; } = "";
        public string LoggingWebhookUsername { get; set; } = "URP Flea Market Logs";
        public string LoggingWebhookAvatarUrl { get; set; } = "https://i.imgur.com/6Me4Gy9.png";
        public int LoggingWebhookSendInterval { get; set; } = 15000;
        public string WebhookUrlCreated { get; set; } = "";
        public string WebhookUrlPurchased { get; set; } = "";
        public string WebhookUrlExpired { get; set; } = "";
        public string WebhookUrlClaimed { get; set; } = "";
        public string WebhookUrlError { get; set; } = "";


        public decimal MarketFeePercent { get; set; } = 5m;


        public int ListingExpiryDaysDefault { get; set; } = 1;

        [XmlArray("ExpiryTiers"), XmlArrayItem("ExpiryTier")]
        public List<ExpiryTier> ExpiryTiers { get; set; } = new List<ExpiryTier>
        {
            new ExpiryTier { GroupId = "default", Days = 1 },
            new ExpiryTier { GroupId = "vip", Days = 2 }
        };

        public void LoadDefaults()
        {
            if (ExpiryTiers == null || ExpiryTiers.Count == 0)
            {
                ExpiryTiers = new List<ExpiryTier>
                {
                    new ExpiryTier { GroupId = "default", Days = 1 },
                    new ExpiryTier { GroupId = "vip", Days = 2 }
                };
            }
        }
    }
}