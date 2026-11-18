using BepInEx.Configuration;
using UnityEngine;
using ZGFueDkx.ZGCLib.Config;

namespace AllQuestsCheckmarks.Helpers
{
    // The settings added by this fork, in sections of their own (numbers 4 and up) so upstream's settings stay as they were:
    // 1. General, 2. Colors, 3. Text and 9. Debug are upstream's, they hold the classic checkmark settings
    internal static partial class Settings
    {
        // The corner(s) where the dashed border is strongest
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

        // Needs and overlays
        public static ConfigEntry<bool>? IncludeHideout;
        public static ConfigEntry<bool>? SharedConditions;
        public static ConfigEntry<bool>? CheckmarkOnNonFir;
        public static ConfigEntry<bool>? UseOverlays;
        public static ConfigEntry<float>? OverlayLaterStrength;
        public static ConfigEntry<bool>? BuyFutureNeeds;
        public static ConfigEntry<bool>? RaidBuyIcon;
        public static ConfigEntry<bool>? KeepNow;
        public static ConfigEntry<bool>? KeepIcon;
        public static ConfigEntry<bool>? RaidBuyLater;
        public static ConfigEntry<bool>? OverlayDashedLater;
        public static ConfigEntry<bool>? OverlayInHideoutRequirements;
        public static ConfigEntry<bool>? ClassicCheckmarks;
        public static ConfigEntry<bool>? VanillaCheckmarkInHideout;

        // Overlay colors
        public static ConfigEntry<Color>? OverlayHandInColor;
        public static ConfigEntry<Color>? OverlayCollectorColor;
        public static ConfigEntry<Color>? OverlayLootColor;
        public static ConfigEntry<Color>? OverlayBuyColor;
        public static ConfigEntry<Color>? OverlayKeepColor;

        // Tooltip
        public static ConfigEntry<bool>? HoverItemTooltip;
        public static ConfigEntry<bool>? SeparatedTooltip;
        public static ConfigEntry<bool>? ActionLine;
        public static ConfigEntry<bool>? CompactFirLabels;
        public static ConfigEntry<Color>? HideoutTextColor;

        // Overlay look (advanced)
        public static ConfigEntry<float>? OverlayBorderMultiplier;
        public static ConfigEntry<float>? OverlayGlowMultiplier;
        public static ConfigEntry<float>? OverlayDashedStrength;
        public static ConfigEntry<EDashCorner>? OverlayDashCorner;
        public static ConfigEntry<float>? OverlayDashFade;
        public static ConfigEntry<float>? OverlayDashLength;
        public static ConfigEntry<float>? OverlayDashGap;
        public static ConfigEntry<int>? OverlayBorderThickness;
        public static ConfigEntry<float>? OverlayIconScale;
        public static ConfigEntry<bool>? OverlayIconMatchColor;
        public static ConfigEntry<float>? OverlayFrameBudget;

        // Testing (advanced)
        public static ConfigEntry<bool>? DebugInRaid;
        public static ConfigEntry<bool>? DebugEmptyStash;

        // Every setting of this fork says so in its description, so it can be told apart from upstream's in F12
        private static ConfigEntry<T> ForkBind<T>(this ConfigCategory category, string key, T defaultValue, string description, bool hide = false, AcceptableValueBase? acceptableValue = null)
        {
            return category.BindConfig(key, defaultValue, "[Fork] " + description, hide, acceptableValue);
        }

        private static ConfigEntry<UnityEngine.Color> ForkColor(this ConfigCategory category, string key, string defaultValue, string description, bool hide = false)
        {
            return category.BindColor(key, defaultValue, "[Fork] " + description, hide);
        }

