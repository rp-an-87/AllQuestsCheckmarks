using AllQuestsCheckmarks.Helpers;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using EFT;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ZGFueDkx.ZGCLib.Config;
using ZGFueDkx.ZGCLib.helpers;

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

        // the stash and needed lines depend on what you hold (and carry) right now too
        public const string StockToken = "\uE001";

        // the tooltip text as built, with its tokens: every hover starts from it (a text that already had its tokens replaced would show old numbers)
        private static readonly ConditionalWeakTable<QuestItemViewPanel, StrongBox<string>> _templates = new();

        // the block of later needs (future quests and upgrades): left out in raid when the setting says so, decided when the tooltip is shown
        public const string LaterToken = "\uE002";

        private static readonly ConditionalWeakTable<QuestItemViewPanel, StrongBox<string>> _later = new();

        public static void SetLater(QuestItemViewPanel panel, string block)
        {
            _later.Remove(panel);
            _later.Add(panel, new StrongBox<string>(block));
        }

        public static void SetTemplate(QuestItemViewPanel panel, string text)
        {
            _templates.Remove(panel);
            _templates.Add(panel, new StrongBox<string>(text));
        }

        private static readonly ConditionalWeakTable<QuestItemViewPanel, StrongBox<MongoID>> _items = new();

        public static void SetItem(QuestItemViewPanel panel, MongoID templateId)
        {
            _items.Remove(panel);
            _items.Add(panel, new StrongBox<MongoID>(templateId));
        }

        // Replaces the token in the panel's tooltip text with the current action line (a separator and a small line), or removes it
        public static string Resolve(QuestItemViewPanel panel, string text)
        {
            if (_templates.TryGetValue(panel, out StrongBox<string> template))
            {
                text = template.Value;
            }

            if (string.IsNullOrEmpty(text) || !text.Contains(ActionToken) && !text.Contains(StockToken) && !text.Contains(LaterToken))
            {
                return text;
            }

            if (text.Contains(LaterToken))
            {
                bool hide = Settings.HideLaterInRaid!.Value && NeedsSnapshot.InRaid;
                string block = !hide && _later.TryGetValue(panel, out StrongBox<string> later) ? later.Value : "";

                // nothing else in the sections: the separator in front of them goes too
                text = hide ? text.Replace("\n" + TooltipSeparator.Line + LaterToken, "") : text;
                text = text.Replace(LaterToken, block);
            }

            bool known = _items.TryGetValue(panel, out StrongBox<MongoID> id);
            string action = known ? BuildAction(id.Value) : "";
            string stock = known ? StockLines.Build(id.Value) : "";

            return text.Replace(StockToken, stock).Replace(ActionToken, action.Length > 0 ? "\n" + TooltipSeparator.Line + "\n" + action : "");
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

        // the header gets the color of the overlay it belongs to
        private static string Hex(UnityEngine.Color color) => "#" + UnityEngine.ColorUtility.ToHtmlStringRGB(color);

        private static string Checklist(string headerKey, int[] counts, string unit, UnityEngine.Color headerColor)
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
                result += $"\n<size=85%><color={color}>  · {NumberFormat.Compact(counts[source])}{unit} {string.Format("aqc_for".Localized(null), SourceName(source))}</color></size>";
            }

            return result.Length == 0 ? "" : "<size=85%><color=" + Hex(headerColor) + ">" + headerKey.Localized(null) + "</color></size>" + result;
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
                    blocks.Add(Checklist("aqc_header_handin", handIn, "", Settings.OverlayHandInColor!.GetValue()));
                }

                // copies earmarked for needs: do not sell them
                int[] keep = new int[NeedsSnapshot.Sources];
                for (int i = 0; i < keep.Length; i++)
                {
                    keep[i] = need.KeepFirBy[i] + need.KeepAnyBy[i];
                }

                blocks.Add(Checklist("aqc_header_keep", keep, "", Settings.OverlayKeepColor!.GetValue()));
                blocks.Add(Checklist("aqc_header_buy", need.BuyBy, "", Settings.OverlayBuyColor!.GetValue()));
            }

            // found in raid needs can't be bought: they are the same in a raid and in the hideout
            blocks.Add(Checklist("aqc_header_fir", need.FirBy, "", Settings.OverlayLootColor!.GetValue()));
            blocks.Add(Checklist("aqc_header_unbuyable", need.UnbuyableBy, "", Settings.OverlayLootColor!.GetValue()));

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
