using Rocket.API;

namespace URPUnturnov
{
    public class MainConfig : IRocketPluginConfiguration
    {
        // Database Config
        public string DatabaseServer { get; set; } = "192.168.1.248";
        public string DatabaseName { get; set; } = "s14_Plugin";
        public string DatabaseUsername { get; set; } = "u14_6eZ6R1dAS8";
        public string DatabasePassword { get; set; } = "jHmwfv7itFd+cFo5X!g014ly";
        public int DatabasePort { get; set; } = 3306;

        // Discord Webhook
        public string MainWebhookUrl { get; set; } = "https://discord.com/api/webhooks/1396610731876552705/wxVDm3IQE1sv5UDnoqHr8HDl3mdJfLRaBsn3LlKe81bX6wEok_VWdAlXuZwBI5lB-BoK";
        public int WebhookCharMax { get; set; } = 2000;
        public int WebhookSendInterval { get; set; } = 15000; // milliseconds
        
        // Discord Embed
        public string DiscordUsername { get; set; } = "URP-Unturnov";
        public string DiscordAvatarUrl { get; set; } = "https://imgur.com/a/6Me4Gy9";
        public int DiscordDecimalColor { get; set; } = 65280;

        public void LoadDefaults()
        {
        }
    }
}