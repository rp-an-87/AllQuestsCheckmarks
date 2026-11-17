using BepInEx.Configuration;
using UnityEngine;
using ZGFueDkx.ZGCLib.Config;

namespace AllQuestsCheckmarks.Helpers
{
    internal static class Settings
    {
        public static ConfigEntry<bool>? IncludeCollector;
        public static ConfigEntry<bool>? IncludeLoyaltyRegain;
        public static ConfigEntry<bool>? IncludeUnreachable;
        public static ConfigEntry<bool>? IncludeNonFir;
        public static ConfigEntry<bool>? HideFulfilled;
        public static ConfigEntry<bool>? OnlyActiveQuests;
        public static ConfigEntry<bool>? IncludeRaidItems;
        public static ConfigEntry<bool>? SquadQuests;
        public static ConfigEntry<bool>? IncludeHideout;
        public static ConfigEntry<bool>? CheckmarkOnNonFir;
        public static ConfigEntry<bool>? HoverItemTooltip;
        public static ConfigEntry<bool>? SeparatedTooltip;
        public static ConfigEntry<bool>? ActionLine;
        public static ConfigEntry<bool>? SharedConditions;
        public static ConfigEntry<bool>? UseOverlays;
        public static ConfigEntry<bool>? ClassicCheckmarks;
        public static ConfigEntry<float>? OverlayLaterStrength;
        public static ConfigEntry<float>? OverlayIconScale;
        public static ConfigEntry<int>? OverlayBorderThickness;
        public static ConfigEntry<float>? OverlayGlowStrength;
        public static ConfigEntry<float>? OverlayFrameBudget;
        public static ConfigEntry<bool>? VanillaCheckmarkInHideout;
        public static ConfigEntry<bool>? CompactFirLabels;
        public static ConfigEntry<bool>? MarkEnoughItems;
        public static ConfigEntry<bool>? UseCustomQuestColor;
        public static ConfigEntry<bool>? BulletPoints;
        public static ConfigEntry<bool>? CustomTextColors;
        public static ConfigEntry<bool>? ShowDebug;
        public static ConfigEntry<bool>? DebugInRaid;
        public static ConfigEntry<bool>? DebugEmptyStash;

        public static ConfigEntry<Color>? OverlayHandInColor;
        public static ConfigEntry<Color>? OverlayCollectorColor;
        public static ConfigEntry<Color>? OverlayLootColor;
        public static ConfigEntry<Color>? OverlayBuyColor;
        public static ConfigEntry<Color>? CheckmarkColor;
        public static ConfigEntry<Color>? NonFirColor;
        public static ConfigEntry<Color>? CollectorColor;
        public static ConfigEntry<Color>? EnoughItemsColor;
        public static ConfigEntry<Color>? CustomQuestColor;
        public static ConfigEntry<Color>? SquadColor;
        public static ConfigEntry<Color>? ActiveQuestTextColor;
        public static ConfigEntry<Color>? FutureQuestTextColor;
        public static ConfigEntry<Color>? HideoutTextColor;
        public static ConfigEntry<Color>? SquadQuestTextColor;

