using AllQuestsCheckmarks.Helpers;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UI.DragAndDrop.ItemViews;
using UnityEngine;
using UnityEngine.UI;
using ZGFueDkx.ZGCLib.Config;
using ZGFueDkx.ZGCLib.helpers;

namespace AllQuestsCheckmarks.Patches
{
    /// <summary>
    /// Draws the needs snapshot on the wishlist overlay of an item view (glow, border, icon): strength = urgency
    /// (needed now / later), kind = what to do (hand in, collector, loot, buy). Where there is nothing to do the native
    /// wishlist look is left alone.
    /// </summary>
    internal static class NeedsOverlay
    {
        private static readonly AccessTools.FieldRef<WishlistGridView, MongoID> _templateRef =
            AccessTools.FieldRefAccess<WishlistGridView, MongoID>("_templateId");

        // what a view looked like before we touched it, so it can be put back when the item view is reused
        private class State
        {
            public bool Examined = true;
            public bool Modified;
            public readonly Dictionary<Graphic, Color> Colors = [];
            public Vector2 IconAnchorMin, IconAnchorMax, IconPivot, IconPosition;
            public bool IconSaved;
        }

        private static readonly ConditionalWeakTable<WishlistGridView, State> _states = new();
        private static readonly HashSet<WishlistGridView> _views = [];
        private static bool _applying;
        private static bool _refreshQueued;

        public static void EnablePatches()
        {
            new OverlaySetExaminedPatch().Enable();
            new OverlayAddPatch().Enable();
            new OverlayRemovePatch().Enable();
            new OverlayGroupPatch().Enable();
            new InventoryChangedPatch().Enable();
        }

        // Something in an inventory changed (bought, sold, moved, handed in): rebuild the snapshot and redraw the item views that are open.
        // Debounced, so a burst of events costs one rebuild.
        public static void OnInventoryChanged()
        {
            NeedsSnapshot.Invalidate();

            if (_refreshQueued || Plugin.Instance == null)
            {
                return;
            }

            _refreshQueued = true;
            Plugin.Instance.StartCoroutine(RefreshLater());
        }

        private static IEnumerator RefreshLater()
        {
            yield return new WaitForSeconds(0.2f);
            _refreshQueued = false;

            _views.RemoveWhere(v => v == null);

            foreach (WishlistGridView view in _views.ToArray())
            {
                if (view != null && view.isActiveAndEnabled)
                {
                    Apply(view);
                }
            }
        }

        public static void OnExamined(WishlistGridView view, bool examined)
        {
            _states.GetOrCreateValue(view).Examined = examined;
            Apply(view);
        }

        public static void Apply(WishlistGridView view)
        {
            if (_applying || view == null)
            {
                return;
            }

            _views.Add(view);

            _applying = true;

            try
            {
                State state = _states.GetOrCreateValue(view);

                if (!Settings.UseOverlays!.Value || !state.Examined || !TryGetStyle(view, out Color color, out float strength, out EWishlistGroup group, out bool corner))
                {
                    Restore(view, state);
                    return;
                }

                Style(view, state, color, strength, group, corner);
            }
            finally
            {
                _applying = false;
            }
        }

        private static bool TryGetStyle(WishlistGridView view, out Color color, out float strength, out EWishlistGroup group, out bool corner)
        {
            color = default;
            strength = 1f;
            group = EWishlistGroup.Other;
            corner = false;

            // the requirement icons of a hideout upgrade already say what is needed
            if (view.GetComponentInParent<EFT.Hideout.ItemRequirementPanel>() != null)
            {
                return false;
            }

            ItemView? itemView = view.GetComponentInParent<ItemView>();
            EItemViewType? viewType = itemView?.ItemContext?.ViewType;

            if (viewType == EItemViewType.Hideout || !NeedsSnapshot.TryGet(_templateRef(view), out NeedsSnapshot.ItemNeed need))
            {
                return false;
            }

            float later = Settings.OverlayLaterStrength!.Value;
            NeedsSnapshot.EStrength kind;

            if (NeedsSnapshot.InRaid)
            {
                if (need.CollectorMissing)
                {
                    color = Settings.OverlayCollectorColor!.GetValue();
                    corner = true;
                    group = EWishlistGroup.Other;
                    return true;
                }

                kind = need.Loot;
            }
            else
            {
                // handing in only makes sense for items in your own stash
                if (need.HandIn && viewType == EItemViewType.Inventory)
                {
                    color = Settings.OverlayHandInColor!.GetValue();
                    corner = true;
                    group = EWishlistGroup.Quests;
                    return true;
                }

                kind = need.Buy;
            }

            if (kind == NeedsSnapshot.EStrength.None)
            {
                return false;
            }

            color = NeedsSnapshot.InRaid ? Settings.OverlayLootColor!.GetValue() : Settings.OverlayBuyColor!.GetValue();
            strength = kind == NeedsSnapshot.EStrength.Now ? 1f : later;
            group = need.FromQuest && need.FromHideout ? EWishlistGroup.Other : need.FromQuest ? EWishlistGroup.Quests : EWishlistGroup.Hideout;
            return true;
        }

