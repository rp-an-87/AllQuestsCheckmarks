using AllQuestsCheckmarks.Helpers;

namespace AllQuestsCheckmarks
{
    /// <summary>
    /// Entry point for other mods (looked up by reflection, keep names and signatures unchanged).
    /// </summary>
    public static class AllQuestsCheckmarksApi
    {
        public const int ApiVersion = 1;

        /// <summary>
        /// True when this tooltip already lists hideout upgrades and quests for the hovered item
        /// (so Wishlist Extended can leave those lines out and avoid duplicates).
        /// </summary>
        public static bool ShowsHideoutInTooltip()
        {
            return Settings.IncludeHideout!.Value && Settings.HoverItemTooltip!.Value && WishlistBridge.Available;
        }
    }
}
