﻿using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System.Linq;
using ChatTwo.Code;
using System;
using System.Timers;

namespace TidyChat

{
    public sealed class TidyChat : IDalamudPlugin
    {
        public string Name => "Tidy Chat";

        private const string SettingsCommand = "/tidychat";
        private const string ShorthandCommand = "/tidy";

        private DalamudPluginInterface PluginInterface { get; init; }
        private ChatGui ChatGui { get; init; }
        private CommandManager CommandManager { get; init; }
        private Configuration Configuration { get; init; }
        private PluginUI PluginUi { get; init; }

        // Lifted below lines (27-39) from Anna's Chat2
        private const ushort Clear7 = ~(~0 << 7);
        internal ushort Raw { get; }
        internal ChatType Type => (ChatType)(this.Raw & Clear7);

        private static ChatType FromCode(ushort code)
        {
            return (ChatType)(code & Clear7);
        }

        private static ChatType FromDalamud(XivChatType type)
        {
            return FromCode((ushort)type);
        }

        public TidyChat(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] ChatGui chatGui,
            [RequiredVersion("1.0")] CommandManager commandManager)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.ChatGui = chatGui;

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            ChatGui.ChatMessage += OnChat;

            this.PluginUi = new PluginUI(this.Configuration);

            this.CommandManager.AddHandler(SettingsCommand, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open settings"
            });

            this.CommandManager.AddHandler(ShorthandCommand, new CommandInfo(OnCommand)
            {
                HelpMessage = "Shorthand command to open settings"
            });

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        }

        public int numberOfCommendations = 0;
        public string lastDuty = "";

        public void Dispose()
        {
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(SettingsCommand);
            this.CommandManager.RemoveHandler(ShorthandCommand);
            this.ChatGui.ChatMessage -= this.OnChat;
        }

        private void OnChat(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            // Lifted below line from Anna's chat2
            var chatType = FromDalamud(type);

            // Make everything lowercase so I don't have to think about which words are capitalized in the message
            string normalizedText = message.TextValue.ToLower();

            if (!Configuration.Enabled)
            {
                return;
            }

            if (Configuration.BetterInstanceMessage && ChatStrings.InstancedArea.All(normalizedText.Contains) && chatType is ChatType.System)
            {
                // The last character in the first sentence is the instanceNumber so
                // we capture it by finding the period that ends the first sentence and going back one character
                int index = message.TextValue.IndexOf('.');
                string instanceNumber = message.TextValue.Substring(index - 1, 1);
                message = $"You are now in instance: {instanceNumber}";
            }

            if (Configuration.BetterSayReminder && ChatStrings.SayQuestReminder.All(normalizedText.Contains) && !Configuration.HideQuestReminder && chatType is ChatType.System)
            {
                // With the chat mode in Say, enter a phrase containing "Capture this"

                int containingPhraseStart = message.TextValue.IndexOf('“');
                int containingPhraseEnd = message.TextValue.LastIndexOf('”');
                int lengthOfPhrase = containingPhraseEnd - containingPhraseStart; 
                string containingPhrase = message.TextValue.Substring(containingPhraseStart+1, lengthOfPhrase-1);
                message = $"/say {containingPhrase}";
            }

            if (Configuration.BetterCommendationMessage && ChatStrings.PlayerCommendation.All(normalizedText.Contains))
            {

                isHandled = true;
                numberOfCommendations++;

                // Give it a few seconds before sending the /debug message with the total number of commendations in case there is any lag between commendation messages
                // There shouldn't be any lag since I think they all get sent at once - but having this small wait guarantees that there won't be any problems
                if (numberOfCommendations == 1)
                {
                    var t = new System.Timers.Timer();
                    t.Interval = 5000;
                    t.AutoReset = false;
                    t.Elapsed += delegate
                    {
                            string commendations = $"commendation{(numberOfCommendations == 1 ? "" : "s")}";
                            string dutyName = $"{(Configuration.IncludeDutyNameInComms && lastDuty.Length > 0 ? " from completing " + lastDuty + "." : ".")}";
                            ChatGui.Print($"You received {numberOfCommendations} {commendations}{dutyName}");
                            t.Enabled = false;
                            t.Dispose();
                        numberOfCommendations = 0;
                    };
                    t.Enabled = true;
                }
            }

            if (Configuration.BetterCommendationMessage && ChatStrings.DutyEnded.All(normalizedText.Contains))
            {
                //      match here then go back 4 characters to capture everything before " has"
                //           |
                //           v
                // <duty> has ended.
                lastDuty = message.TextValue.Substring(0, message.TextValue.LastIndexOf(" ") - 4);
            }

            if (Configuration.BetterCommendationMessage && ChatStrings.GuildhestEnded.All(normalizedText.Contains))
            {
                lastDuty = "a Guildhest";
            }

            if (chatType is ChatType.StandardEmote && Configuration.FilterEmoteSpam || Configuration.HideUsedEmotes)
            {
                isHandled = FilterEmoteMessages.IsFiltered(normalizedText, chatType, Configuration);
            }

            if (chatType is ChatType.CustomEmote)
            {
                isHandled = FilterEmoteMessages.IsFiltered(normalizedText, chatType, Configuration);
            }

            if (chatType is ChatType.System && Configuration.FilterSystemMessages)
            {
                isHandled = FilterSystemMessages.IsFiltered(normalizedText, Configuration);
            }

            if (chatType is ChatType.LootNotice && Configuration.FilterObtainedSpam)
            {
                isHandled = FilterObtainMessages.IsFiltered(normalizedText, Configuration);
            }

            if (chatType is ChatType.LootRoll && Configuration.FilterLootSpam)
            {
                isHandled = FilterLootMessages.IsFiltered(normalizedText, Configuration);
            }

        }

        private void OnCommand(string command, string args)
        {
            this.PluginUi.SettingsVisible = true;
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.SettingsVisible = true;
        }
    }
}
