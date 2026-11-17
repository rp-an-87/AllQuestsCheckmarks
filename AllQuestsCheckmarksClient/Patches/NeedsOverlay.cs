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
        // which picture the overlay icon shows: the game's own for the group (hideout, quest, both), or one drawn by this mod
        private enum EIcon
        {
            Group,
            HandIn,
            Collector,
        }

        private static readonly AccessTools.FieldRef<WishlistGridView, MongoID> _templateRef =
            AccessTools.FieldRefAccess<WishlistGridView, MongoID>("_templateId");

        // what a view looked like before we touched it, so it can be put back when the item view is reused
        private class State
        {
            public bool Examined = true;
            public bool Modified;
            public int Retries;
            public readonly Dictionary<Graphic, Color> Colors = [];
            public readonly Dictionary<Image, Sprite> Sprites = [];
            public Image? Frame;
            public Image? Clock;
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

                Plugin.FrameMs += watch.Elapsed.TotalMilliseconds;

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

                if (!Settings.UseOverlays!.Value || !state.Examined || !TryGetStyle(view, state, out Color color, out float strength, out EWishlistGroup group, out EIcon icon))
                {
                    Restore(view, state);
                    return;
                }

                Style(view, state, color, strength, group, icon);
            }
            finally
            {
                _applying = false;
            }
        }

        private static bool TryGetStyle(WishlistGridView view, State state, out Color color, out float strength, out EWishlistGroup group, out EIcon icon)
        {
            icon = EIcon.Group;
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

            // a copy that is not found in raid can't fill a need for found in raid items
            bool notFirCopy = itemView?.Item is { MarkedAsSpawnedInSession: false };

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
                    // the Collector wants found in raid copies: nothing to do with a non-FiR one
                    if (notFirCopy)
                    {
                        return false;
                    }

                    group = EWishlistGroup.Other;
                    icon = EIcon.Collector;
                    color = Settings.OverlayCollectorColor!.GetValue();
                    return true;
                }

                kind = need.Loot;
                color = Settings.OverlayLootColor!.GetValue();

                // missing found in raid copies only: a non-FiR copy is no use for those, so there is nothing to do with it
                if (kind != NeedsSnapshot.EStrength.None && notFirCopy && need.FirBy.Sum() > 0 && need.UnbuyableBy.Sum() == 0)
                {
                    return false;
                }
            }
            else
            {
                // handing in only makes sense for items in your own stash
                if (need.HandIn && viewType == EItemViewType.Inventory && (!notFirCopy || need.HandInAnyCopy))
                {
                    color = Settings.OverlayHandInColor!.GetValue();
                    group = EWishlistGroup.Quests;
                    icon = EIcon.HandIn;
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

        private static void Style(WishlistGridView view, State state, Color color, float strength, EWishlistGroup group, EIcon icon)
        {
            view.SetElementsActive(true);

            Settings.EOverlayPart borderStyle = Settings.OverlayBorderStyle!.Value;
            Settings.EOverlayPart glowStyle = Settings.OverlayGlowStyle!.Value;
            float borderAlpha = strength * Settings.OverlayBorderMultiplier!.Value;
            float glowAlpha = strength * Settings.OverlayGlowMultiplier!.Value;

            // border and glow: the game's own (tinted), ours, or none
            NativePart(view._border, state, borderStyle == Settings.EOverlayPart.Native, color, borderAlpha);
            NativePart(view._glow, state, glowStyle == Settings.EOverlayPart.Native, color, glowAlpha);

            if (borderStyle == Settings.EOverlayPart.Own || glowStyle == Settings.EOverlayPart.Own)
            {
                EnsureFrame(view, state);
            }

            if (state.Frame != null)
            {
                bool on = borderStyle == Settings.EOverlayPart.Own;
                state.Frame.gameObject.SetActive(on);

                if (on)
                {
                    state.Frame.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(borderAlpha));
                    state.Frame.pixelsPerUnitMultiplier = 2f / Settings.OverlayBorderThickness!.Value;
                }
            }

            if (state.Fill != null)
            {
                bool on = glowStyle == Settings.EOverlayPart.Own;
                state.Fill.gameObject.SetActive(on);

                if (on)
                {
                    state.Fill.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(0.55f * glowAlpha));
                }
            }

            if (view._icon != null)
            {
                if (!state.IconSaved)
                {
                    state.IconSaved = true;
                    state.IconColor = view._icon.color;
                    state.IconScale = view._icon.transform.localScale;
                }

                if (icon == EIcon.Group)
                {
                    view.SetIcon(group);
                }
                else
                {
                    view._icon.sprite = icon == EIcon.HandIn ? OverlayIcons.HandIn : OverlayIcons.Collector;
                    view._icon.preserveAspect = true;
                }

                view._icon.color = new Color(1f, 1f, 1f, strength);
                view._icon.transform.localScale = state.IconScale * Settings.OverlayIconScale!.Value;
                view._icon.gameObject.SetActive(true);

                ShowClock(view, state, strength < 1f && icon == EIcon.Group);
            }

            state.Modified = true;
        }

        // A small clock next to the icon when the need is for later. It is a child of the icon, so it moves and scales with it
        private static void ShowClock(WishlistGridView view, State state, bool show)
        {
            if (!show)
            {
                if (state.Clock != null)
                {
                    state.Clock.gameObject.SetActive(false);
                }

                return;
            }

            if (state.Clock == null)
            {
                GameObject go = new("AQC_Clock", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(view._icon.transform, false);

                RectTransform rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(1f, 0f);

                Image image = go.GetComponent<Image>();
                image.sprite = OverlayIcons.Clock;
                image.preserveAspect = true;
                image.raycastTarget = false;
                state.Clock = image;
            }

            RectTransform iconRect = (RectTransform)view._icon.transform;
            float side = Mathf.Max(8f, Mathf.Min(iconRect.rect.width, iconRect.rect.height) * 0.6f);
            state.Clock.rectTransform.sizeDelta = new Vector2(side, side);
            state.Clock.color = Color.white;
            state.Clock.gameObject.SetActive(true);
        }

        // The game's own border or glow: shown or hidden, and when shown tinted with the overlay color and scaled in strength
        private static void NativePart(GameObject? part, State state, bool show, Color color, float alpha)
        {
            if (part == null)
            {
                return;
            }

            part.SetActive(show);

            if (!show)
            {
                return;
            }

            bool tint = Settings.OverlayTintNative!.Value && color != Color.white;

            foreach (Graphic graphic in part.GetComponentsInChildren<Graphic>(true))
            {
                if (!state.Colors.TryGetValue(graphic, out Color original))
                {
                    original = graphic.color;
                    state.Colors[graphic] = original;
                }

                // the game's pictures are green: a color only multiplies with them (gold turns olive). A white copy of the same picture takes any color exactly
                bool recolored = false;

                if (graphic is Image image && image.sprite != null)
                {
                    if (!state.Sprites.TryGetValue(image, out Sprite originalSprite))
                    {
                        originalSprite = image.sprite;
                        state.Sprites[image] = originalSprite;
                    }

                    Sprite? white = tint ? WhiteCopy(originalSprite) : null;
                    image.sprite = white != null ? white : originalSprite;
                    recolored = white != null;
                }

                bool exact = tint && recolored;
                graphic.color = new Color(tint ? color.r : original.r, tint ? color.g : original.g, tint ? color.b : original.b,
                    Mathf.Clamp01((exact ? 1f : original.a) * alpha));
            }
        }

        private static readonly Dictionary<Sprite, Sprite?> _whiteCopies = [];

        // The same picture (shape and fade) in white, so that a color applied to it comes out as that color. Made once per sprite
        private static Sprite? WhiteCopy(Sprite source)
        {
            if (_whiteCopies.TryGetValue(source, out Sprite? cached))
            {
                return cached;
            }

            Sprite? result = null;

            try
            {
                Texture2D texture = source.texture;
                Rect rect = source.textureRect;
                int width = Mathf.RoundToInt(rect.width);
                int height = Mathf.RoundToInt(rect.height);

                // read the sprite's part of the texture back through a render texture (the game's textures are not readable)
                RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(texture, target,
                    new Vector2(rect.width / texture.width, rect.height / texture.height),
                    new Vector2(rect.x / texture.width, rect.y / texture.height));

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                Texture2D copy = new(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);

                Color32[] pixels = copy.GetPixels32();
                byte minAlpha = 255;
                byte maxAlpha = 0;

                foreach (Color32 pixel in pixels)
                {
                    minAlpha = pixel.a < minAlpha ? pixel.a : minAlpha;
                    maxAlpha = pixel.a > maxAlpha ? pixel.a : maxAlpha;
                }

                // the shape is in the alpha channel when that varies, otherwise in the brightness
                bool useAlpha = maxAlpha - minAlpha > 25;

                for (int i = 0; i < pixels.Length; i++)
                {
                    byte a = useAlpha ? pixels[i].a : (byte)Mathf.Max(pixels[i].r, Mathf.Max(pixels[i].g, pixels[i].b));
                    pixels[i] = new Color32(255, 255, 255, a);
                }

                copy.SetPixels32(pixels);
                copy.filterMode = texture.filterMode;
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.Apply();

                result = Sprite.Create(copy, new Rect(0, 0, width, height), new Vector2(source.pivot.x / width, source.pivot.y / height),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
            }
            catch (System.Exception e)
            {
                Plugin.LogSource?.LogWarning($"Could not make a white copy of the overlay sprite {source.name}: {e.Message}");
            }

            _whiteCopies[source] = result;
            return result;
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

            foreach (KeyValuePair<Graphic, Color> entry in state.Colors)
            {
                if (entry.Key != null)
                {
                    entry.Key.color = entry.Value;
                }
            }

            foreach (KeyValuePair<Image, Sprite> entry in state.Sprites)
            {
                if (entry.Key != null)
                {
                    entry.Key.sprite = entry.Value;
                }
            }

            if (state.Clock != null)
            {
                state.Clock.gameObject.SetActive(false);
            }

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
