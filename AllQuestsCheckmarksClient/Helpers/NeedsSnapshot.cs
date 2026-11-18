using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.Trading;
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

            // a started quest that takes any copy (not only found in raid ones) is waiting for it
            public bool HandInAnyCopy;
            public bool CollectorMissing;
            public bool FromQuest;
            public bool FromHideout;
            public EStrength Loot;
            public EStrength Buy;
            public EStrength BuyAll;

            // what is still missing per source: FiR copies (and non-FiR copies that can't be bought) to loot, non-FiR copies to buy
            public readonly int[] LootBy = new int[Sources];
            public readonly int[] BuyBy = new int[Sources];

            // the two parts of LootBy: FiR copies missing, and non-FiR copies missing for items that can't be bought
            public readonly int[] FirBy = new int[Sources];
            public readonly int[] UnbuyableBy = new int[Sources];

            // copies you hold that are earmarked for a need (not the ones for a quest you can hand in right now): found in raid needs, and needs that take any copy
            public readonly int[] KeepFirBy = new int[Sources];
            public readonly int[] KeepAnyBy = new int[Sources];
            public int HandInCount;

            // how much the item is needed in all per source (what is left to hand in or build, held or not): found in raid needs and needs that take any copy.
            // Left out for quest conditions that several items share (they have no per item number)
            public readonly int[] NeedFirBy = new int[Sources];
            public readonly int[] NeedAnyBy = new int[Sources];
            public bool Shared;
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

        // One quest condition or hideout requirement, with what is given to it when the copies you hold are shared out
        private class NeedRec
        {
            public int Source;
            public bool Fir;
            public bool Collector;
            public bool Hideout;
            public int Remaining;
            public int FullCount;
            public int Missing;
            public MongoID QuestId;
            public List<MongoID> Targets = [];
            public readonly Dictionary<MongoID, int> Given = [];
        }

        private static readonly Dictionary<MongoID, ItemNeed> _needs = [];
        private static readonly Dictionary<MongoID, List<PoolInfo>> _pools = [];
        private static readonly Dictionary<MongoID, bool> _buyable = [];
        private const float TraderItemsMaxAge = 30f;
        private static HashSet<MongoID> _traderItems = [];
        private static float _traderItemsAt = -100f;
        private static readonly StashHelper.ItemsCount _none = new();
        private static Dictionary<MongoID, StashHelper.ItemsCount> _held = [];
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

        // What you hold of one item (from the cached count: much cheaper than going through the whole inventory every time)
        public static StashHelper.ItemsCount Held(MongoID itemId)
        {
            EnsureFresh();
            return _held.TryGetValue(itemId, out StashHelper.ItemsCount count) ? count : _none;
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
            RefreshTraderItems();

            Profile profile = ClientAppUtils.GetClientApp().GetClientBackEndSession().Profile;
            Dictionary<MongoID, StashHelper.ItemsCount> held = Settings.DebugEmptyStash!.Value ? [] : StashHelper.CountAll();
            _held = held;
            List<Pool> pools = [];

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
            foreach (QuestsData.QuestPool pool in Settings.OnlyActiveQuests!.Value ? [] : QuestsData.Pools)
            {
                if (started.Contains(pool.QuestId))
                {
                    continue;
                }

                pools.Add(new Pool(pool.QuestId, [.. pool.Targets.Distinct()], pool.Count, pool.Count, pool.Fir, false, pool.QuestId == QuestsHelper.COLLECTOR_ID));
            }

            // Every need as one record, so the copies you hold can be shared out between all of them in one go:
            // what a hand in uses up is no longer there for the next quest or upgrade
            List<NeedRec> needs = [];

            foreach (Pool pool in pools)
            {
                if (pool.Targets.Count > 1 && !Settings.SharedConditions!.Value)
                {
                    continue;
                }

                needs.Add(new NeedRec
                {
                    Source = pool.Now ? QuestNow : QuestLater,
                    Fir = pool.Fir,
                    Remaining = pool.Remaining,
                    Targets = pool.Targets,
                    Collector = pool.Collector,
                    QuestId = pool.QuestId,
                    FullCount = pool.FullCount,
                });
            }

            // hideout upgrades: from the Wishlist Extended fork
            if (Settings.IncludeHideout!.Value &&
                HideoutNeedsData.TryGetAll(out string[] hItems, out int[] hQty, out bool[] hFir, out bool[] hFuture))
            {
                for (int i = 0; i < hItems.Length; i++)
                {
                    needs.Add(new NeedRec
                    {
                        Source = hFuture[i] ? HideoutLater : HideoutNow,
                        Fir = hFir[i],
                        Remaining = hQty[i],
                        Targets = [new MongoID(hItems[i])],
                        Hideout = true,
                    });
                }
            }

            // money is earned, not looted or bought: it is no need for the overlays
            needs.RemoveAll(n =>
            {
                n.Targets = [.. n.Targets.Where(t => !StashHelper.MoneyIds.Contains(t))];
                return n.Targets.Count == 0;
            });

            // how many needs want each item: for a shared need the items few others want are used first
            Dictionary<MongoID, int> contest = [];
            foreach (NeedRec need in needs)
            {
                foreach (MongoID target in need.Targets)
                {
                    contest[target] = contest.TryGetValue(target, out int n) ? n + 1 : 1;
                }
            }

            // copies left to share out: [0] found in raid, [1] not
            Dictionary<MongoID, int[]> stock = [];
            foreach (KeyValuePair<MongoID, StashHelper.ItemsCount> entry in held)
            {
                stock[entry.Key] = [entry.Value.Fir, entry.Value.NonFir];
            }

            // current needs before later ones, found in raid needs before the ones that take any copy, narrow needs before wide ones
            needs.Sort((a, b) =>
            {
                int c = a.Source.CompareTo(b.Source);
                if (c != 0) return c;
                c = b.Fir.CompareTo(a.Fir);
                return c != 0 ? c : a.Targets.Count.CompareTo(b.Targets.Count);
            });

            foreach (NeedRec need in needs)
            {
                int missing = need.Remaining;

                foreach (MongoID target in need.Targets.OrderBy(t => contest[t]).ThenByDescending(t => stock.TryGetValue(t, out int[] s) ? s[0] + s[1] : 0))
                {
                    if (missing <= 0 || !stock.TryGetValue(target, out int[] copies))
                    {
                        continue;
                    }

                    int take;

                    if (need.Fir)
                    {
                        take = Math.Min(missing, copies[0]);
                        copies[0] -= take;
                    }
                    else
                    {
                        // keep the found in raid copies for the needs that require them
                        take = Math.Min(missing, copies[1]);
                        copies[1] -= take;
                        int more = Math.Min(missing - take, copies[0]);
                        copies[0] -= more;
                        take += more;
                    }

                    if (take > 0)
                    {
                        need.Given[target] = take;
                        missing -= take;
                    }
                }

                need.Missing = missing;
            }

            // what that means for each item
            Dictionary<MongoID, ItemNeed> items = [];
            HashSet<MongoID> collectorItems = [];

            foreach (NeedRec need in needs)
            {
                bool shared = need.Targets.Count > 1;
                int heldByPool = shared ? need.Targets.Sum(t => held.TryGetValue(t, out StashHelper.ItemsCount c) ? (need.Fir ? c.Fir : c.Total) : 0) : 0;

                foreach (MongoID target in need.Targets)
                {
                    if (!items.TryGetValue(target, out ItemNeed item))
                    {
                        item = new ItemNeed();
                        items.Add(target, item);
                    }

                    item.FromQuest |= !need.Hideout;
                    item.FromHideout |= need.Hideout;
                    item.Shared |= shared;

                    if (!shared)
                    {
                        (need.Fir ? item.NeedFirBy : item.NeedAnyBy)[need.Source] += need.Remaining;
                    }

                    if (need.Collector)
                    {
                        collectorItems.Add(target);
                    }

                    if (need.Missing > 0)
                    {
                        if (need.Fir)
                        {
                            item.FirBy[need.Source] += need.Missing;
                        }
                        else if (IsBuyable(target))
                        {
                            item.BuyBy[need.Source] += need.Missing;
                        }
                        else
                        {
                            item.UnbuyableBy[need.Source] += need.Missing;
                        }
                    }

                    // copies of this item that go to a quest you have started: what you can hand in
                    if (need.Source == QuestNow && need.Given.TryGetValue(target, out int given) && given > 0)
                    {
                        item.HandIn = true;
                        item.HandInCount += given;
                        item.HandInAnyCopy |= !need.Fir;
                    }

                    // copies earmarked for later needs (and for upgrades): do not sell these
                    if (need.Source != QuestNow && !StashHelper.MoneyIds.Contains(target) && need.Given.TryGetValue(target, out int kept) && kept > 0)
                    {
                        (need.Fir ? item.KeepFirBy : item.KeepAnyBy)[need.Source] += kept;
                    }

                    if (shared)
                    {
                        if (!_pools.TryGetValue(target, out List<PoolInfo> list))
                        {
                            list = [];
                            _pools.Add(target, list);
                        }

                        list.Add(new PoolInfo
                        {
                            QuestId = need.QuestId,
                            TargetCount = need.Targets.Count,
                            Held = heldByPool,
                            Remaining = need.Remaining,
                            FullCount = need.FullCount,
                            Fir = need.Fir,
                            Now = need.Source == QuestNow,
                        });
                    }
                }
            }

            foreach (KeyValuePair<MongoID, ItemNeed> entry in items)
            {
                ItemNeed need = entry.Value;
                held.TryGetValue(entry.Key, out StashHelper.ItemsCount own);
                need.CollectorMissing = collectorItems.Contains(entry.Key) && (own?.Fir ?? 0) < 1;

                for (int i = 0; i < Sources; i++)
                {
                    need.LootBy[i] = need.FirBy[i] + need.UnbuyableBy[i];
                }

                need.Loot = Strength(need.LootBy);

                // the overlay can leave out what is only needed later (the tooltip checklist keeps it)
                int[] buyForOverlay = (int[])need.BuyBy.Clone();
                if (!Settings.BuyFutureNeeds!.Value)
                {
                    buyForOverlay[QuestLater] = 0;
                    buyForOverlay[HideoutLater] = 0;
                }

                need.Buy = Strength(buyForOverlay);
                need.BuyAll = Strength(need.BuyBy);

                if (need.HandIn || need.CollectorMissing || need.LootBy.Any(n => n > 0) || need.BuyBy.Any(n => n > 0)
                    || need.KeepFirBy.Any(n => n > 0) || need.KeepAnyBy.Any(n => n > 0) || need.NeedFirBy.Any(n => n > 0) || need.NeedAnyBy.Any(n => n > 0))
                {
                    _needs.Add(entry.Key, need);
                }
            }

            int hash = 17;
            foreach (KeyValuePair<MongoID, ItemNeed> entry in _needs)
            {
                ItemNeed n = entry.Value;
                int sum = n.HandInCount * 19 + (n.CollectorMissing ? 23 : 0) + (n.HandIn ? 29 : 0);

                for (int i = 0; i < Sources; i++)
                {
                    sum += (n.LootBy[i] * 7 + n.BuyBy[i] * 11 + (n.KeepFirBy[i] + n.KeepAnyBy[i]) * 13) * (i + 1);
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

        // Everything the traders sell right now (their loaded assortments: the item types, for money or barter). Refreshed every 30 s
        private static void RefreshTraderItems()
        {
            if (Time.realtimeSinceStartup - _traderItemsAt < TraderItemsMaxAge)
            {
                return;
            }

            _traderItemsAt = Time.realtimeSinceStartup;

            try
            {
                HashSet<MongoID> items = [];
                object session = ClientAppUtils.GetClientApp().GetClientBackEndSession();

                if (session.GetType().GetProperty("Traders")?.GetValue(session) is IEnumerable<Trader> traders)
                {
                    foreach (Trader trader in traders)
                    {
                        Assortment? assortment = trader?.Info is { Unlocked: true } ? trader.CurrentAssortment : null;

                        if (assortment?.TraderController?.RootItem is null)
                        {
                            continue;
                        }

                        foreach (Item item in assortment.TraderController.RootItem.GetAllItems())
                        {
                            items.Add(item.TemplateId);
                        }
                    }
                }

                _traderItems = items;
                _buyable.Clear();
                Plugin.LogDebug($"Trader items: {items.Count} item types for sale");
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning($"Reading the traders' assortments failed, only the flea market ban is used: {e.Message}");
            }
        }

        // Can it be bought? On the flea market (not banned), or sold by a trader
        public static bool IsBuyable(MongoID id)
        {
            if (_buyable.TryGetValue(id, out bool cached))
            {
                return cached;
            }

            bool buyable = true;

            if (Singleton<ItemFactory>.Instantiated && Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(id, out ItemTemplate template))
            {
                buyable = template.CanSellOnRagfair || _traderItems.Contains(id);
            }

            _buyable[id] = buyable;
            return buyable;
        }
    }
}