        private static void Style(WishlistGridView view, State state, Color color, float strength, EWishlistGroup group, bool corner)
        {
            view.SetElementsActive(true);

            foreach (GameObject part in new[] { view._glow, view._border })
            {
                if (part == null)
                {
                    continue;
                }

                foreach (Graphic graphic in part.GetComponentsInChildren<Graphic>(true))
                {
                    if (!state.Colors.TryGetValue(graphic, out Color original))
                    {
                        original = graphic.color;
                        state.Colors[graphic] = original;
                    }

                    bool tinted = color != Color.white;
                    graphic.color = new Color(tinted ? color.r : original.r, tinted ? color.g : original.g, tinted ? color.b : original.b, original.a * strength);
                }
            }

            if (view._icon != null)
            {
                RectTransform rect = view._icon.rectTransform;

                if (!state.IconSaved)
                {
                    state.IconSaved = true;
                    state.IconAnchorMin = rect.anchorMin;
                    state.IconAnchorMax = rect.anchorMax;
                    state.IconPivot = rect.pivot;
                    state.IconPosition = rect.anchoredPosition;
                }

                view.SetIcon(group);

                Color iconColor = view._icon.color;
                view._icon.color = new Color(iconColor.r, iconColor.g, iconColor.b, strength);

                if (corner)
                {
                    // bottom left, like the owner asked for the hand-in and collector markers
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                    rect.anchoredPosition = new Vector2(2f, 2f);
                }
                else
                {
                    rect.anchorMin = state.IconAnchorMin;
                    rect.anchorMax = state.IconAnchorMax;
                    rect.pivot = state.IconPivot;
                    rect.anchoredPosition = state.IconPosition;
                }
            }

            state.Modified = true;
        }

        private static void Restore(WishlistGridView view, State state)
        {
            if (!state.Modified)
            {
                return;
            }

            state.Modified = false;

            foreach (KeyValuePair<Graphic, Color> entry in state.Colors)
            {
                if (entry.Key != null)
                {
                    entry.Key.color = entry.Value;
                }
            }

            if (view._icon != null && state.IconSaved)
            {
                RectTransform rect = view._icon.rectTransform;
                rect.anchorMin = state.IconAnchorMin;
                rect.anchorMax = state.IconAnchorMax;
                rect.pivot = state.IconPivot;
                rect.anchoredPosition = state.IconPosition;

                Color iconColor = view._icon.color;
                view._icon.color = new Color(iconColor.r, iconColor.g, iconColor.b, 1f);
            }

            // back to the native look (the real wishlist decides whether it shows)
            view.SetExamined(state.Examined);
        }
    }

    // The same event Wishlist Extended uses to know the stash changed
    internal class InventoryChangedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemController), nameof(ItemController.SafeRefreshItemEvent));
        }

        [PatchPostfix]
        private static void Postfix()
        {
            NeedsOverlay.OnInventoryChanged();
        }
    }

    internal class OverlaySetExaminedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WishlistGridView), nameof(WishlistGridView.SetExamined));
        }

        [PatchPostfix]
        private static void Postfix(WishlistGridView __instance, bool examined)
        {
            NeedsOverlay.OnExamined(__instance, examined);
        }
    }

    internal class OverlayAddPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WishlistGridView), nameof(WishlistGridView.OnAddInWishlist));
        }

        [PatchPostfix]
        private static void Postfix(WishlistGridView __instance)
        {
            NeedsOverlay.Apply(__instance);
        }
    }

    internal class OverlayRemovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WishlistGridView), nameof(WishlistGridView.OnRemoveFromWishlist));
        }

        [PatchPostfix]
        private static void Postfix(WishlistGridView __instance)
        {
            NeedsOverlay.Apply(__instance);
        }
    }

    internal class OverlayGroupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WishlistGridView), nameof(WishlistGridView.OnChangeWishlistGroup));
        }

        [PatchPostfix]
        private static void Postfix(WishlistGridView __instance)
        {
            NeedsOverlay.Apply(__instance);
        }
    }
}
