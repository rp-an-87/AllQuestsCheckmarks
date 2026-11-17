using AllQuestsCheckmarks.Helpers;
using EFT;
using EFT.Quests;
using SPT.Reflection.Utils;
using System.Collections.Generic;

namespace AllQuestsCheckmarks
{
    /// <summary>
    /// Entry point for other mods (looked up by reflection, keep names and signatures unchanged).
    /// </summary>
    public static class AllQuestsCheckmarksApi
    {
        public const int ApiVersion = 1;

        /// <summary>
        /// True when this tooltip already lists hideout upgrades and quests for the hovered item
        /// (so Wishlist Extended can leave those lines out and avoid duplicates).
        /// </summary>
        /// <summary>
        /// Every remaining quest item need (parallel arrays) of quests the profile has NOT started yet, honoring this mod's quest settings.
        /// Started quests are left out: the caller already knows them and their hand-in progress.
        /// </summary>
        public static bool TryGetFutureQuestNeeds(out string[] itemId, out string[] questName, out int[] quantity, out bool[] fir)
        {
            itemId = [];
            questName = [];
            quantity = [];
            fir = [];

            HashSet<MongoID> started = [];
            Profile profile = ClientAppUtils.GetClientApp().GetClientBackEndSession().Profile;

            foreach (QuestDataClass quest in profile.QuestsData)
            {
                if (quest.Template is not null && quest.Status == EQuestStatus.Started)
                {
                    started.Add(quest.Template.Id);
                }
            }

            List<string> ids = [];
            List<string> names = [];
            List<int> counts = [];
            List<bool> firs = [];

            foreach (var (item, data) in QuestsData.QuestItemsByItemId)
            {
                foreach (var (questId, values) in data.Quests)
                {
                    if (started.Contains(questId))
                    {
                        continue;
                    }

                    ids.Add(item.ToString());
                    names.Add(string.IsNullOrEmpty(values.LocalizedName) ? values.Name : values.LocalizedName);
                    counts.Add(values.Count.Count);
                    firs.Add(values.Count.Fir);
                }
            }

            itemId = [.. ids];
            questName = [.. names];
            quantity = [.. counts];
            fir = [.. firs];

            return ids.Count > 0;
        }

        public static bool ShowsHideoutInTooltip()
        {
            return Settings.IncludeHideout!.Value && Settings.HoverItemTooltip!.Value && WishlistBridge.Available;
        }
    }
}
