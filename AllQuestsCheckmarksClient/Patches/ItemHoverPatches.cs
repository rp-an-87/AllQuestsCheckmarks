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

        // The checklist: only what you can act on, one line per source, current needs bright and future ones dim.
        // order of the lines: current upgrade, current quest, future upgrade, future quest
        private static readonly int[] _lineOrder = [NeedsSnapshot.HideoutNow, NeedsSnapshot.QuestNow, NeedsSnapshot.HideoutLater, NeedsSnapshot.QuestLater];

        private static string SourceName(int source)
        {
            return (source switch
            {
                NeedsSnapshot.HideoutNow => "aqc_src_hideout_now",
                NeedsSnapshot.QuestNow => "aqc_src_quest_now",
                NeedsSnapshot.HideoutLater => "aqc_src_hideout_later",
                _ => "aqc_src_quest_later",
            }).Localized(null);
        }

        private static string Checklist(string headerKey, int[] counts, string unit)
        {
            string result = "";

            foreach (int source in _lineOrder)
            {
                if (counts[source] <= 0)
                {
                    continue;
                }

                bool now = source == NeedsSnapshot.QuestNow || source == NeedsSnapshot.HideoutNow;
                string color = now ? "#E6E6E6" : "#8F8F8F";
                result += $"\n<size=85%><color={color}>  · {counts[source]}{unit} {string.Format("aqc_for".Localized(null), SourceName(source))}</color></size>";
            }

            return result.Length == 0 ? "" : "<size=85%><color=#B8B8B8>" + headerKey.Localized(null) + "</color></size>" + result;
        }

        // What to do with the item right now. Out of raid: hand in, buy, and what still has to be found in a raid. In raid: what to get
        private static string BuildAction(MongoID templateId)
        {
            if (!Settings.ActionLine!.Value || !NeedsSnapshot.TryGet(templateId, out NeedsSnapshot.ItemNeed need))
            {
                return "";
            }

            List<string> blocks = [];

            if (NeedsSnapshot.InRaid)
            {
                if (need.CollectorMissing)
                {
                    blocks.Add("aqc_action_collector".Localized(null));
                }
            }
            else
            {
                if (need.HandIn && need.HandInCount > 0)
                {
                    int[] handIn = new int[NeedsSnapshot.Sources];
                    handIn[NeedsSnapshot.QuestNow] = need.HandInCount;
                    blocks.Add(Checklist("aqc_header_handin", handIn, ""));
                }

                blocks.Add(Checklist("aqc_header_buy", need.BuyBy, ""));
            }

            // found in raid needs can't be bought: they are the same in a raid and in the hideout
            blocks.Add(Checklist("aqc_header_fir", need.FirBy, ""));
            blocks.Add(Checklist("aqc_header_unbuyable", need.UnbuyableBy, ""));

            return string.Join("\n", blocks.Where(b => b.Length > 0));
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
