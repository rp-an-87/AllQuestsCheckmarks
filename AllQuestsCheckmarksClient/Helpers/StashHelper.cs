using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using SPT.Reflection.Utils;
using System.Collections.Generic;
using System.Linq;
using ZGFueDkx.ZGCLib.helpers;

namespace AllQuestsCheckmarks.Helpers
{
    class StashHelper
    {
        public static readonly List<MongoID> MoneyIds =
        [
            "5449016a4bdc2d6f028b456f", //Roubles
            "5696686a4bdc2da3298b456a", //Dollars
            "569668774bdc2da2298b4568", //Euros
        ];

        private static readonly Dictionary<MongoID, ItemsCount> _itemsCache = [];

        public class ItemsCount
        {
            public int Fir;
            public int NonFir;
            public int Total => Fir + NonFir;
        }

        public static ItemsCount GetItemsInStash(MongoID itemId)
        {
            ItemsCount itemsCount = new();
            Profile profile = ClientAppUtils.GetClientApp().GetClientBackEndSession().Profile;
            IEnumerable<Item> items;

            if (RaidUtils.IsInRaid())
            {
                if (_itemsCache != null && _itemsCache.TryGetValue(itemId, out ItemsCount cached))
                {
                    itemsCount.Fir += cached.Fir;
                    itemsCount.NonFir += cached.NonFir;
                }

                if (!Settings.IncludeRaidItems!.Value)
                {
                    return itemsCount;
                }

                items = profile.Inventory.GetPlayerItems(EPlayerItems.Equipment | EPlayerItems.QuestItems).Where(i => i.TemplateId == itemId);
            }
            else
            {
                items = profile.Inventory.GetPlayerItems().Where(i => i.TemplateId == itemId);
            }

            foreach (Item item in items)
            {
                if (item.MarkedAsSpawnedInSession)
                {
                    itemsCount.Fir += item.StackObjectsCount;
                }
                else
                {
                    itemsCount.NonFir += item.StackObjectsCount;
                }
            }

            return itemsCount;
        }

        // What you carry in a raid (equipment and quest items): it goes to the stash when you extract
        public static ItemsCount GetCarried(MongoID itemId)
        {
            // the caller decides when this applies (a raid, or the debug switch that pretends one)
            ItemsCount count = new();
            Profile profile = ClientAppUtils.GetClientApp().GetClientBackEndSession().Profile;

            foreach (Item item in profile.Inventory.GetPlayerItems(EPlayerItems.Equipment | EPlayerItems.QuestItems).Where(i => i.TemplateId == itemId))
            {
                if (item.MarkedAsSpawnedInSession)
                {
                    count.Fir += item.StackObjectsCount;
                }
                else
                {
                    count.NonFir += item.StackObjectsCount;
                }
            }

            return count;
        }

        // GetItemsInStash for every item type at once
        public static Dictionary<MongoID, ItemsCount> CountAll()
        {
            Dictionary<MongoID, ItemsCount> result = [];
            Profile profile = ClientAppUtils.GetClientApp().GetClientBackEndSession().Profile;

            void Add(MongoID id, int fir, int nonFir)
            {
                if (!result.TryGetValue(id, out ItemsCount count))
                {
                    count = new ItemsCount();
                    result.Add(id, count);
                }

                count.Fir += fir;
                count.NonFir += nonFir;
            }

            IEnumerable<Item> items;

            if (RaidUtils.IsInRaid())
            {
                foreach (KeyValuePair<MongoID, ItemsCount> cached in _itemsCache)
                {
                    Add(cached.Key, cached.Value.Fir, cached.Value.NonFir);
                }

                if (!Settings.IncludeRaidItems!.Value)
                {
                    return result;
                }

                items = profile.Inventory.GetPlayerItems(EPlayerItems.Equipment | EPlayerItems.QuestItems);
            }
            else
            {
                items = profile.Inventory.GetPlayerItems();
            }

            foreach (Item item in items)
            {
                if (item.MarkedAsSpawnedInSession)
                {
                    Add(item.TemplateId, item.StackObjectsCount, 0);
                }
                else
                {
                    Add(item.TemplateId, 0, item.StackObjectsCount);
                }
            }

            return result;
        }

        public static void BuildItemsCache()
        {
            _itemsCache.Clear();

            Profile profile = ClientAppUtils.GetClientApp().GetClientBackEndSession().Profile;
            IEnumerable<Item> itemsToCache = profile.Inventory.GetPlayerItems(EPlayerItems.HideoutStashes);
            IEnumerable<Item>? stashItems = Singleton<HideoutRepresentation>.Instance?.AllStashItems;

            if (stashItems != null)
            {
                itemsToCache = itemsToCache.Concat(stashItems);
            }

            foreach (Item item in itemsToCache)
            {
                if (_itemsCache.TryGetValue(item.TemplateId, out ItemsCount itemsCount))
                {
                    if (item.MarkedAsSpawnedInSession)
                    {
                        itemsCount.Fir += item.StackObjectsCount;
                    }
                    else
                    {
                        itemsCount.NonFir += item.StackObjectsCount;
                    }
                }
                else
                {
                    ItemsCount count = new();

                    if (item.MarkedAsSpawnedInSession)
                    {
                        count.Fir = item.StackObjectsCount;
                    }
                    else
                    {
                        count.NonFir = item.StackObjectsCount;
                    }

                    _itemsCache.Add(item.TemplateId, count);
                }
            }

            Plugin.LogSource?.LogInfo($"Items cache built. Total items in cache: {_itemsCache.Count}");
        }
    }
}
