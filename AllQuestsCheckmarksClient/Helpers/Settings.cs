using BepInEx.Configuration;
using UnityEngine;
using ZGFueDkx.ZGCLib.Config;

namespace AllQuestsCheckmarks.Helpers
{
    internal static class Settings
    {
        // How a part of the overlay (border, glow) is drawn
        // The corner where the dashed border is strongest
        public enum EDashCorner
        {
            Diagonal,
            AntiDiagonal,
            BottomLeft,
            BottomRight,
            TopLeft,
            TopRight,
            None,
        }

        public enum EOverlayPart
        {
            Native,
            Own,
            Off,
        }

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
        public static ConfigEntry<bool>? BuyFutureNeeds;
        public static ConfigEntry<bool>? UseOverlays;
        public static ConfigEntry<bool>? ClassicCheckmarks;
        public static ConfigEntry<float>? OverlayLaterStrength;
        public static ConfigEntry<float>? OverlayIconScale;
        public static ConfigEntry<bool>? OverlayDashedLater;
        public static ConfigEntry<bool>? RaidBuyIcon;
        public static ConfigEntry<bool>? KeepIcon;
        public static ConfigEntry<Color>? OverlayKeepColor;
        public static ConfigEntry<EDashCorner>? OverlayDashCorner;
        public static ConfigEntry<float>? OverlayDashFade;
        public static ConfigEntry<float>? OverlayDashLength;
        public static ConfigEntry<float>? OverlayDashGap;
        public static ConfigEntry<float>? OverlayDashedStrength;
        public static ConfigEntry<bool>? OverlayIconMatchColor;
        public static ConfigEntry<int>? OverlayBorderThickness;
        public static ConfigEntry<EOverlayPart>? OverlayBorderStyle;
        public static ConfigEntry<EOverlayPart>? OverlayGlowStyle;
        public static ConfigEntry<float>? OverlayBorderMultiplier;
        public static ConfigEntry<float>? OverlayGlowMultiplier;
        public static ConfigEntry<bool>? OverlayTintNative;
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

            BuyFutureNeeds = general.BindConfig(
                "Can buy for future needs",
                false,
                "Whether or not items you could buy but only need for future quests or hideout upgrades get the overlay. " +
                    "Off: only what you need now is outlined (the tooltip list always shows everything)"
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

            OverlayIconMatchColor = general.BindConfig(
                "Overlay icon in the overlay color",
                true,
                "Whether the overlay icon (and the clock for later needs) has the same color as the overlay border, so it reads as one thing. Off: white"
            );

            RaidBuyIcon = general.BindConfig(
                "Show buyable items that are needed in raid",
                true,
                "Whether items you need but can simply buy get just their icon (in the can buy color, no border or glow) while in raid, " +
                    "so they can be told apart from the ones you have to loot"
            );

            KeepIcon = general.BindConfig(
                "Show items to keep in the stash",
                true,
                "Whether items you hold that are earmarked for a later quest or a hideout upgrade get just their icon (in the keep color, no border) in your stash, " +
                    "so you know not to sell them. The tooltip lists how many to keep"
            );

            OverlayDashCorner = general.BindConfig(
                "Dashed border strong corners",
                EDashCorner.Diagonal,
                "Where the dashed border of later needs is at its strongest; it fades away from there.\n" +
                    "Diagonal - top left and bottom right (like the game's wishlist outline)\n" +
                    "AntiDiagonal - top right and bottom left\n" +
                    "A single corner, or None for even all around"
            );

            OverlayDashFade = general.BindConfig(
                "Dashed border fade amount",
                0.75f,
                "How much the dashed border fades away from its strong corners (0 = not at all, 1 = until it is gone at the far corners)",
                acceptableValue: new AcceptableValueRange<float>(0f, 1f)
            );

            OverlayDashLength = general.BindConfig(
                "Dash length",
                3f,
                "Length of one dash of the dashed border, in pixels",
                acceptableValue: new AcceptableValueRange<float>(1f, 12f)
            );

            OverlayDashGap = general.BindConfig(
                "Dash gap",
                2f,
                "Gap between two dashes of the dashed border, in pixels",
                acceptableValue: new AcceptableValueRange<float>(1f, 12f)
            );

            OverlayDashedLater = general.BindConfig(
                "Dashed border for later needs",
                true,
                "Whether items needed only later get a dashed border (strong, but clearly different from the solid one) instead of a weaker solid one"
            );

            OverlayDashedStrength = general.BindConfig(
                "Dashed border strength",
                0.9f,
                "How visible the dashed border of later needs is (1 = fully)",
                acceptableValue: new AcceptableValueRange<float>(0.1f, 1f)
            );

            OverlayBorderThickness = general.BindConfig(
                "Overlay border width",
                1,
                "Width of the thin colored border around an item with an overlay, in pixels",
                acceptableValue: new AcceptableValueRange<int>(1, 4)
            );

            OverlayBorderStyle = general.BindConfig(
                "Overlay border style",
                EOverlayPart.Native,
                "Native - the game's own wishlist border, colored and weakened or strengthened\n" +
                    "Own - a thin border drawn by this mod (see 'Overlay border width')\n" +
                    "Off - no border"
            );

            OverlayGlowStyle = general.BindConfig(
                "Overlay glow style",
                EOverlayPart.Native,
                "Native - the game's own wishlist glow, colored and weakened or strengthened\n" +
                    "Own - a soft glow along the inside of the item edge drawn by this mod\n" +
                    "Off - no glow"
            );

            OverlayBorderMultiplier = general.BindConfig(
                "Overlay border multiplier",
                1f,
                "Multiplies how visible the border is (on top of strong / later). 1 = as is, above 1 = more visible, 0 = invisible",
                acceptableValue: new AcceptableValueRange<float>(0f, 4f)
            );

            OverlayGlowMultiplier = general.BindConfig(
                "Overlay glow multiplier",
                1f,
                "Multiplies how visible the glow is (on top of strong / later). 1 = as is, above 1 = more visible, 0 = invisible",
                acceptableValue: new AcceptableValueRange<float>(0f, 4f)
            );

            OverlayTintNative = general.BindConfig(
                "Tint the native overlay",
                true,
                "Whether the game's own border and glow get the overlay color (gold, red...). The game's pictures are green, so the mod colors a white copy of them " +
                    "(same shape, exact color); off keeps the game's own green and only changes how visible it is"
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

            OverlayKeepColor = colors.BindColor(
                "Overlay color - keep",
                "#4aa8ff",
                "Color of the icon on items you hold that you should keep for later needs (in your stash)"
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
