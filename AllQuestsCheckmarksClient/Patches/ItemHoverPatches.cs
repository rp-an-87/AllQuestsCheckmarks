using AllQuestsCheckmarks.Helpers;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AllQuestsCheckmarks.Patches
{
    /// <summary>
    /// Shows the checkmark tooltip text under the item tooltip when hovering the item itself.
    /// The text is the one QuestItemViewPanelPatch already built for the checkmark, so both always match.
    /// </summary>
    internal static class ItemHoverTooltip
    {
        private static readonly AccessTools.FieldRef<QuestItemViewPanel, string> _textRef =
            AccessTools.FieldRefAccess<QuestItemViewPanel, string>("_tooltipText");

        private static readonly ConditionalWeakTable<QuestItemViewPanel, object> _relevant = new();

        // Remembers whether the item needs a tooltip, independent of whether a checkmark is drawn
        public static void SetRelevant(QuestItemViewPanel panel, bool relevant)
        {
            _relevant.Remove(panel);

            if (relevant)
            {
                _relevant.Add(panel, new object());
            }
        }

        private static string? _pending;
        private static bool _fromCheckmark;

        public static void OnItemShowTooltip(GridItemView item)
        {
            _pending = null;

            if (!Settings.HoverItemTooltip!.Value)
            {
                return;
            }

            QuestItemViewPanel panel = item.QuestsItemViewPanel;
            if (panel == null || !panel.gameObject.activeSelf && !_relevant.TryGetValue(panel, out _))
            {
                return;
            }

            string text = _textRef(panel);
            if (!string.IsNullOrEmpty(text))
            {
                _pending = text;
            }
        }

        public static void OnItemExit()
        {
            _pending = null;
            _fromCheckmark = false;
        }

        public static void OnCheckmarkHover()
        {
            _pending = null;
            _fromCheckmark = true;
        }

        public static void OnTooltipShow(ref string text)
        {
            if (_fromCheckmark)
            {
                _fromCheckmark = false;
                return;
            }

            if (_pending is null)
            {
                return;
            }

            text += "\n" + _pending;
            _pending = null;
        }
    }

    internal class ItemHoverShowTooltipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GridItemView), nameof(GridItemView.ShowTooltip));
        }

        [PatchPrefix]
        private static void Prefix(GridItemView __instance)
        {
            ItemHoverTooltip.OnItemShowTooltip(__instance);
        }
    }

    internal class ItemHoverExitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GridItemView), nameof(GridItemView.OnPointerExit));
        }

        [PatchPrefix]
        private static void Prefix()
        {
            ItemHoverTooltip.OnItemExit();
        }
    }

    // Hovering the checkmark shows its own tooltip, don't add the text a second time
    internal class CheckmarkHoverPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestItemViewPanel), nameof(QuestItemViewPanel.CG_Awake));
        }

        [PatchPrefix]
        private static void Prefix()
        {
            ItemHoverTooltip.OnCheckmarkHover();
        }
    }

    internal class SimpleTooltipShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(SimpleTooltip), nameof(SimpleTooltip.Show));
        }

        [PatchPrefix]
        private static void Prefix(ref string text)
        {
            ItemHoverTooltip.OnTooltipShow(ref text);
        }
    }
}
