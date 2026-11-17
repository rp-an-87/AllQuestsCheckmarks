using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using ZGFueDkx.ZGCLib.helpers;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// What the player has to do about each item, computed once and cached (rebuilt lazily when something changes, see Invalidate):
    /// the overlays and the tooltip read one dictionary entry per item.
    /// Needs come from three places that already exist: hideout upgrades (Wishlist Extended fork API), quests not started yet
    /// (the quest data of this mod) and started quests (the profile, with hand-in progress). Quest conditions that accept several items
    /// share one count, so they are evaluated as a pool over the stash.
    /// </summary>
    internal static class NeedsSnapshot
    {
        private const float MaxAge = 5f;

        // inventory events can fire many times a second (hovering some items does): rebuild at most this often
        private const float MinRebuildInterval = 0.3f;

        public enum EStrength
        {
            None = 0,
            Later = 1,
            Now = 2,
        }

        // Where a need comes from, in the order held copies are used up: what is needed now comes first, so the shortage lands on later needs
        public const int QuestNow = 0;
        public const int HideoutNow = 1;
        public const int QuestLater = 2;
        public const int HideoutLater = 3;
        public const int Sources = 4;

        public class ItemNeed
        {
            public bool HandIn;
            public bool CollectorMissing;
            public bool FromQuest;
            public bool FromHideout;
            public EStrength Loot;
            public EStrength Buy;

            // what is still missing per source: FiR copies (and non-FiR copies that can't be bought) to loot, non-FiR copies to buy
            public readonly int[] LootBy = new int[Sources];
            public readonly int[] BuyBy = new int[Sources];
            public int HandInCount;
        }

        // A quest condition that accepts several items for one shared count, as seen from one of its items
        public class PoolInfo
        {
            public MongoID QuestId;
            public int TargetCount;
            public int Held;
            public int Remaining;
            public int FullCount;
            public bool Fir;
            public bool Now;
        }

        private class Acc
        {
            public readonly int[] FirDemand = new int[Sources];
            public readonly int[] NonFirDemand = new int[Sources];
            public readonly int[] PoolFirShort = new int[Sources];
            public readonly int[] PoolNonFirShort = new int[Sources];
            public int HandInCount;
            public bool HandIn, Collector, Quest, Hideout;
        }

        private class Pool(MongoID questId, List<MongoID> targets, int remaining, int fullCount, bool fir, bool now, bool collector)
        {
            public MongoID QuestId = questId;
            public List<MongoID> Targets = targets;
            public int Remaining = remaining;
            public int FullCount = fullCount;
            public bool Fir = fir;
            public bool Now = now;
            public bool Collector = collector;
        }

        private static readonly Dictionary<MongoID, ItemNeed> _needs = [];
        private static readonly Dictionary<MongoID, List<PoolInfo>> _pools = [];
        private static readonly Dictionary<MongoID, bool> _buyable = [];
        private static bool _dirty = true;
        private static float _builtAt;
        private static bool _builtInRaid;
        private static bool _failed;
        private static int _lastHash;

        // goes up only when a rebuild gave different results, so the views are redrawn only when something changed
        public static int Version { get; private set; }

        public static void Invalidate() => _dirty = true;

        public static void Refresh() => EnsureFresh();

        // the real raid state, or the debug switch that stands in for it
        public static bool InRaid => RaidUtils.IsInRaid() || Settings.DebugInRaid!.Value;

        public static bool TryGet(MongoID itemId, out ItemNeed need)
        {
            EnsureFresh();
            return _needs.TryGetValue(itemId, out need);
        }

        // Quest conditions that several items share, as seen from one item (empty for most items)
        public static IReadOnlyList<PoolInfo> PoolsFor(MongoID itemId)
        {
            EnsureFresh();
            return _pools.TryGetValue(itemId, out List<PoolInfo> list) ? list : [];
        }

        private static void EnsureFresh()
        {
            bool inRaid = InRaid;
            float age = Time.realtimeSinceStartup - _builtAt;

            if (!_dirty && inRaid == _builtInRaid && age < MaxAge)
            {
                return;
            }

            // a burst of events must not cause a rebuild each time: keep the last result a moment (still marked dirty)
            if (inRaid == _builtInRaid && age < MinRebuildInterval)
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
                _pools.Clear();

                if (!_failed)
                {
                    Plugin.LogSource?.LogError($"Building the needs snapshot failed: {e}");
                }

                _failed = true;
            }
        }

        private static void Rebuild(bool inRaid)
        {
            Stopwatch watch = Stopwatch.StartNew();
            _needs.Clear();
            _pools.Clear();

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

                    List<MongoID> targets = [.. conditionItem.target.Where(AQCUtils.IsValidMongoID).Select(t => new MongoID(t)).Distinct()];
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
                    pools.Add(new Pool(quest.Template.Id, targets, remaining, (int)conditionItem.value, fir, true, collector));
                }
            }

            // quests not started yet: the quest data of this mod (no progress, nothing handed in yet)
            foreach (QuestsData.QuestPool pool in QuestsData.Pools)
            {
                if (started.Contains(pool.QuestId))
                {
                    continue;
                }

                pools.Add(new Pool(pool.QuestId, [.. pool.Targets.Distinct()], pool.Count, pool.Count, pool.Fir, false, pool.QuestId == QuestsHelper.COLLECTOR_ID));
            }

            foreach (Pool pool in pools)
            {
                int heldByPool = pool.Targets.Sum(t => held.TryGetValue(t, out StashHelper.ItemsCount c) ? (pool.Fir ? c.Fir : c.Total) : 0);
                bool shared = pool.Targets.Count > 1;
                int sourceIndex = pool.Now ? QuestNow : QuestLater;

                foreach (MongoID target in pool.Targets)
                {
                    Acc acc = AccFor(target);
                    acc.Quest = true;
                    held.TryGetValue(target, out StashHelper.ItemsCount own);

                    if (pool.Now && heldByPool > 0 && own is not null && (pool.Fir ? own.Fir : own.Total) > 0)
                    {
                        acc.HandIn = true;
                        acc.HandInCount += Math.Min(pool.Remaining, shared ? heldByPool : (pool.Fir ? own.Fir : own.Total));
                    }

                    if (pool.Collector)
                    {
                        acc.Collector = true;
                    }

                    if (!shared)
                    {
                        // a single item: allocated with the other needs of that item below
                        (pool.Fir ? acc.FirDemand : acc.NonFirDemand)[sourceIndex] += pool.Remaining;
                        continue;
                    }

                    // several items share the count: short until the sum of what you hold reaches it
                    if (heldByPool < pool.Remaining)
                    {
                        (pool.Fir ? acc.PoolFirShort : acc.PoolNonFirShort)[sourceIndex] += pool.Remaining - heldByPool;
                    }

                    if (!_pools.TryGetValue(target, out List<PoolInfo> list))
                    {
                        list = [];
                        _pools.Add(target, list);
                    }

                    list.Add(new PoolInfo
                    {
                        QuestId = pool.QuestId,
                        TargetCount = pool.Targets.Count,
                        Held = heldByPool,
                        Remaining = pool.Remaining,
                        FullCount = pool.FullCount,
                        Fir = pool.Fir,
                        Now = pool.Now,
                    });
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
                    (hFir[i] ? acc.FirDemand : acc.NonFirDemand)[hFuture[i] ? HideoutLater : HideoutNow] += hQty[i];
                }
            }

            foreach (KeyValuePair<MongoID, Acc> entry in accs)
            {
                MongoID id = entry.Key;
                Acc acc = entry.Value;

                held.TryGetValue(id, out StashHelper.ItemsCount own);
                int heldFir = own?.Fir ?? 0;
                int heldNonFir = own?.NonFir ?? 0;

                // FiR copies fill FiR needs first; what is left over, with the non-FiR copies, fills the non-FiR needs.
                // Within a kind the needs are filled in the order of the sources, so what stays missing is the later needs
                int[] firMissing = Unmet(acc.FirDemand, heldFir, out int spareFir);
                int[] nonFirMissing = Unmet(acc.NonFirDemand, heldNonFir + spareFir, out _);

                for (int i = 0; i < Sources; i++)
                {
                    firMissing[i] += acc.PoolFirShort[i];
                    nonFirMissing[i] += acc.PoolNonFirShort[i];
                }

                bool buyable = IsBuyable(id);

                ItemNeed need = new()
                {
                    HandIn = acc.HandIn,
                    CollectorMissing = acc.Collector && heldFir < 1,
                    FromQuest = acc.Quest,
                    FromHideout = acc.Hideout,
                    HandInCount = acc.HandInCount,
                };

                for (int i = 0; i < Sources; i++)
                {
                    // in raid: loot it when FiR copies are short, or when it can't be bought; out of raid: buy it when it can be
                    need.LootBy[i] = firMissing[i] + (buyable ? 0 : nonFirMissing[i]);
                    need.BuyBy[i] = buyable ? nonFirMissing[i] : 0;
                }

                need.Loot = Strength(need.LootBy);
                need.Buy = Strength(need.BuyBy);

                if (need.HandIn || need.CollectorMissing || need.Loot != EStrength.None || need.Buy != EStrength.None)
                {
                    _needs.Add(id, need);
                }
            }

            int hash = 17;
            foreach (KeyValuePair<MongoID, ItemNeed> entry in _needs)
            {
                ItemNeed n = entry.Value;
                int sum = n.HandInCount * 19 + (n.CollectorMissing ? 23 : 0) + (n.HandIn ? 29 : 0);

                for (int i = 0; i < Sources; i++)
                {
                    sum += (n.LootBy[i] * 7 + n.BuyBy[i] * 11) * (i + 1);
                }

                hash = unchecked(hash + entry.Key.GetHashCode() * 31 + sum * 37);
            }

            if (hash != _lastHash)
            {
                _lastHash = hash;
                Version++;
            }

            Plugin.LogDebug($"Needs snapshot built in {watch.ElapsedMilliseconds} ms (in raid: {inRaid}): {_needs.Count} items, {pools.Count} quest conditions, changed: {Version}");
        }

        // How much of each need is not covered when `available` copies are used up in the order of the sources
        private static int[] Unmet(int[] demand, int available, out int spare)
        {
            int[] unmet = new int[Sources];

            for (int i = 0; i < Sources; i++)
            {
                int take = Math.Min(demand[i], available);
                available -= take;
                unmet[i] = demand[i] - take;
            }

            spare = available;
            return unmet;
        }

        private static EStrength Strength(int[] missing)
        {
            if (missing[QuestNow] > 0 || missing[HideoutNow] > 0)
            {
                return EStrength.Now;
            }

            return missing[QuestLater] > 0 || missing[HideoutLater] > 0 ? EStrength.Later : EStrength.None;
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
