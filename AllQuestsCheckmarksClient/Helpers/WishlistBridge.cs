using BepInEx.Bootstrap;
using System;
using System.Reflection;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// Reads hideout requirements from the Wishlist Extended fork (WishlistExtended.WishlistExtendedApi) by reflection,
    /// so it stays an optional dependency.
    /// </summary>
    internal static class WishlistBridge
    {
        private const string PluginGuid = "com.zgfuedkx.wishlistextended";

        private delegate bool TryGetNeedsDelegate(string templateId, out int[] quantity, out bool[] fir, out bool[] future, out string[] area, out int[] level);

        private static bool _resolved;
        private static TryGetNeedsDelegate? _tryGetNeeds;

        public class HideoutNeeds
        {
            public int[] Quantity = [];
            public bool[] Fir = [];
            public bool[] Future = [];
            public string[] Area = [];
            public int[] Level = [];
        }

        public static bool Available
        {
            get
            {
                Resolve();
                return _tryGetNeeds is not null;
            }
        }

        public static bool TryGetHideoutNeeds(string templateId, out HideoutNeeds needs)
        {
            needs = new HideoutNeeds();
            Resolve();

            if (_tryGetNeeds is null)
            {
                return false;
            }

            try
            {
                return _tryGetNeeds(templateId, out needs.Quantity, out needs.Fir, out needs.Future, out needs.Area, out needs.Level);
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogError($"Wishlist Extended hideout lookup failed, disabling it: {e}");
                _tryGetNeeds = null;
                return false;
            }
        }

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;

            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info) || info.Instance is null)
            {
                return;
            }

            Type? api = info.Instance.GetType().Assembly.GetType("WishlistExtended.WishlistExtendedApi");
            MethodInfo? method = api?.GetMethod("TryGetHideoutNeeds", BindingFlags.Public | BindingFlags.Static);

            if (method is null)
            {
                Plugin.LogSource?.LogWarning("Wishlist Extended is installed but has no hideout API (use the fork) - hideout upgrades are not shown");
                return;
            }

            _tryGetNeeds = (TryGetNeedsDelegate)Delegate.CreateDelegate(typeof(TryGetNeedsDelegate), method);
            Plugin.LogSource?.LogInfo("Wishlist Extended hideout API found");
        }
    }
}
