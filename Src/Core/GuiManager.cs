using Rocket.API;
using Rocket.Core.Logging;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace URPUnturnov
{
    public class GuiManager
    {
        private MainClass plugin;

        public GuiManager(MainClass pluginInstance)
        {
            plugin = pluginInstance;
        }

        public void Initialize()
        {
        }

        public void Shutdown()
        {
        }
    }
}