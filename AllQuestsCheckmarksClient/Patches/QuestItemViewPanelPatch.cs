using AllQuestsCheckmarks.Helpers;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using JetBrains.Annotations;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZGFueDkx.ZGCLib.Config;

namespace AllQuestsCheckmarks.Patches
{
    internal class QuestItemViewPanelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestItemViewPanel), nameof(QuestItemViewPanel.Show));
        }

        // Item icons of hideout upgrade requirements are created with the Hideout view type
        private static bool IsHideoutView(QuestItemViewPanel panel)
        {
            // by place (the requirement panels of an upgrade) or by view type (created through the hideout item view factory)
            bool inRequirementPanel = panel.GetComponentInParent<EFT.Hideout.ItemRequirementPanel>() != null;
            ItemView? view = panel.GetComponentInParent<ItemView>();
            EItemViewType? viewType = view?.ItemContext?.ViewType;

            return inRequirementPanel || viewType == EItemViewType.Hideout;
        }

        private static string FirLabel(bool fir)
        {
            string text = (fir ? "aqc_fir" : "aqc_nonfir").Localized(null);

            if (!Settings.CompactFirLabels!.Value)
            {
                return text;
            }

            return fir ? $"<size=80%><color=#FFFF66>{text}</color></size>" : $"<size=80%><color=#8A8A8A>{text}</color></size>";
        }

        [PatchPrefix]
        static bool Prefix(Profile profile, Item item, [CanBeNull] SimpleTooltip tooltip, QuestItemViewPanel __instance, Image ____questIconImage,
            Sprite ____foundInRaidSprite, Sprite ____questItemSprite, ref string ____tooltipText, ref SimpleTooltip ____tooltip, TextMeshProUGUI ____questItemLabel)
        {
            __instance.HideGameObject();

            if (____questItemLabel != null)
            {
                ____questItemLabel.gameObject.SetActive(item.QuestItem);
                if (item.QuestItem)
                {
                    ____questItemLabel.text = "QUEST ITEM".Localized(null);
                }
            }

            ____tooltip = tooltip;
            ____tooltipText = "";
            ItemHoverTooltip.SetItem(__instance, item.TemplateId);

            bool separated = Settings.SeparatedTooltip!.Value;
            string foundInRaidLine = "";

            if (item.MarkedAsSpawnedInSession)
            {
                foundInRaidLine = separated
                    ? "<size=" + (Settings.CompactFirLabels!.Value ? "80" : "100") + "%><color=#66FF66>" + "Item found in raid".Localized(null) + "</color></size>"
                    : "Item found in raid".Localized(null);
            }

            bool showNonFir = Settings.IncludeNonFir!.Value;
            StashHelper.ItemsCount inStash = NeedsSnapshot.Held(item.TemplateId);
            // filled in when the tooltip is shown (what you hold and carry changes)
            string stashLine = ItemHoverTooltip.StockToken;

            bool neededForActive = false;
            bool neededForFuture = false;
            bool neededForFriend = false;
            bool neededForHideout = false;
            bool collectorOnly = true;

            int totalNeededFir = 0;
            int totalNeededNonFir = 0;
            int handedOverFir = 0;
            int handedOverNonFir = 0;

            string activeQuestsTooltip = "";
            string futureQuestsTooltip = "";

            bool useCustomTextColors = Settings.CustomTextColors!.Value;
            string indent = Settings.BulletPoints!.Value ? "  · " : "  ";

            // quest conditions that several items share one count for: noted on the quest line, and kept out of "Total needed"
            // (what you hold of the others counts too, so the per item numbers would mislead)
            IReadOnlyList<NeedsSnapshot.PoolInfo> pools = NeedsSnapshot.PoolsFor(item.TemplateId);

            bool IsPooled(MongoID questId) => pools.Any(p => p.QuestId == questId);

            string PoolNote(MongoID questId)
            {
                foreach (NeedsSnapshot.PoolInfo pool in pools)
                {
                    if (pool.QuestId == questId)
                    {
                        return "\n    " + string.Format("aqc_pool_note".Localized(null), pool.TargetCount, NumberFormat.Compact(pool.Held));
                    }
                }

                return "";
            }

            if (QuestsHelper.GetActiveQuestsWithItem(profile, item, out Dictionary<MongoID, QuestsHelper.CurrentQuest> activeQuests,
                out Dictionary<MongoID, QuestsHelper.CurrentQuest> fulfilled))
            {
                string activeColor = Settings.ActiveQuestTextColor!.GetHexColor(!useCustomTextColors);

                foreach (KeyValuePair<MongoID, QuestsHelper.CurrentQuest> keyValuePair in activeQuests)
                {
                    keyValuePair.Deconstruct(out _, out QuestsHelper.CurrentQuest quest);

                    if (showNonFir || item.QuestItem || quest.Condition.onlyFoundInRaid)
                    {
                        if (!neededForActive)
                        {
                            neededForActive = true;
                            activeQuestsTooltip += "\n" + "aqc_active_quests".Localized(null);
                        }
                    }
                    else
                    {
                        continue;
                    }

                    activeQuestsTooltip += $"\n{indent}<color={activeColor}>{quest.Template.Name}</color>: ";

                    if (quest.Condition is ConditionHandoverItem condition
                        && profile.TaskConditionCounters.TryGetValue(condition.id, out TaskConditionCounter counter))
                    {
                        if (condition.onlyFoundInRaid)
                        {
                            handedOverFir += counter.Value;
                            if (!IsPooled(quest.Template.Id)) totalNeededFir += (int)condition.value - counter.Value;
                        }
                        else
                        {
                            handedOverNonFir += counter.Value;
                            if (!IsPooled(quest.Template.Id)) totalNeededNonFir += (int)condition.value - counter.Value;
                        }

                        activeQuestsTooltip += $"{NumberFormat.Compact(counter.Value)}/{NumberFormat.Compact((int)condition.value)}";

                        if (showNonFir)
                        {
                            activeQuestsTooltip += " " + FirLabel(condition.onlyFoundInRaid);
                        }
                    }
                    else
                    {
                        activeQuestsTooltip += $"0/{NumberFormat.Compact((int)quest.Condition.value)}";
                    }

                    activeQuestsTooltip += PoolNote(quest.Template.Id);
                }
            }

            foreach (KeyValuePair<MongoID, QuestsHelper.CurrentQuest> keyValuePair in fulfilled)
            {
                keyValuePair.Deconstruct(out _, out QuestsHelper.CurrentQuest quest);

                if (quest.Condition.onlyFoundInRaid)
                {
                    handedOverFir += (int)quest.Condition.value;
                }
                else
                {
                    handedOverNonFir += (int)quest.Condition.value;
                }
            }

            if (!Settings.OnlyActiveQuests!.Value && QuestsHelper.IsNeededForActiveOrFutureQuests(item, out QuestsData.ItemData itemData))
            {
                string futureColor = Settings.FutureQuestTextColor!.GetHexColor(!useCustomTextColors);

                totalNeededFir = itemData.Fir - handedOverFir;
                totalNeededNonFir = itemData.NonFir - handedOverNonFir;

                foreach (NeedsSnapshot.PoolInfo pool in pools)
                {
                    if (!itemData.Quests.ContainsKey(pool.QuestId))
                    {
                        continue;
                    }

                    if (pool.Fir) totalNeededFir -= pool.FullCount; else totalNeededNonFir -= pool.FullCount;
                }

                totalNeededFir = Math.Max(0, totalNeededFir);
                totalNeededNonFir = Math.Max(0, totalNeededNonFir);

                foreach (KeyValuePair<MongoID, QuestsData.QuestValues> quest in itemData.Quests)
                {
                    if (activeQuests.ContainsKey(quest.Key) || fulfilled.ContainsKey(quest.Key))
                    {
                        continue;
                    }

                    if (!neededForFuture)
                    {
                        neededForFuture = true;
                        futureQuestsTooltip = "\n" + "aqc_future_quests".Localized();
                    }

                    if (quest.Key != QuestsHelper.COLLECTOR_ID)
                    {
                        collectorOnly = false;
                    }

                    string questName = quest.Value.LocalizedName.Localized(null);
                    if (questName == quest.Value.LocalizedName)
                    {
                        if (questName.IsNullOrEmpty())
                        {
                            questName = "Unknown Quest";
                        }
                        else
                        {
                            questName = quest.Value.Name;
                        }
                    }

                    futureQuestsTooltip += $"\n{indent}<color={futureColor}>{questName}</color>: {NumberFormat.Compact(quest.Value.Count.Count)}";

                    if (showNonFir)
                    {
                        futureQuestsTooltip += " " + FirLabel(quest.Value.Count.Fir);
                    }

                    futureQuestsTooltip += PoolNote(quest.Key);
                }
            }

            // Hideout upgrades (from the Wishlist Extended fork). Same relevance rule as quests:
            // a non-FiR item only matters if some requirement accepts non-FiR items.
            string hideoutTooltip = "";
            string hideoutLaterTooltip = "";
            int hideoutFir = 0;
            int hideoutNonFir = 0;

            if (Settings.IncludeHideout!.Value && HideoutNeedsData.TryGet(item.StringTemplateId, out HideoutNeedsData.ItemNeeds hideout))
            {
                for (int i = 0; i < hideout.Quantity.Length; i++)
                {
                    if (hideout.Fir[i])
                    {
                        hideoutFir += hideout.Quantity[i];
                    }
                    else
                    {
                        hideoutNonFir += hideout.Quantity[i];
                    }
                }

                if ((item.MarkedAsSpawnedInSession || Settings.CheckmarkOnNonFir!.Value) && hideoutFir + hideoutNonFir > 0 || hideoutNonFir > 0)
                {
                    neededForHideout = true;
                    collectorOnly = false;

                    string hideoutColor = Settings.HideoutTextColor!.GetHexColor(!useCustomTextColors);

                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool futurePass = pass == 1;
                        bool header = false;

                        for (int i = 0; i < hideout.Quantity.Length; i++)
                        {
                            if (hideout.Future[i] != futurePass)
                            {
                                continue;
                            }

                            string line = "";

                            if (!header)
                            {
                                header = true;
                                line += "\n" + (futurePass ? "aqc_hideout_future" : "aqc_hideout").Localized(null);
                            }

                            line += $"\n{indent}<color={hideoutColor}>{hideout.Area[i]} lvl {hideout.Level[i]}</color>: {NumberFormat.Compact(hideout.Quantity[i])} "
                                + FirLabel(hideout.Fir[i]);

                            if (futurePass) hideoutLaterTooltip += line; else hideoutTooltip += line;
                        }
                    }

                    totalNeededFir += hideoutFir;
                    totalNeededNonFir += hideoutNonFir;
                }
            }

            neededForFuture |= neededForHideout;

            // hideout non-FiR needs must show in the total even when non-FiR quests are off
            bool showNonFirTotal = showNonFir || neededForHideout && hideoutNonFir > 0;

            string notFirLine = "";
            if (Settings.CheckmarkOnNonFir!.Value && !item.MarkedAsSpawnedInSession && totalNeededFir > 0)
            {
                notFirLine = "aqc_copy_not_fir".Localized(null);

                if (Settings.CompactFirLabels!.Value)
                {
                    notFirLine = "<size=80%>" + notFirLine + "</size>";
                }
            }

            // "Total needed" is only a fair comparison with "In Stash" when every need of the item is its own: with a shared quest
            // condition (any of several items) it would be incomplete, so it is left out for every such item
            // (now part of the stash block: StockLines)
            string totalLine = "";

            // every section starts with a line break
            // what you can act on now first, then what is needed later: smaller, behind a separator
            string sections = activeQuestsTooltip + hideoutTooltip;
            string laterSections = futureQuestsTooltip + (futureQuestsTooltip.Length > 0 && hideoutLaterTooltip.Length > 0 ? "\n<size=5> </size>" : "") + hideoutLaterTooltip;

            if (laterSections.Length > 0)
            {
                // nothing current above it: the solid line before the sections already separates
                sections += separated && sections.Length > 0
                    ? "\n<size=5> </size>\n" + TooltipSeparator.Soft + "<size=85%>" + laterSections + "</size>"
                    : "<size=85%>" + laterSections + "</size>";
            }

            if (Plugin.isFikaInstalled && Settings.SquadQuests!.Value && SquadQuests.IsNeededForSquadMembers(item, out List<string> members))
            {
                string squadColor = Settings.SquadQuestTextColor!.GetHexColor(!useCustomTextColors);

                neededForFriend = true;
                sections += "\n" + "aqc_squad_quests".Localized(null);

                foreach (string nick in members)
                {
                    sections += $"\n{indent}<color={squadColor}>{nick}</color>";
                }
            }

            if (separated)
            {
                // label (found in raid, or this copy is not FiR), separator, stash and totals, separator, quests and hideout
                string label = foundInRaidLine.Length > 0 ? foundInRaidLine : notFirLine;

                ____tooltipText = (label.Length > 0 ? label + "\n" : "") + TooltipSeparator.Line + "\n";
                ____tooltipText += stashLine + (totalLine.Length > 0 ? "\n" + totalLine : "");

                if (sections.Length > 0)
                {
                    ____tooltipText += "\n" + TooltipSeparator.Line + sections;
                }
            }
            else
            {
                ____tooltipText = (foundInRaidLine.Length > 0 ? foundInRaidLine + "\n" : "") + stashLine
                    + (notFirLine.Length > 0 ? "\n" + notFirLine : "")
                    + (totalLine.Length > 0 ? "\n" + totalLine : "")
                    + sections;
            }

            int leftFir = inStash.Fir - totalNeededFir;
            QuestsHelper.ECheckmarkStatus status = QuestsHelper.GetCheckmarkStatus(neededForActive, neededForFuture, neededForFriend, item.MarkedAsSpawnedInSession,
                leftFir >= 0 && inStash.NonFir + leftFir - totalNeededNonFir >= 0, collectorOnly);

            // the action line is filled in when the tooltip is shown (what you hold changes)
            if (Settings.ActionLine!.Value)
            {
                ____tooltipText += ItemHoverTooltip.ActionToken;
            }

            ItemHoverTooltip.SetTemplate(__instance, ____tooltipText);

            // the tooltip text above stays; hovering the item uses it whenever something is needed, even if no checkmark is drawn
            ItemHoverTooltip.SetRelevant(__instance, status != QuestsHelper.ECheckmarkStatus.None);

            // hideout requirement icons: vanilla checkmark (found in raid items only), so FiR vs non-FiR reads at a glance
            if (Settings.VanillaCheckmarkInHideout!.Value && IsHideoutView(__instance))
            {
                if (item.MarkedAsSpawnedInSession)
                {
                    QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite, QuestsHelper.DEFAULT_COLOR);
                }

                return false;
            }

            // overlays replace the colored statuses: the checkmark is only the grey found in raid marker
            if (!Settings.ClassicCheckmarks!.Value)
            {
                if (item.MarkedAsSpawnedInSession)
                {
                    QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite, QuestsHelper.DEFAULT_COLOR);
                }

                return false;
            }

            switch (status)
            {
                case QuestsHelper.ECheckmarkStatus.Fir:
                    QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite, QuestsHelper.DEFAULT_COLOR);
                    break;
                case QuestsHelper.ECheckmarkStatus.Active:
                    if (Settings.UseCustomQuestColor!.Value)
                    {
                        QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite, Settings.CustomQuestColor!.GetValue());
                    }
                    else
                    {
                        QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____questItemSprite, QuestsHelper.DEFAULT_COLOR);
                    }
                    break;
                case QuestsHelper.ECheckmarkStatus.Future:
                    QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite,
                        item.MarkedAsSpawnedInSession ? Settings.CheckmarkColor!.GetValue() : Settings.NonFirColor!.GetValue());
                    break;
                case QuestsHelper.ECheckmarkStatus.Squad:
                    QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite, Settings.SquadColor!.GetValue());
                    break;
                case QuestsHelper.ECheckmarkStatus.Fulfilled:
                    QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite, Settings.EnoughItemsColor!.GetValue());
                    break;
                case QuestsHelper.ECheckmarkStatus.Collector:
                    QuestsHelper.SetCheckmark(__instance, ____questIconImage, ____foundInRaidSprite, Settings.CollectorColor!.GetValue());
                    break;
                case QuestsHelper.ECheckmarkStatus.None:
                    break;
            }

            return false;
        }
    }
}
