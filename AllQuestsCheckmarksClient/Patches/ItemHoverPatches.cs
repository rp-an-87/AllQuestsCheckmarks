using AllQuestsCheckmarks.Helpers;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using EFT;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
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

        // The action line depends on what you hold right now, so the tooltip text carries a token that is replaced when it is shown
        public const string ActionToken = "\uE000";

        private static readonly ConditionalWeakTable<QuestItemViewPanel, StrongBox<MongoID>> _items = new();

        public static void SetItem(QuestItemViewPanel panel, MongoID templateId)
        {
            _items.Remove(panel);
            _items.Add(panel, new StrongBox<MongoID>(templateId));
        }

        // Replaces the token in the panel's tooltip text with the current action line (a separator and a small line), or removes it
        public static string Resolve(QuestItemViewPanel panel, string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains(ActionToken))
            {
                return text;
            }

            string action = _items.TryGetValue(panel, out StrongBox<MongoID> id) ? BuildAction(id.Value) : "";
            return text.Replace(ActionToken, action.Length > 0 ? "\n" + TooltipSeparator.Line + "\n" + action : "");
        }

        private static string Reasons(NeedsSnapshot.EReason reasons)
        {
            List<string> names = [];

            if (reasons.HasFlag(NeedsSnapshot.EReason.QuestNow)) names.Add("aqc_reason_quest_now".Localized(null));
            if (reasons.HasFlag(NeedsSnapshot.EReason.HideoutNow)) names.Add("aqc_reason_hideout_now".Localized(null));
            if (reasons.HasFlag(NeedsSnapshot.EReason.QuestLater)) names.Add("aqc_reason_quest_later".Localized(null));
            if (reasons.HasFlag(NeedsSnapshot.EReason.HideoutLater)) names.Add("aqc_reason_hideout_later".Localized(null));

            return names.Count switch
            {
                0 => "",
                1 => names[0],
                _ => string.Join(", ", names.GetRange(0, names.Count - 1)) + "aqc_and".Localized(null) + names[names.Count - 1],
            };
        }

        // What to do with the item right now: loot it in raid, hand in / buy it out of raid
        private static string BuildAction(MongoID templateId)
        {
            if (!Settings.ActionLine!.Value || !NeedsSnapshot.TryGet(templateId, out NeedsSnapshot.ItemNeed need))
            {
                return "";
            }

            if (NeedsSnapshot.InRaid)
            {
                if (need.CollectorMissing)
                {
                    return "aqc_action_collector".Localized(null);
                }

                return need.LootCount > 0 ? string.Format("aqc_action_loot".Localized(null), need.LootCount, Reasons(need.LootReasons)) : "";
            }

            if (need.HandIn && need.HandInCount > 0)
            {
                return string.Format("aqc_action_handin".Localized(null), need.HandInCount, Reasons(NeedsSnapshot.EReason.QuestNow));
            }

            if (need.BuyCount > 0)
            {
                return string.Format("aqc_action_buy".Localized(null), need.BuyCount, Reasons(need.BuyReasons));
            }

            // FiR needs can't be bought: out of raid they are still waiting to be found
            return need.LootCount > 0 ? string.Format("aqc_action_find".Localized(null), need.LootCount, Reasons(need.LootReasons)) : "";
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

            string text = Resolve(panel, _textRef(panel));
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

        public static void OnCheckmarkHover(QuestItemViewPanel panel)
        {
            _pending = null;
            _fromCheckmark = true;

            // the panel shows its own text: give it the current action line
            _textRef(panel) = Resolve(panel, _textRef(panel));
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
        private static void Prefix(QuestItemViewPanel __instance)
        {
            ItemHoverTooltip.OnCheckmarkHover(__instance);
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
