using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ZGFueDkx.ZGCLib.helpers;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// What the player has to do about each item, computed once and cached (rebuilt lazily when something changes, see Invalidate):
    /// the overlays read one dictionary entry per item view.
    /// Needs come from three places that already exist: hideout upgrades (Wishlist Extended fork API), quests not started yet
    /// (the quest data of this mod) and started quests (the profile, with hand-in progress). Quest conditions that accept several items
    /// share one count, so they are evaluated as a pool over the stash.
    /// </summary>
    internal static class NeedsSnapshot
    {
        private const float MaxAge = 5f;

        public enum EStrength
        {
            None = 0,
            Later = 1,
            Now = 2,
        }

        public class ItemNeed
        {
            public bool HandIn;
            public bool CollectorMissing;
            public bool FromQuest;
            public bool FromHideout;
            public EStrength Loot;
            public EStrength Buy;
        }

        private class Acc
        {
            public int FirNow, FirLater, NonFirNow, NonFirLater;
            public bool PoolFirNow, PoolFirLater, PoolNonFirNow, PoolNonFirLater;
            public bool HandIn, Collector, Quest, Hideout;
        }

        private class Pool(List<MongoID> targets, int remaining, bool fir, bool now, bool collector)
        {
            public List<MongoID> Targets = targets;
            public int Remaining = remaining;
            public bool Fir = fir;
            public bool Now = now;
            public bool Collector = collector;
        }

        private static readonly Dictionary<MongoID, ItemNeed> _needs = [];
        private static readonly Dictionary<MongoID, bool> _buyable = [];
        private static bool _dirty = true;
        private static float _builtAt;
        private static bool _builtInRaid;
        private static bool _failed;

        public static void Invalidate() => _dirty = true;

        // the real raid state, or the debug switch that stands in for it
        public static bool InRaid => RaidUtils.IsInRaid() || Settings.DebugInRaid!.Value;

        public static bool TryGet(MongoID itemId, out ItemNeed need)
        {
            EnsureFresh();
            return _needs.TryGetValue(itemId, out need);
        }

        private static void EnsureFresh()
        {
            bool inRaid = InRaid;

            if (!_dirty && inRaid == _builtInRaid && Time.realtimeSinceStartup - _builtAt < MaxAge)
            {
                return;
            }

            _dirty = false;
            _builtInRaid = inRaid;
            _builtAt = Time.realtimeSinceStartup;

            try
            {
                Rebuild(inRaid);
                _failed = false;
            }
            catch (Exception e)
            {
                _needs.Clear();

                if (!_failed)
                {
                    Plugin.LogSource?.LogError($"Building the needs snapshot failed: {e}");
                }

                _failed = true;
            }
        }

        private static void Rebuild(bool inRaid)
        {
            _needs.Clear();

            Profile profile = ClientAppUtils.GetClientApp().GetClientBackEndSession().Profile;
            Dictionary<MongoID, StashHelper.ItemsCount> held = Settings.DebugEmptyStash!.Value ? [] : StashHelper.CountAll();
            Dictionary<MongoID, Acc> accs = [];
            List<Pool> pools = [];

            Acc AccFor(MongoID id)
            {
                if (!accs.TryGetValue(id, out Acc acc))
                {
                    acc = new Acc();
                    accs.Add(id, acc);
                }

                return acc;
            }

            // started quests: conditions and hand-in progress come from the profile
            HashSet<MongoID> started = [];

            foreach (QuestDataClass quest in profile.QuestsData)
            {
                if (quest.Template is null || quest.Status != EQuestStatus.Started)
                {
                    continue;
                }

                started.Add(quest.Template.Id);
                bool collector = quest.Template.Id == QuestsHelper.COLLECTOR_ID;

                if (!QuestAllowed(quest.Template.Id, collector) ||
                    !quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out ConditionCollection conditions))
                {
                    continue;
                }

                List<string> seen = [];

                foreach (Condition condition in conditions)
                {
                    if (condition is not ConditionItem conditionItem || condition is ConditionWeaponAssembly ||
                        quest.CompletedConditions.Contains(condition.id))
                    {
                        continue;
                    }

                    string kind = condition.GetType().Name;
                    if (kind != "ConditionHandoverItem" && kind != "ConditionFindItem" && kind != "ConditionLeaveItemAtLocation")
                    {
                        continue;
                    }

                    bool fir = conditionItem.onlyFoundInRaid;
                    if (!fir && !Settings.IncludeNonFir!.Value)
                    {
                        continue;
                    }

                    List<MongoID> targets = [.. conditionItem.target.Where(AQCUtils.IsValidMongoID).Select(t => new MongoID(t))];
                    int remaining = (int)conditionItem.value;

                    if (profile.TaskConditionCounters.TryGetValue(condition.id, out TaskConditionCounter counter))
                    {
                        remaining -= counter.Value;
                    }

                    // find + hand over of the same items is one need
                    string key = string.Join(",", targets.Select(t => t.ToString()).OrderBy(s => s)) + "/" + (int)conditionItem.value;
                    if (targets.Count == 0 || remaining <= 0 || seen.Contains(key))
                    {
                        continue;
                    }

                    seen.Add(key);
                    pools.Add(new Pool(targets, remaining, fir, true, collector));
                }
            }

            // quests not started yet: the quest data of this mod (no progress, nothing handed in yet)
            foreach (QuestsData.QuestPool pool in QuestsData.Pools)
            {
                if (started.Contains(pool.QuestId))
                {
                    continue;
                }

                pools.Add(new Pool(pool.Targets, pool.Count, pool.Fir, false, pool.QuestId == QuestsHelper.COLLECTOR_ID));
            }

            foreach (Pool pool in pools)
            {
                int heldByPool = pool.Targets.Sum(t => held.TryGetValue(t, out StashHelper.ItemsCount c) ? (pool.Fir ? c.Fir : c.Total) : 0);

                foreach (MongoID target in pool.Targets)
                {
                    Acc acc = AccFor(target);
                    acc.Quest = true;
                    held.TryGetValue(target, out StashHelper.ItemsCount own);

                    if (pool.Now && heldByPool > 0 && own is not null && (pool.Fir ? own.Fir : own.Total) > 0)
                    {
                        acc.HandIn = true;
                    }

                    if (pool.Collector)
                    {
                        acc.Collector = true;
                    }

                    if (pool.Targets.Count == 1)
                    {
                        // a single item: allocated with the other needs of that item below
                        if (pool.Fir)
                        {
                            if (pool.Now) acc.FirNow += pool.Remaining; else acc.FirLater += pool.Remaining;
                        }
                        else
                        {
                            if (pool.Now) acc.NonFirNow += pool.Remaining; else acc.NonFirLater += pool.Remaining;
                        }
                    }
                    else if (heldByPool < pool.Remaining)
                    {
                        // several items share the count: short until the sum of what you hold reaches it
                        if (pool.Fir)
                        {
                            if (pool.Now) acc.PoolFirNow = true; else acc.PoolFirLater = true;
                        }
                        else
                        {
                            if (pool.Now) acc.PoolNonFirNow = true; else acc.PoolNonFirLater = true;
                        }
                    }
                }
            }

            // hideout upgrades: from the Wishlist Extended fork
            if (Settings.IncludeHideout!.Value &&
                WishlistBridge.TryGetAllHideoutNeeds(out string[] hItems, out int[] hQty, out bool[] hFir, out bool[] hFuture))
            {
                for (int i = 0; i < hItems.Length; i++)
                {
                    Acc acc = AccFor(new MongoID(hItems[i]));
                    acc.Hideout = true;

                    if (hFir[i])
                    {
                        if (hFuture[i]) acc.FirLater += hQty[i]; else acc.FirNow += hQty[i];
                    }
                    else
                    {
                        if (hFuture[i]) acc.NonFirLater += hQty[i]; else acc.NonFirNow += hQty[i];
                    }
                }
            }

            foreach (KeyValuePair<MongoID, Acc> entry in accs)
            {
                MongoID id = entry.Key;
                Acc acc = entry.Value;

                held.TryGetValue(id, out StashHelper.ItemsCount own);
                int heldFir = own?.Fir ?? 0;
                int heldNonFir = own?.NonFir ?? 0;

                // FiR copies fill FiR needs first (now before later); what is left over, with the non-FiR copies, fills the non-FiR needs
                int shortFirNow = Math.Max(0, acc.FirNow - heldFir);
                int spare = Math.Max(0, heldFir - acc.FirNow);
                int shortFirLater = Math.Max(0, acc.FirLater - spare);
                spare = Math.Max(0, spare - acc.FirLater);

                int pool = heldNonFir + spare;
                int shortNonFirNow = Math.Max(0, acc.NonFirNow - pool);
                pool = Math.Max(0, pool - acc.NonFirNow);
                int shortNonFirLater = Math.Max(0, acc.NonFirLater - pool);

                bool buyable = IsBuyable(id);

                bool firNow = shortFirNow > 0 || acc.PoolFirNow;
                bool firLater = shortFirLater > 0 || acc.PoolFirLater;
                bool nonFirNow = shortNonFirNow > 0 || acc.PoolNonFirNow;
                bool nonFirLater = shortNonFirLater > 0 || acc.PoolNonFirLater;

                // in raid: loot it when FiR copies are short, or when it can't be bought; out of raid: buy it when it can be
                EStrength loot = firNow || !buyable && nonFirNow ? EStrength.Now
                    : firLater || !buyable && nonFirLater ? EStrength.Later : EStrength.None;
                EStrength buy = buyable && nonFirNow ? EStrength.Now : buyable && nonFirLater ? EStrength.Later : EStrength.None;

                ItemNeed need = new()
                {
                    HandIn = acc.HandIn,
                    CollectorMissing = acc.Collector && heldFir < 1,
                    FromQuest = acc.Quest,
                    FromHideout = acc.Hideout,
                    Loot = loot,
                    Buy = buy,
                };

                if (need.HandIn || need.CollectorMissing || need.Loot != EStrength.None || need.Buy != EStrength.None)
                {
                    _needs.Add(id, need);
                }
            }

            Plugin.LogDebug($"Needs snapshot built (in raid: {inRaid}): {_needs.Count} items, {pools.Count} quest conditions");
        }

        // Same quest filters as the checkmarks (Collector, loyalty regain)
        private static bool QuestAllowed(MongoID questId, bool collector)
        {
            if (collector && !Settings.IncludeCollector!.Value)
            {
                return false;
            }

            return Settings.IncludeLoyaltyRegain!.Value || !QuestsHelper.TRUST_REGAIN_QUESTS.Contains(questId);
        }

        // Can it be bought? Flea market eligibility from the item template (trader-only items are treated as buyable)
        private static bool IsBuyable(MongoID id)
        {
            if (_buyable.TryGetValue(id, out bool cached))
            {
                return cached;
            }

            bool buyable = true;

            if (Singleton<ItemFactory>.Instantiated && Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(id, out ItemTemplate template))
            {
                buyable = template.CanSellOnRagfair;
            }

            _buyable[id] = buyable;
            return buyable;
        }
    }
}