        private static void InitFork(ConfigFile config)
        {
            ConfigCategory needs = config.MakeCategory(4, "Needs and overlays");
            ConfigCategory colors = config.MakeCategory(5, "Overlay colors");
            ConfigCategory tooltip = config.MakeCategory(6, "Tooltip");
            ConfigCategory look = config.MakeCategory(7, "Overlay look");
            ConfigCategory testing = config.MakeCategory(8, "Testing");

            /*
             * NEEDS AND OVERLAYS
             */
            IncludeHideout = needs.ForkBind(
                "Include hideout upgrades (needs Wishlist Extended fork)",
                true,
                "Whether or not hideout upgrade requirements (FiR and non-FiR) count as needs, in the tooltip and in the overlays. " +
                    "Needs the Wishlist Extended fork; has no effect without it. Independent of that mod's 'Hideout upgrades wishlist mode': " +
                    "every remaining upgrade counts here, whatever the wishlist settings are"
            );

            SharedConditions = needs.ForkBind(
                "Shared quest conditions",
                true,
                "Whether or not quest conditions that accept several item types for one shared count (for example any medical item) are counted together. " +
                    "Off: they are ignored by the overlays and the checklist (each item just lists the quest)"
            );

            CheckmarkOnNonFir = needs.ForkBind(
                "Show checkmark on non-FiR items that are needed FiR",
                true,
                "Whether or not a non-FiR copy of an item that quests or hideout upgrades only accept found in raid still counts as relevant " +
                    "(the tooltip says it is not FiR, and in classic mode it gets a checkmark)"
            );

            UseOverlays = needs.ForkBind(
                "Need overlays",
                true,
                "Whether or not items you have to act on get an overlay: in raid what you have to loot (and the Collector items you lack), " +
                    "out of raid what you can hand in and what you still have to buy. Strong when needed now, dashed when needed later"
            );

            OverlayLaterStrength = look.ForkBind(
                "Overlay strength for later needs",
                1f,
                "How strong the overlay is for items needed only for future quests or hideout upgrades (1 = same as needed now: they only differ by the dashed border)",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(0.1f, 1f)
            );

            BuyFutureNeeds = needs.ForkBind(
                "Dashed buy border for later needs (stash)",
                false,
                "Whether or not items in your stash that you could buy but only need for future quests or hideout upgrades get the dashed can buy overlay. " +
                    "Off: only what you are missing now is outlined (the tooltip list always shows everything)"
            );

            RaidBuyIcon = needs.ForkBind(
                "Show buyable items that are needed in raid",
                true,
                "Whether or not items you need but can simply buy get just their icon (in the can buy color, no border or glow) while in raid, " +
                    "so they can be told apart from the ones you have to loot"
            );

            RaidBuyLater = needs.ForkBind(
                "Buy icon for later needs (in raid)",
                true,
                "Whether or not the buy icon in raid also shows for items you only need for future quests or hideout upgrades. Needs the setting above it"
            );

            KeepNow = needs.ForkBind(
                "Keep: covers the open upgrades (stash)",
                true,
                "Whether or not items in your stash that cover everything the upgrades you can start now need get the keep icon with a solid border"
            );

            KeepIcon = needs.ForkBind(
                "Keep: held for later needs (stash)",
                true,
                "Whether or not items you hold that are earmarked for a later quest or a hideout upgrade get just their icon (in the keep color, no border) in your stash, " +
                    "so you know not to sell them. The tooltip lists how many to keep"
            );

            OverlayDashedLater = look.ForkBind(
                "Dashed border for later needs",
                true,
                "Whether or not items needed only later get a dashed border instead of the solid one",
                hide: true
            );

            OverlayInHideoutRequirements = needs.ForkBind(
                "Overlays on hideout requirement items",
                true,
                "Whether or not the item icons of a hideout upgrade's requirements (the construction requirements screen) get the overlay too, " +
                    "for example the green can buy overlay on a requirement you do not have yet"
            );

            ClassicCheckmarks = needs.ForkBind(
                "Classic colored checkmarks",
                false,
                "Whether or not to use the original colored checkmark statuses (quest, future, hideout, collector, enough...), set up in sections 1. General and 2. Colors. " +
                    "Off: the checkmark is only the grey found in raid marker and the overlays show what you need. " +
                    "The checkmark settings of upstream (1. General: Hide checkmark if have enough; 2. Colors: all the checkmark colors) only matter with this on and are advanced otherwise"
            );

            VanillaCheckmarkInHideout = needs.ForkBind(
                "Vanilla checkmark on hideout requirement items",
                true,
                "Whether or not the item icons of hideout upgrade requirements show the vanilla checkmark (FiR items only) instead of the classic colored ones. " +
                    "Only matters with classic colored checkmarks"
            );

            /*
             * OVERLAY COLORS
             */
            OverlayHandInColor = colors.ForkColor(
                "Overlay color - hand in",
                "#ffc71a",
                "Color of the overlay on items you can hand in to a started quest (out of raid, in your stash)"
            );

            OverlayCollectorColor = colors.ForkColor(
                "Overlay color - collector",
                "#f22626",
                "Color of the overlay on Collector items you don't have yet (in raid)"
            );

            OverlayLootColor = colors.ForkColor(
                "Overlay color - must loot",
                "#ff5a2e",
                "Color of the overlay on items you have to loot (in raid)"
            );

            OverlayBuyColor = colors.ForkColor(
                "Overlay color - can buy",
                "#4fd36b",
                "Color of the overlay on items you still have to buy (out of raid), and of the icon of buyable items in raid"
            );

            OverlayKeepColor = colors.ForkColor(
                "Overlay color - keep",
                "#4aa8ff",
                "Color of the icon on items you hold that you should keep for later needs (in your stash)"
            );

            /*
             * TOOLTIP
             */
            HoverItemTooltip = tooltip.ForkBind(
                "Show tooltip when hovering the item",
                true,
                "Whether or not to also show the checkmark tooltip when hovering the item itself, not only the checkmark. " +
                    "The Wishlist Extended fork then leaves its own hideout and quest lines out (its setting 'Leave hideout and quests to AllQuestsCheckmarks fork')"
            );

            SeparatedTooltip = tooltip.ForkBind(
                "Separated tooltip layout",
                true,
                "Whether or not to use the layout: found in raid label (green), separator, stash and totals, separator, quests and hideout. " +
                    "Off keeps the original single block"
            );

            ActionLine = tooltip.ForkBind(
                "Action line in the tooltip",
                true,
                "Whether or not the tooltip should end with the checklist saying what to do with the item and why " +
                    "(can hand in, can buy, keep, must get FiR)"
            );

            CompactFirLabels = tooltip.ForkBind(
                "Small FiR labels",
                true,
                "Whether or not FiR / non-FiR labels in the tooltip lines should be smaller, with non-FiR in grey",
                hide: true
            );

            HideoutTextColor = tooltip.ForkColor(
                "Custom text color - hideout",
                "#4dc3ff",
                "Custom color of hideout upgrades text (the other text colors are in section 3. Text)"
            );

            /*
             * OVERLAY LOOK (advanced)
             */
            OverlayBorderMultiplier = look.ForkBind(
                "Overlay border multiplier",
                1f,
                "Multiplies how visible the game's border is (on top of strong / later). 1 = as is, above 1 = more visible, 0 = invisible",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(0f, 4f)
            );

            OverlayGlowMultiplier = look.ForkBind(
                "Overlay glow multiplier",
                1f,
                "Multiplies how visible the game's glow is (on top of strong / later). 1 = as is, above 1 = more visible, 0 = invisible",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(0f, 4f)
            );

            OverlayDashedStrength = look.ForkBind(
                "Dashed border strength",
                1f,
                "How visible the dashed border of later needs is (1 = fully)",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(0.1f, 1f)
            );

            OverlayDashCorner = look.ForkBind(
                "Dashed border strong corners",
                EDashCorner.Diagonal,
                "Where the dashed border of later needs is at its strongest; it fades away from there.\n" +
                    "Diagonal - top left and bottom right (like the game's wishlist outline)\n" +
                    "AntiDiagonal - top right and bottom left\n" +
                    "A single corner, or None for even all around",
                hide: true
            );

            OverlayDashFade = look.ForkBind(
                "Dashed border fade amount",
                0.75f,
                "How much the dashed border fades away from its strong corners (0 = not at all, 1 = until it is gone at the far corners)",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(0f, 1f)
            );

            OverlayDashLength = look.ForkBind(
                "Dash length",
                3f,
                "Length of one dash of the dashed border, in pixels",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(1f, 12f)
            );

            OverlayDashGap = look.ForkBind(
                "Dash gap",
                2f,
                "Gap between two dashes of the dashed border, in pixels",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(1f, 12f)
            );

            OverlayBorderThickness = look.ForkBind(
                "Dashed border width",
                1,
                "Width of the dashed border, in pixels",
                hide: true,
                acceptableValue: new AcceptableValueRange<int>(1, 4)
            );

            OverlayIconScale = look.ForkBind(
                "Overlay icon scale",
                1f,
                "Scale of the overlay icon (1 = the size of the game's own wishlist icon). The position is the game's: it knows where the item has other markers",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(0.5f, 2.5f)
            );

            OverlayIconMatchColor = look.ForkBind(
                "Overlay icon in the overlay color",
                true,
                "Whether or not the overlay icon has the same color as the overlay border, so it reads as one thing. Off: white",
                hide: true
            );

            OverlayFrameBudget = look.ForkBind(
                "Overlay drawing budget",
                2f,
                "Most milliseconds per frame spent drawing overlays on item icons. Lower is smoother, higher is faster to appear when a screen opens",
                hide: true,
                acceptableValue: new AcceptableValueRange<float>(0.5f, 10f)
            );

            /*
             * TESTING (advanced)
             */
            DebugInRaid = testing.ForkBind(
                "Pretend to be in raid",
                false,
                "Testing only: the need overlays behave as if a raid was running (loot and Collector overlays instead of hand-in and buy). " +
                    "Counts what you hold from your real stash. Reopen the stash screen to see the change",
                hide: true
            );

            DebugEmptyStash = testing.ForkBind(
                "Pretend the stash is empty",
                false,
                "Testing only: the need overlays ignore what you hold, so every item you may need shows its overlay. Reopen the stash screen to see the change",
                hide: true
            );

            // the needs depend on almost every setting: rebuild them whenever one changes
            // upstream's checkmark settings only matter for the classic colored checkmarks: advanced unless those are on
            // (done here so upstream's Settings.cs stays untouched)
            ConfigEntryBase?[] classicOnly =
            [
                HideFulfilled, MarkEnoughItems, UseCustomQuestColor, CustomQuestColor, CheckmarkColor,
                NonFirColor, CollectorColor, EnoughItemsColor, SquadColor,
            ];

            void ApplyClassicOnly()
            {
                foreach (ConfigEntryBase? entry in classicOnly)
                {
                    foreach (object tag in entry?.Description.Tags ?? [])
                    {
                        if (tag is ConfigurationManagerAttributes attributes)
                        {
                            attributes.IsAdvanced = !ClassicCheckmarks!.Value;
                        }
                    }
                }
            }

            ApplyClassicOnly();
            ClassicCheckmarks!.SettingChanged += (_, _) => ApplyClassicOnly();

            config.SettingChanged += (_, _) => NeedsSnapshot.Invalidate();
        }
    }
}