        public static void Init(ConfigFile config)
        {
            ConfigCategory general = config.MakeCategory(1, "General");
            ConfigCategory colors = config.MakeCategory(2, "Colors");
            ConfigCategory text = config.MakeCategory(3, "Text");
            ConfigCategory debug = config.MakeCategory(9, "Debug");

            /*
             * GENERAL
             */
            IncludeCollector = general.BindConfig(
                "Include Collector quest (Fence)",
                true,
                "Whether or not to include items needed for Collector quest"
            );

            IncludeNonFir = general.BindConfig(
                "Include non-FiR quests",
                true,
                "Whether or not to include quests that don't require found in raid items"
            );

            IncludeLoyaltyRegain = general.BindConfig(
                "Include loyalty regain quests",
                false,
                "Whether or not to include quests for regaining loyalty (Compensation for Damage (Fence), Make Amends (Lightkeeper) & Chemical questline finale)"
            );

            IncludeUnreachable = general.BindConfig(
                "Include unreachable quests",
                false,
                "Whether or not to include quests that are unreachable (event quests and quests for other account types)"
            );

            HideFulfilled = general.BindConfig(
                "Hide checkmark if have enough (in raid)",
                false,
                "Whether or not to hide checkmark in raid on items that you have enough for all active and future quests. Be careful when using with " +
                    "'Include items in PMC inventory (in raid)', as this combo may hide checkmarks while still in raid!"
            );

            OnlyActiveQuests = general.BindConfig(
                "Show only active quests",
                false,
                "Whether or not to show only active quests (no future quests)"
            );

            IncludeRaidItems = general.BindConfig(
                "Include items in PMC inventory (in raid)",
                false,
                "Whether or not to include items in PMC inventory while in raid in 'In Stash' count"
            );

            IncludeHideout = general.BindConfig(
                "Include hideout upgrades (needs Wishlist Extended fork)",
                true,
                "Whether or not to add hideout upgrade requirements (FiR and non-FiR) to the tooltip and to 'Total needed'. " +
                    "Needs the Wishlist Extended fork; has no effect without it"
            );

            CheckmarkOnNonFir = general.BindConfig(
                "Show checkmark on non-FiR items that are needed FiR",
                true,
                "Whether or not non-FiR items get a checkmark and tooltip when quests or hideout upgrades only accept FiR items. " +
                    "The tooltip says the copy is not FiR; non-FiR color is used for future needs"
            );

            HoverItemTooltip = general.BindConfig(
                "Show tooltip when hovering the item",
                true,
                "Whether or not to also show the checkmark tooltip when hovering the item itself, not only the checkmark"
            );

            SeparatedTooltip = general.BindConfig(
                "Separated tooltip layout",
                true,
                "Whether or not to use the layout: found in raid label (green), separator, stash and totals, separator, quests and hideout. " +
                    "Off keeps the original single block"
            );

            VanillaCheckmarkInHideout = general.BindConfig(
                "Vanilla checkmark on hideout requirement items",
                true,
                "Whether or not the item icons of hideout upgrade requirements should show the vanilla checkmark (FiR items only) instead of the colored ones. " +
                    "The tooltip is not affected"
            );

            CompactFirLabels = general.BindConfig(
                "Small FiR labels",
                true,
                "Whether or not FiR / non-FiR labels in the tooltip lines should be smaller, with non-FiR in grey"
            );

            SharedConditions = general.BindConfig(
                "Shared quest conditions",
                true,
                "Whether or not quest conditions that accept several item types for one shared count (for example any dogtag) are counted together. " +
                    "Off: they are ignored by the overlays and the checklist (each item just lists the quest)"
            );

            ActionLine = general.BindConfig(
                "Action line in the tooltip",
                true,
                "Whether or not the tooltip should end with a small line saying what to do with the item and why " +
                    "(for example: can buy 3 for current hideout upgrades and future quests, or needs to be looted in raid)"
            );

            UseOverlays = general.BindConfig(
                "Need overlays",
                true,
                "Whether or not items you have to act on get an outline overlay: in raid what you have to loot (or the Collector items you lack), " +
                    "out of raid what you can hand in (gold) and what you still have to buy. Strong when needed now, weaker when needed later"
            );

            OverlayLaterStrength = general.BindConfig(
                "Overlay strength for later needs",
                0.5f,
                "How strong the overlay is for items needed only for future quests or hideout upgrades (1 = same as needed now)",
                acceptableValue: new AcceptableValueRange<float>(0.1f, 1f)
            );

            OverlayIconScale = general.BindConfig(
                "Overlay icon scale",
                1f,
                "Scale of the overlay icon (1 = the size of the game's own wishlist icon). The position is the game's: it knows where the item has other markers",
                acceptableValue: new AcceptableValueRange<float>(0.5f, 2.5f)
            );

            OverlayBorderThickness = general.BindConfig(
                "Overlay border width",
                1,
                "Width of the thin colored border around an item with an overlay, in pixels",
                acceptableValue: new AcceptableValueRange<int>(1, 4)
            );

            OverlayGlowStrength = general.BindConfig(
                "Overlay glow strength",
                0.55f,
                "How strong the soft glow along the inside of the item edge is (0 = no glow, 1 = full). The border and the icon are not affected",
                acceptableValue: new AcceptableValueRange<float>(0f, 1f)
            );

            OverlayFrameBudget = general.BindConfig(
                "Overlay drawing budget",
                2f,
                "Most milliseconds per frame spent drawing overlays on item icons. Lower is smoother, higher is faster to appear when a screen opens",
                acceptableValue: new AcceptableValueRange<float>(0.5f, 10f)
            );

            ClassicCheckmarks = general.BindConfig(
                "Classic colored checkmarks",
                false,
                "Whether or not to keep the colored checkmark statuses (quest, future, hideout, collector, enough...). " +
                    "Off: the checkmark is only the grey found in raid marker and the overlays show what you need"
            );

            /*
             * COLORS
             */
            CheckmarkColor = colors.BindColor(
                "Checkmark color",
                "#bf00ff",
                "Color of checkmark if item is not currently needed but is required for future quests"
            );

            NonFirColor = colors.BindColor(
                "Checkmark color (non-FIR)",
                "#73264d",
                "Color of checkmark if non-FiR item is not currently needed but is required for future quests"
            );

            CollectorColor = colors.BindColor(
                "Collector color",
                "#bf00ff",
                "Color of checkmark for collector quest"
            );

            OverlayHandInColor = colors.BindColor(
                "Overlay color - hand in",
                "#ffc71a",
                "Color of the overlay on items you can hand in to a started quest (out of raid, in your stash)"
            );

            OverlayCollectorColor = colors.BindColor(
                "Overlay color - collector",
                "#f22626",
                "Color of the overlay on Collector items you don't have yet (in raid)"
            );

            OverlayLootColor = colors.BindColor(
                "Overlay color - must loot",
                "#ff5a2e",
                "Color of the overlay on items you have to loot (in raid)"
            );

            OverlayBuyColor = colors.BindColor(
                "Overlay color - can buy",
                "#4fd36b",
                "Color of the overlay on items you still have to buy (out of raid)"
            );

            MarkEnoughItems = colors.BindConfig(
                "Use different color if have enough",
                false,
                "Whether or not to use different checkmark color if you have enough items for all quests. " +
                    "'Hide checkmark if have enough' option will hide this checkmark while in raid"
            );

            EnoughItemsColor = colors.BindColor(
                "Have enough color",
                "#00ff00",
                "Color of checkmark if you have enough items for all quests"
            );

            UseCustomQuestColor = colors.BindConfig(
                "Use custom quest checkmark color",
                false,
                "Whether or not to use custom checkmark color for active quests"
            );

            CustomQuestColor = colors.BindColor(
                "Custom quest color",
                "#ffeb6d",
                "Custom color of default quest checkmark"
            );

            /*
             * TEXT
             */
            BulletPoints = text.BindConfig(
                "Use bullet points",
                true,
                "Whether or not to use bullet points in quests list"
            );

            CustomTextColors = text.BindConfig(
                "Use custom text colors",
                false,
                "Whether or not to use custom text colors"
            );

            ActiveQuestTextColor = text.BindColor(
                "Custom text color - active quests",
                "#dd831a",
                "Custom color of active quests text"
            );

            FutureQuestTextColor = text.BindColor(
                "Custom text color - future quests",
                "#d24dff",
                "Custom color of future quests text"
            );

            HideoutTextColor = text.BindColor(
                "Custom text color - hideout",
                "#4dc3ff",
                "Custom color of hideout upgrades text"
            );

            if (Plugin.isFikaInstalled)
            {
                SquadQuests = general.BindConfig(
                    "Mark squad members quests",
                    true,
                    "Wether or not to mark items currently needed for players in your squad"
                );

                SquadColor = colors.BindColor(
                    "Checkmark color (squad members)",
                    "#ff3333",
                    "Color of checkmark if item is not currently needed but is required for one of your squad members"
                );

                SquadQuestTextColor = text.BindColor(
                    "Custom text color - squad quests",
                    "#ffc299",
                    "Custom color of squad quests text"
                );
            }

            /*
             * DEBUG
             */
            ShowDebug = debug.BindConfig(
                "Debug logs",
                false,
                "Enable debug logs in Player.log"
            );

            DebugInRaid = debug.BindConfig(
                "Pretend to be in raid",
                false,
                "Testing only: the need overlays behave as if a raid was running (loot and Collector overlays instead of hand-in and buy). " +
                    "Counts what you hold from your real stash. Reopen the stash screen to see the change"
            );

            DebugEmptyStash = debug.BindConfig(
                "Pretend the stash is empty",
                false,
                "Testing only: the need overlays ignore what you hold, so every item you may need shows its overlay. Reopen the stash screen to see the change"
            );

            debug.BindButton(
                "Reload quests data",
                "Reload",
                "Reload quests data from server",
                () =>
                {
                    QuestsData.LoadData();
                }
            );

            config.SettingChanged += SettingChanged;
            Plugin.LogSource?.LogInfo("Settings loaded");
        }

        private static void SettingChanged(object sender, SettingChangedEventArgs args)
        {
            NeedsSnapshot.Invalidate();

            switch (args.ChangedSetting.Definition.Key)
            {
                case "Include Collector quest (Fence)":
                case "Include non-FiR quest":
                case "Include loyalty regain quests":
                    QuestsData.LoadData();
                    break;
            }
        }
    }
}
