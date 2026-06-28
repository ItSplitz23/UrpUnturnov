using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Timers;
using Newtonsoft.Json;
using Rocket.Core.Logging;

namespace UrpUnturnov.Logging
{




    public class WebhookLogger
    {
        private readonly string webhookUrl;
        private readonly string username;
        private readonly string avatarUrl;
        private readonly List<WebhookMessage> messageQueue;
        private readonly Timer sendTimer;
        private readonly object queueLock = new object();

        public bool Enabled { get; set; }
        public int SendInterval { get; set; }

        public WebhookLogger(WebhookConfig config)
        {
            webhookUrl = config.WebhookUrl;
            username = config.Username;
            avatarUrl = config.AvatarUrl;
            Enabled = config.Enabled;
            SendInterval = config.SendInterval;

            messageQueue = new List<WebhookMessage>();

            if (Enabled && SendInterval > 0)
            {
                sendTimer = new Timer(SendInterval);
                sendTimer.Elapsed += OnTimerElapsed;
                sendTimer.AutoReset = true;
                sendTimer.Start();
            }
        }

        public void LogMessage(string title, Dictionary<string, string> fields, WebhookLogLevel level = WebhookLogLevel.Info)
        {
            if (!Enabled) return;

            lock (queueLock)
            {
                messageQueue.Add(new WebhookMessage
                {
                    Title = title,
                    Fields = fields,
                    Level = level,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public void LogMessageImmediate(string title, Dictionary<string, string> fields, WebhookLogLevel level = WebhookLogLevel.Info)
        {
            if (!Enabled) return;

            var msg = new WebhookMessage
            {
                Title = title,
                Fields = fields,
                Level = level,
                Timestamp = DateTime.UtcNow
            };

            SendSingleMessage(msg);
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            ProcessQueue();
        }

        private void ProcessQueue()
        {
            List<WebhookMessage> messagesToSend;

            lock (queueLock)
            {
                if (messageQueue.Count == 0) return;

                messagesToSend = new List<WebhookMessage>(messageQueue);
                messageQueue.Clear();
            }

            foreach (var msg in messagesToSend)
            {
                SendSingleMessage(msg);
            }
        }

        private void SendSingleMessage(WebhookMessage message)
        {
            try
            {
                WebRequest request = WebRequest.Create(webhookUrl);
                request.ContentType = "application/json";
                request.Method = "POST";

                var embedFields = new List<object>();
                foreach (var field in message.Fields)
                {
                    embedFields.Add(new
                    {
                        name = field.Key,
                        value = field.Value?.Length > 1024 ? field.Value.Substring(0, 1021) + "..." : field.Value,
                        inline = true
                    });
                }

                using (var sw = new StreamWriter(request.GetRequestStream()))
                {
                    string json = JsonConvert.SerializeObject(new
                    {
                        username = username,
                        avatar_url = avatarUrl,
                        embeds = new[]
                        {
                            new
                            {
                                title = message.Title,
                                color = GetLevelColor(message.Level),
                                fields = embedFields.ToArray(),
                                footer = new
                                {
                                    text = username
                                },
                                timestamp = message.Timestamp.ToString("o")
                            }
                        }
                    });

                    sw.Write(json);
                }

                using (var response = request.GetResponse())
                {
                }
            }
            catch (WebException ex)
            {
                Logger.LogError($"WebhookLogger: Failed to send webhook: {ex.Message}");

                if (ex.Response is HttpWebResponse response)
                {
                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        Logger.LogWarning("WebhookLogger: Rate limited. Consider increasing SendInterval.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"WebhookLogger: {ex.Message}");
            }
        }

        private int GetLevelColor(WebhookLogLevel level)
        {
            switch (level)
            {
                case WebhookLogLevel.Success:
                    return 3066993;
                case WebhookLogLevel.Error:
                    return 15158332;
                case WebhookLogLevel.Warning:
                    return 16776960;
                case WebhookLogLevel.Info:
                    return 3447003;
                case WebhookLogLevel.Debug:
                    return 9807270;
                default:
                    return 3447003;
            }
        }

        public void Shutdown()
        {
            if (sendTimer != null)
            {
                sendTimer.Stop();
                sendTimer.Dispose();
            }

            ProcessQueue();
        }
    }

    public class WebhookMessage
    {
        public string Title { get; set; }
        public Dictionary<string, string> Fields { get; set; }
        public WebhookLogLevel Level { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum WebhookLogLevel
    {
        Info,
        Warning,
        Error,
        Success,
        Debug
    }

    public class WebhookConfig
    {
        public bool Enabled { get; set; } = true;
        public string WebhookUrl { get; set; }
        public string Username { get; set; } = "URP Flea Market";
        public string AvatarUrl { get; set; } = "https://i.imgur.com/6Me4Gy9.png";
        public int SendInterval { get; set; } = 15000;
    }
}
