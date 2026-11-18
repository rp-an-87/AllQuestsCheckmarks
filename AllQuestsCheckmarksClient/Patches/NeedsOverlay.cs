using AllQuestsCheckmarks.Helpers;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
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
            Both,
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
            public DashedBorder? Dashed;
            public bool IconSaved;
            public Color IconColor;
            public Vector3 IconScale;
        }

        // the requirement behind each requirement panel of the upgrade screen
        private static readonly ConditionalWeakTable<EFT.Hideout.ItemRequirementPanel, EFT.Hideout.ItemRequirement> _requirements = new();

        private static readonly ConditionalWeakTable<WishlistGridView, State> _states = new();
        private static readonly HashSet<WishlistGridView> _views = [];
        private static readonly Queue<WishlistGridView> _queue = new();
        private static readonly HashSet<WishlistGridView> _queued = [];
        private static bool _applying;
        private static bool _pumping;
        private static bool _refreshQueued;
        private static int _appliedVersion = -1;

        public static void EnablePatches()
        {
            new OverlaySetExaminedPatch().Enable();
            new OverlayAddPatch().Enable();
            new OverlayRemovePatch().Enable();
            new OverlayGroupPatch().Enable();
            new InventoryChangedPatch().Enable();
            new RequirementShowPatch().Enable();
        }

        public static void OnRequirementShown(EFT.Hideout.ItemRequirementPanel panel, EFT.Hideout.Requirement requirement)
        {
            _requirements.Remove(panel);

            if (requirement is EFT.Hideout.ItemRequirement itemRequirement)
            {
                _requirements.Add(panel, itemRequirement);
            }
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

                if (!Settings.UseOverlays!.Value || !state.Examined || !TryGetStyle(view, state, out Color color, out float strength, out EWishlistGroup group, out EIcon icon, out bool iconOnly, out bool isLater))
                {
                    Restore(view, state);
                    return;
                }

                Style(view, state, color, strength, group, icon, iconOnly, isLater);
            }
            finally
            {
                _applying = false;
            }
        }

        private static bool TryGetStyle(WishlistGridView view, State state, out Color color, out float strength, out EWishlistGroup group, out EIcon icon, out bool iconOnly, out bool isLater)
        {
            iconOnly = false;
            isLater = false;
            icon = EIcon.Group;
            color = default;
            strength = 1f;
            group = EWishlistGroup.Other;

            // the requirement icons of a hideout upgrade already say what is needed
            bool inRequirements = view.GetComponentInParent<EFT.Hideout.ItemRequirementPanel>() != null;

            if (inRequirements && !Settings.OverlayInHideoutRequirements!.Value)
            {
                return false;
            }

            // an upgrade's requirement icon: the overlay is about that requirement only, not about every upgrade and quest the item is needed for
            if (inRequirements)
            {
                return TryGetRequirementStyle(view, out color, out strength, out group, out icon, out iconOnly, out isLater);
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

            if (viewType == EItemViewType.Hideout && !inRequirements || !NeedsSnapshot.TryGet(_templateRef(view), out NeedsSnapshot.ItemNeed need))
            {
                return false;
            }

            float later = Settings.OverlayLaterStrength!.Value;
            NeedsSnapshot.EStrength kind;
            int[]? keepSources = null;

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

                // a copy in your stash that fills the upgrade you can start now: that is "keep, needed now", not the buy marker of the later needs
                if (Settings.KeepIcon!.Value && viewType == EItemViewType.Inventory)
                {
                    int heldNow = need.KeepAnyBy[NeedsSnapshot.HideoutNow] + (notFirCopy ? 0 : need.KeepFirBy[NeedsSnapshot.HideoutNow]);

                    if (heldNow > 0)
                    {
                        int[] keepNow = new int[NeedsSnapshot.Sources];
                        keepNow[NeedsSnapshot.HideoutNow] = heldNow;
                        keepSources = keepNow;
                        kind = NeedsSnapshot.EStrength.Now;
                        color = Settings.OverlayKeepColor!.GetValue();
                        iconOnly = true;
                    }
                }
            }

            if (kind == NeedsSnapshot.EStrength.None)
            {
                // in raid: nothing to loot, but something you can simply buy: just the icon, in the can buy color, no border
                if (NeedsSnapshot.InRaid && Settings.RaidBuyIcon!.Value && need.Buy != NeedsSnapshot.EStrength.None)
                {
                    kind = need.Buy;
                    color = Settings.OverlayBuyColor!.GetValue();
                    iconOnly = true;
                }
                else if (!NeedsSnapshot.InRaid && Settings.KeepIcon!.Value && viewType == EItemViewType.Inventory)
                {
                    // in your stash: copies earmarked for a later need, so they do not get sold. A non-FiR copy is only kept for needs that take any copy
                    int[] keep = new int[NeedsSnapshot.Sources];
                    for (int i = 0; i < keep.Length; i++)
                    {
                        keep[i] = need.KeepAnyBy[i] + (notFirCopy ? 0 : need.KeepFirBy[i]);
                    }

                    if (keep.Sum() == 0)
                    {
                        return false;
                    }

                    keepSources = keep;
                    kind = keep[NeedsSnapshot.HideoutNow] > 0 ? NeedsSnapshot.EStrength.Now : NeedsSnapshot.EStrength.Later;
                    color = Settings.OverlayKeepColor!.GetValue();
                    iconOnly = true;
                }
                else
                {
                    return false;
                }
            }

            isLater = kind != NeedsSnapshot.EStrength.Now;
            strength = isLater ? later : 1f;
            // the icon says why: only the sources that are actually missing and that you can act on (in raid what to loot, out of raid what to buy)
            int[] missing = keepSources ?? (NeedsSnapshot.InRaid && !iconOnly ? need.LootBy : need.BuyBy);
            bool withFuture = keepSources != null || NeedsSnapshot.InRaid && !iconOnly || Settings.BuyFutureNeeds!.Value;
            bool fromQuest = missing[NeedsSnapshot.QuestNow] > 0 || withFuture && missing[NeedsSnapshot.QuestLater] > 0;
            bool fromHideout = missing[NeedsSnapshot.HideoutNow] > 0 || withFuture && missing[NeedsSnapshot.HideoutLater] > 0;

            if (!fromQuest && !fromHideout)
            {
                fromQuest = need.FromQuest;
                fromHideout = need.FromHideout;
            }

            group = fromQuest && fromHideout ? EWishlistGroup.Other : fromQuest ? EWishlistGroup.Quests : EWishlistGroup.Hideout;
            icon = fromQuest && fromHideout ? EIcon.Both : EIcon.Group;
            return true;
        }

        private static bool TryGetRequirementStyle(WishlistGridView view, out Color color, out float strength, out EWishlistGroup group, out EIcon icon, out bool iconOnly, out bool isLater)
        {
            color = default;
            strength = 1f;
            group = EWishlistGroup.Hideout;
            icon = EIcon.Group;
            iconOnly = false;
            isLater = false;

            EFT.Hideout.ItemRequirementPanel? panel = view.GetComponentInParent<EFT.Hideout.ItemRequirementPanel>();

            if (panel == null || !_requirements.TryGetValue(panel, out EFT.Hideout.ItemRequirement requirement))
            {
                return false;
            }

            // what is still missing for THIS upgrade (the game's own count of what you hold)
            int missing = requirement.IntCount - Mathf.Clamp(requirement.UserItemsCount, 0, requirement.IntCount);

            if (missing <= 0)
            {
                return false;
            }

            bool inRaid = NeedsSnapshot.InRaid;
            bool buyable = NeedsSnapshot.IsBuyable(new MongoID(requirement.TemplateId));

            if (requirement.IsSpawnedInSession)
            {
                // found in raid can't be bought: something to loot in raid, nothing to do out of raid
                if (!inRaid)
                {
                    return false;
                }

                color = Settings.OverlayLootColor!.GetValue();
                return true;
            }

            if (!inRaid)
            {
                if (!buyable)
                {
                    return false;
                }

                color = Settings.OverlayBuyColor!.GetValue();
                return true;
            }

            if (!buyable)
            {
                color = Settings.OverlayLootColor!.GetValue();
                return true;
            }

            if (!Settings.RaidBuyIcon!.Value)
            {
                return false;
            }

            color = Settings.OverlayBuyColor!.GetValue();
            iconOnly = true;
            return true;
        }

        private static void Style(WishlistGridView view, State state, Color color, float strength, EWishlistGroup group, EIcon icon, bool iconOnly, bool isLater)
        {
            view.SetElementsActive(true);

            // a need for later: a dashed border instead of the solid one; just an icon: no border or glow
            bool dashed = !iconOnly && isLater && Settings.OverlayDashedLater!.Value;

            // the game's own border and glow in the overlay color
            NativePart(view._border, state, !dashed && !iconOnly, color, strength * Settings.OverlayBorderMultiplier!.Value);
            NativePart(view._glow, state, !iconOnly, color, strength * Settings.OverlayGlowMultiplier!.Value);
            ShowDashed(view, state, dashed, color);

            if (view._icon != null)
            {
                if (!state.IconSaved)
                {
                    state.IconSaved = true;
                    state.IconColor = view._icon.color;
                    state.IconScale = view._icon.transform.localScale;
                }

                Sprite? custom = icon switch
                {
                    EIcon.HandIn => HandInSprite(),
                    EIcon.Collector => OverlayIcons.Collector,
                    EIcon.Both => BothSprite(),
                    _ => null,
                };

                if (custom == null)
                {
                    view.SetIcon(group);
                }
                else
                {
                    view._icon.sprite = custom;
                    view._icon.preserveAspect = true;
                }

                // the icon has the color of the overlay (white when it has none: the game's own look)
                Color iconColor = Settings.OverlayIconMatchColor!.Value ? new Color(color.r, color.g, color.b, 1f) : Color.white;
                view._icon.color = new Color(iconColor.r, iconColor.g, iconColor.b, strength);
                view._icon.transform.localScale = state.IconScale * Settings.OverlayIconScale!.Value;
                view._icon.gameObject.SetActive(true);
            }

            state.Modified = true;
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

            bool tint = color != Color.white;

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

        // The hand in icon is the game's "bring an item" quest icon, the same one CustomRpTweaks puts on its hand in button
        private static Sprite? _handIn;
        private static bool _handInTried;

        private static Sprite HandInSprite()
        {
            if (!_handInTried)
            {
                _handInTried = true;

                try
                {
                    _handIn = EFTHardSettings.Instance.StaticIcons.QuestIconTypeSprites[EQuestIconType.PickUp];
                }
                catch (System.Exception)
                {
                    _handIn = null;
                }
            }

            return _handIn != null ? _handIn : OverlayIcons.HandIn;
        }

        private static Sprite? _both;
        private static bool _bothTried;

        // The hideout icon and the quest icon in one: hideout on the upper left, quest on the lower right, split by a thin diagonal gap
        private static Sprite? BothSprite()
        {
            if (_bothTried)
            {
                return _both;
            }

            _bothTried = true;

            try
            {
                var icons = EFTHardSettings.Instance.StaticIcons.WishlistSpritesOutline;
                Sprite hideout = icons[EWishlistGroup.Hideout];
                Sprite quest = icons[EWishlistGroup.Quests];

                int width = Mathf.RoundToInt(hideout.textureRect.width);
                int height = Mathf.RoundToInt(hideout.textureRect.height);
                Color32[] upper = ReadPixels(hideout, width, height);
                Color32[] lower = ReadPixels(quest, width, height);
                Color32[] result = new Color32[width * height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // the diagonal runs from the lower left to the upper right corner
                        float side = (float)y / height - (float)x / width;
                        int i = y * width + x;

                        if (Mathf.Abs(side) < 0.05f)
                        {
                            result[i] = new Color32(0, 0, 0, 0);
                        }
                        else
                        {
                            result[i] = side > 0 ? upper[i] : lower[i];
                        }
                    }
                }

                Texture2D texture = new(width, height, TextureFormat.RGBA32, false) { filterMode = hideout.texture.filterMode, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels32(result);
                texture.Apply();
                _both = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), hideout.pixelsPerUnit);
            }
            catch (System.Exception e)
            {
                Plugin.LogSource?.LogWarning($"Could not make the combined hideout / quest icon, using the game's 'other' icon: {e.Message}");
                _both = null;
            }

            return _both;
        }

        // The pixels of a sprite, scaled to width x height (the game's textures are not readable, so through a render texture)
        private static Color32[] ReadPixels(Sprite source, int width, int height)
        {
            Texture2D texture = source.texture;
            Rect rect = source.textureRect;

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
            UnityEngine.Object.Destroy(copy);
            return pixels;
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

        // The dashed border: one mesh in the same place as the game's own border (see DashedBorder)
        private static void ShowDashed(WishlistGridView view, State state, bool show, Color color)
        {
            if (!show)
            {
                if (state.Dashed != null)
                {
                    state.Dashed.gameObject.SetActive(false);
                }

                return;
            }

            if (state.Dashed == null)
            {
                Transform parent = view._border != null ? view._border.transform.parent : view.transform;
                RectTransform? source = view._border != null ? view._border.transform as RectTransform : null;

                GameObject go = new("AQC_Dashed", typeof(RectTransform));
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

                state.Dashed = go.AddComponent<DashedBorder>();
                state.Dashed.raycastTarget = false;
            }

            state.Dashed.Configure(
                new Color(color.r, color.g, color.b, Mathf.Clamp01(Settings.OverlayDashedStrength!.Value)),
                Settings.OverlayDashLength!.Value,
                Settings.OverlayDashGap!.Value,
                Mathf.Max(1f, Settings.OverlayBorderThickness!.Value),
                1f - Settings.OverlayDashFade!.Value,
                Settings.OverlayDashCorner!.Value);

            state.Dashed.gameObject.SetActive(true);
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

            if (state.Dashed != null)
            {
                state.Dashed.gameObject.SetActive(false);
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
    // Remembers which requirement each requirement panel of the upgrade screen shows
    internal class RequirementShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.Hideout.ItemRequirementPanel), nameof(EFT.Hideout.ItemRequirementPanel.Show));
        }

        [PatchPostfix]
        private static void Postfix(EFT.Hideout.ItemRequirementPanel __instance, EFT.Hideout.Requirement requirement)
        {
            NeedsOverlay.OnRequirementShown(__instance, requirement);
        }
    }

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
