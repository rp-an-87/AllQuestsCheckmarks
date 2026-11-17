using AllQuestsCheckmarks.Helpers;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Draws the needs snapshot on the wishlist overlay of an item view: a colored border and a faint fill of our own
    /// (the game's glow and border are too faint to tell strong from weak) plus the game's icon, which knows where to sit.
    /// Strength = urgency (needed now / later), kind = what to do (hand in, collector, loot, buy).
    /// Where there is nothing to do the native wishlist look is left alone.
    /// Drawing is queued and spread over frames (see Settings.OverlayFrameBudget), so opening a full stash does not stall.
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
            public int Retries;
            public Image? Frame;
            public Image? Fill;
            public bool IconSaved;
            public Color IconColor;
            public Vector3 IconScale;
        }

        private static readonly ConditionalWeakTable<WishlistGridView, State> _states = new();
        private static readonly HashSet<WishlistGridView> _views = [];
        private static readonly Queue<WishlistGridView> _queue = new();
        private static readonly HashSet<WishlistGridView> _queued = [];
        private static bool _applying;
        private static bool _pumping;
        private static bool _refreshQueued;
        private static int _eventsSinceRefresh;
        private static int _appliedVersion = -1;
        private static Sprite? _frameSprite;

        public static void EnablePatches()
        {
            new OverlaySetExaminedPatch().Enable();
            new OverlayAddPatch().Enable();
            new OverlayRemovePatch().Enable();
            new OverlayGroupPatch().Enable();
            new InventoryChangedPatch().Enable();
        }

        // ---------------------------------------------------------------- drawing queue

        private static void Enqueue(WishlistGridView view)
        {
            if (view == null || Plugin.Instance == null || !_queued.Add(view))
            {
                return;
            }

            _queue.Enqueue(view);

            if (!_pumping)
            {
                _pumping = true;
                Plugin.Instance.StartCoroutine(Pump());
            }
        }

        // Applies queued views within a time budget each frame. The first frame is skipped on purpose: when an item view is created
        // it is not fully set up yet (its item context, its place in the hierarchy)
        private static IEnumerator Pump()
        {
            yield return null;

            while (_queue.Count > 0)
            {
                Stopwatch watch = Stopwatch.StartNew();
                double budget = Settings.OverlayFrameBudget!.Value;

                while (_queue.Count > 0 && watch.Elapsed.TotalMilliseconds < budget)
                {
                    WishlistGridView view = _queue.Dequeue();
                    _queued.Remove(view);

                    if (view != null && view.isActiveAndEnabled)
                    {
                        Apply(view);
                    }
                }

                if (_queue.Count > 0)
                {
                    yield return null;
                }
            }

            _pumping = false;
        }

        // Something in an inventory changed (bought, sold, moved, handed in): rebuild the snapshot and redraw the item views that are open.
        // Debounced, so a burst of events costs one rebuild.
        public static void OnInventoryChanged()
        {
            _eventsSinceRefresh++;
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
            yield return new WaitForSeconds(0.3f);
            _refreshQueued = false;

            if (_eventsSinceRefresh > 30)
            {
                Plugin.LogSlow($"{_eventsSinceRefresh} inventory events in 0.3 s");
            }

            _eventsSinceRefresh = 0;

            // rebuild first; redraw only if the result is different from what the views already show
            NeedsSnapshot.Refresh();

            if (NeedsSnapshot.Version == _appliedVersion)
            {
                yield break;
            }

            _appliedVersion = NeedsSnapshot.Version;
            _views.RemoveWhere(v => v == null);

            foreach (WishlistGridView view in _views.ToArray())
            {
                if (view != null && view.isActiveAndEnabled)
                {
                    Enqueue(view);
                }
            }
        }

        private static IEnumerator RetryLater(WishlistGridView view)
        {
            yield return new WaitForSeconds(0.25f);
            Enqueue(view);
        }

        // ---------------------------------------------------------------- hooks

        public static void OnExamined(WishlistGridView view, bool examined)
        {
            _states.GetOrCreateValue(view).Examined = examined;

            // our own Restore calls SetExamined: nothing to redraw then
            if (_applying)
            {
                return;
            }

            _views.Add(view);
            Enqueue(view);
        }

        public static void OnChanged(WishlistGridView view)
        {
            if (_applying)
            {
                return;
            }

            _views.Add(view);
            Enqueue(view);
        }

        // ---------------------------------------------------------------- applying

        private static void Apply(WishlistGridView view)
        {
            if (_applying || view == null)
            {
                return;
            }

            _applying = true;

            try
            {
                State state = _states.GetOrCreateValue(view);

                if (!Settings.UseOverlays!.Value || !state.Examined || !TryGetStyle(view, state, out Color color, out float strength, out EWishlistGroup group))
                {
                    Restore(view, state);
                    return;
                }

                Style(view, state, color, strength, group);
            }
            finally
            {
                _applying = false;
            }
        }

        private static bool TryGetStyle(WishlistGridView view, State state, out Color color, out float strength, out EWishlistGroup group)
        {
            color = default;
            strength = 1f;
            group = EWishlistGroup.Other;

            // the requirement icons of a hideout upgrade already say what is needed
            if (view.GetComponentInParent<EFT.Hideout.ItemRequirementPanel>() != null)
            {
                return false;
            }

            ItemView? itemView = view.GetComponentInParent<ItemView>();
            EItemViewType? viewType = itemView?.ItemContext?.ViewType;

            // not set up yet: look again a moment later
            if (viewType == null && state.Retries < 4 && Plugin.Instance != null)
            {
                state.Retries++;
                Plugin.Instance.StartCoroutine(RetryLater(view));
                return false;
            }

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
                    group = EWishlistGroup.Other;
                    return true;
                }

                kind = need.Loot;
                color = Settings.OverlayLootColor!.GetValue();
            }
            else
            {
                // handing in only makes sense for items in your own stash
                if (need.HandIn && viewType == EItemViewType.Inventory)
                {
                    color = Settings.OverlayHandInColor!.GetValue();
                    group = EWishlistGroup.Quests;
                    return true;
                }

                kind = need.Buy;
                color = Settings.OverlayBuyColor!.GetValue();
            }

            if (kind == NeedsSnapshot.EStrength.None)
            {
                return false;
            }

            strength = kind == NeedsSnapshot.EStrength.Now ? 1f : later;
            group = need.FromQuest && need.FromHideout ? EWishlistGroup.Other : need.FromQuest ? EWishlistGroup.Quests : EWishlistGroup.Hideout;
            return true;
        }

        private static void Style(WishlistGridView view, State state, Color color, float strength, EWishlistGroup group)
        {
            // native glow and border off: ours replace them (the game's icon stays, it knows where to sit)
            view.SetElementsActive(true);
            view._glow?.SetActive(false);
            view._border?.SetActive(false);

            EnsureFrame(view, state);

            state.Frame!.color = new Color(color.r, color.g, color.b, strength);
            state.Frame.pixelsPerUnitMultiplier = 2f / Settings.OverlayBorderThickness!.Value;
            state.Frame.gameObject.SetActive(true);

            state.Fill!.color = new Color(color.r, color.g, color.b, Settings.OverlayGlowStrength!.Value * strength);
            state.Fill.gameObject.SetActive(true);

            if (view._icon != null)
            {
                if (!state.IconSaved)
                {
                    state.IconSaved = true;
                    state.IconColor = view._icon.color;
                    state.IconScale = view._icon.transform.localScale;
                }

                view.SetIcon(group);
                view._icon.color = new Color(1f, 1f, 1f, strength);
                view._icon.transform.localScale = state.IconScale * Settings.OverlayIconScale!.Value;
                view._icon.gameObject.SetActive(true);
            }

            state.Modified = true;
        }

        private static void EnsureFrame(WishlistGridView view, State state)
        {
            if (state.Frame != null && state.Fill != null)
            {
                return;
            }

            // same geometry as the game's own border, in the same parent
            Transform parent = view._border != null ? view._border.transform.parent : view.transform;
            RectTransform? source = view._border != null ? view._border.transform as RectTransform : null;

            state.Fill = NewImage("AQC_Glow", parent, source, GlowSprite(), Image.Type.Sliced);
            state.Frame = NewImage("AQC_Frame", parent, source, FrameSprite(), Image.Type.Sliced);
        }

        private static Image NewImage(string name, Transform parent, RectTransform? source, Sprite? sprite, Image.Type type)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;

            if (source != null)
            {
                rect.anchorMin = source.anchorMin;
                rect.anchorMax = source.anchorMax;
                rect.pivot = source.pivot;
                rect.offsetMin = source.offsetMin;
                rect.offsetMax = source.offsetMax;
            }
            else
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = type;
            image.raycastTarget = false;
            return image;
        }

        private static Sprite? _glowSprite;

        // a soft glow along the inside of the edge, fading to nothing toward the middle (like the game's own selection glow); sliced, so it keeps its width at any item size
        private static Sprite GlowSprite()
        {
            if (_glowSprite != null)
            {
                return _glowSprite;
            }

            const int size = 32;
            const int reach = 12;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int distance = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                    float t = Mathf.Clamp01(1f - distance / (float)reach);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, t * t));
                }
            }

            texture.Apply();
            _glowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(reach, reach, reach, reach));
            return _glowSprite;
        }

        // a hollow square with a 2 px edge, sliced, so the border keeps its thickness at any item size
        private static Sprite FrameSprite()
        {
            if (_frameSprite != null)
            {
                return _frameSprite;
            }

            const int size = 8;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    texture.SetPixel(x, y, edge ? Color.white : Color.clear);
                }
            }

            texture.Apply();
            _frameSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
            return _frameSprite;
        }

        private static void Restore(WishlistGridView view, State state)
        {
            if (!state.Modified)
            {
                return;
            }

            state.Modified = false;

            if (state.Frame != null)
            {
                state.Frame.gameObject.SetActive(false);
            }

            if (state.Fill != null)
            {
                state.Fill.gameObject.SetActive(false);
            }

            if (view._icon != null && state.IconSaved)
            {
                view._icon.color = state.IconColor;
                view._icon.transform.localScale = state.IconScale;
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
            NeedsOverlay.OnChanged(__instance);
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
            NeedsOverlay.OnChanged(__instance);
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
            NeedsOverlay.OnChanged(__instance);
        }
    }
}
