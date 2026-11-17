using AllQuestsCheckmarks.Helpers;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using JetBrains.Annotations;
using SPT.Reflection.Patching;
using System.Collections.Generic;
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

            Plugin.LogDebug($"Checkmark panel: requirement panel={inRequirementPanel}, view type={viewType}");

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

            bool separated = Settings.SeparatedTooltip!.Value;
            string foundInRaidLine = "";

            if (item.MarkedAsSpawnedInSession)
            {
                foundInRaidLine = separated
                    ? "<size=" + (Settings.CompactFirLabels!.Value ? "80" : "100") + "%><color=#66FF66>" + "Item found in raid".Localized(null) + "</color></size>"
                    : "Item found in raid".Localized(null);
            }

            bool showNonFir = Settings.IncludeNonFir!.Value;
            StashHelper.ItemsCount inStash = StashHelper.GetItemsInStash(item.TemplateId);
            string stashLine = string.Format("aqc_in_stash".Localized(null), inStash.Total, inStash.Fir);

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
                            totalNeededFir += (int)condition.value - counter.Value;
                        }
                        else
                        {
                            handedOverNonFir += counter.Value;
                            totalNeededNonFir += (int)condition.value - counter.Value;
                        }

                        activeQuestsTooltip += $"{counter.Value}/{condition.value}";

                        if (showNonFir)
                        {
                            activeQuestsTooltip += " " + FirLabel(condition.onlyFoundInRaid);
                        }
                    }
                    else
                    {
                        activeQuestsTooltip += $"0/{quest.Condition.value}";
                    }
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

                    futureQuestsTooltip += $"\n{indent}<color={futureColor}>{questName}</color>: {quest.Value.Count.Count}";

                    if (showNonFir)
                    {
                        futureQuestsTooltip += " " + FirLabel(quest.Value.Count.Fir);
                    }
                }
            }

            // Hideout upgrades (from the Wishlist Extended fork). Same relevance rule as quests:
            // a non-FiR item only matters if some requirement accepts non-FiR items.
            string hideoutTooltip = "";
            int hideoutFir = 0;
            int hideoutNonFir = 0;

            if (Settings.IncludeHideout!.Value && WishlistBridge.TryGetHideoutNeeds(item.StringTemplateId, out WishlistBridge.HideoutNeeds hideout))
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

                            if (!header)
                            {
                                header = true;
                                hideoutTooltip += "\n" + (futurePass ? "aqc_hideout_future" : "aqc_hideout").Localized(null);
                            }

                            hideoutTooltip += $"\n{indent}<color={hideoutColor}>{hideout.Area[i]} lvl {hideout.Level[i]}</color>: {hideout.Quantity[i]} "
                                + FirLabel(hideout.Fir[i]);
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

            string totalLine = "";
            if (totalNeededFir > 0 || showNonFirTotal && totalNeededNonFir > 0)
            {
                totalLine = string.Format((showNonFirTotal ? "aqc_total_needed_alt" : "aqc_total_needed").Localized(null),
                       totalNeededFir, totalNeededNonFir);
            }

            // every section starts with a line break
            string sections = activeQuestsTooltip + futureQuestsTooltip + hideoutTooltip;

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

                ____tooltipText = label.Length > 0 ? label + "\n" + TooltipSeparator.Line + "\n" : "";
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
