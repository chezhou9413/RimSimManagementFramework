using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 表示收藏品兑换购买失败原因，职责是让 UI 能按失败类型给出不同反馈。
    /// </summary>
    public enum CollectibleExchangePurchaseFailKind
    {
        None,
        InvalidItem,
        InvalidCurrency,
        ComponentMissing,
        SoldOut,
        NoPlayerMap,
        NotEnoughCurrency
    }

    /// <summary>
    /// 处理收藏品兑换购买流程，职责是校验配置、扣除地图货币、生成商品并通过空投发放。
    /// </summary>
    public static class CollectibleExchangePurchaseUtility
    {
        /// <summary>
        /// 尝试购买一个兑换商品，负责完成限购、库存、扣款、发货和购买次数记录。
        /// </summary>
        public static bool TryPurchase(CollectibleExchangeListDef exchangeDef, CollectibleExchangeItemEntry item, out string failReason)
        {
            return TryPurchase(exchangeDef, item, out failReason, out _);
        }

        /// <summary>
        /// 尝试购买一个兑换商品，负责额外返回失败类型以便界面播放对应商店文本。
        /// </summary>
        public static bool TryPurchase(CollectibleExchangeListDef exchangeDef, CollectibleExchangeItemEntry item, out string failReason, out CollectibleExchangePurchaseFailKind failKind)
        {
            failReason = "";
            failKind = CollectibleExchangePurchaseFailKind.None;
            if (exchangeDef == null || item == null || item.thingDef == null || item.maxPurchases <= 0)
            {
                failReason = SimTranslation.T("RSMF.CollectibleExchange.InvalidItem");
                failKind = CollectibleExchangePurchaseFailKind.InvalidItem;
                return false;
            }

            if (item.currencyDef == null || item.price <= 0)
            {
                failReason = SimTranslation.T("RSMF.CollectibleExchange.InvalidCurrency");
                failKind = CollectibleExchangePurchaseFailKind.InvalidCurrency;
                return false;
            }

            GameComponent_CollectibleExchangeManager manager = Current.Game?.GetComponent<GameComponent_CollectibleExchangeManager>();
            if (manager == null)
            {
                failReason = SimTranslation.T("RSMF.CollectibleExchange.ComponentMissing");
                failKind = CollectibleExchangePurchaseFailKind.ComponentMissing;
                return false;
            }

            if (manager.GetRemainingCount(exchangeDef, item) <= 0)
            {
                failReason = SimTranslation.T("RSMF.CollectibleExchange.SoldOut");
                failKind = CollectibleExchangePurchaseFailKind.SoldOut;
                return false;
            }

            Map map = ResolvePlayerMap();
            if (map == null)
            {
                failReason = SimTranslation.T("RSMF.CollectibleExchange.NoPlayerMap");
                failKind = CollectibleExchangePurchaseFailKind.NoPlayerMap;
                return false;
            }

            int availableCurrency = CountAvailableCurrency(map, item.currencyDef);
            if (availableCurrency < item.price)
            {
                failReason = SimTranslation.T(
                    "RSMF.CollectibleExchange.NotEnoughCurrency",
                    item.currencyDef.LabelCap.Named("currency"),
                    item.price.Named("need"),
                    availableCurrency.Named("have"));
                failKind = CollectibleExchangePurchaseFailKind.NotEnoughCurrency;
                return false;
            }

            List<Thing> deliveredThings = MakeDeliveryThings(item.thingDef, item.PurchaseCount);
            if (deliveredThings.NullOrEmpty())
            {
                failReason = SimTranslation.T("RSMF.CollectibleExchange.InvalidItem");
                failKind = CollectibleExchangePurchaseFailKind.InvalidItem;
                return false;
            }

            DeductCurrency(map, item.currencyDef, item.price);
            IntVec3 dropCenter = ResolveDropCenter(map);
            DropPodUtility.DropThingsNear(dropCenter, map, deliveredThings, 110, false, false, true, true, true);
            manager.RecordPurchase(exchangeDef, item);
            SendPurchaseLetter(exchangeDef, item, dropCenter, map);
            Messages.Message(SimTranslation.T("RSMF.CollectibleExchange.PurchaseSucceededMessage", FormatItemLabel(item).Named("item")), new LookTargets(dropCenter, map), MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        /// <summary>
        /// 读取可用于购买的玩家地图，负责优先使用当前玩家殖民地地图并在必要时回退到任意玩家殖民地地图。
        /// </summary>
        private static Map ResolvePlayerMap()
        {
            Map current = Find.CurrentMap;
            if (current != null && current.IsPlayerHome)
                return current;

            return Find.AnyPlayerHomeMap;
        }

        /// <summary>
        /// 统计地图上指定货币数量，负责只计算已生成且未销毁的实体堆叠。
        /// </summary>
        private static int CountAvailableCurrency(Map map, ThingDef currencyDef)
        {
            if (map?.listerThings == null || currencyDef == null)
                return 0;

            List<Thing> things = map.listerThings.ThingsOfDef(currencyDef);
            int total = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && thing.Spawned && !thing.Destroyed && thing.stackCount > 0)
                    total += thing.stackCount;
            }
            return total;
        }

        /// <summary>
        /// 统计当前玩家地图中可用于收藏品兑换的货币数量，负责给 UI 提前判断购买按钮状态。
        /// </summary>
        public static int CountAvailableCurrencyForCurrentPlayerMap(ThingDef currencyDef)
        {
            return CountAvailableCurrency(ResolvePlayerMap(), currencyDef);
        }

        /// <summary>
        /// 扣除地图上的货币堆叠，职责是在外层已确认足额后按堆叠逐个减少数量。
        /// </summary>
        private static void DeductCurrency(Map map, ThingDef currencyDef, int amount)
        {
            if (map?.listerThings == null || currencyDef == null || amount <= 0)
                return;

            List<Thing> things = map.listerThings.ThingsOfDef(currencyDef).ToList();
            int remaining = amount;
            for (int i = 0; i < things.Count && remaining > 0; i++)
            {
                Thing thing = things[i];
                if (thing == null || !thing.Spawned || thing.Destroyed || thing.stackCount <= 0)
                    continue;

                int take = System.Math.Min(remaining, thing.stackCount);
                remaining -= take;
                if (take >= thing.stackCount)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    thing.stackCount -= take;
                }
            }
        }

        /// <summary>
        /// 创建待发放物品列表，负责按物品堆叠上限拆分为合法堆叠。
        /// </summary>
        private static List<Thing> MakeDeliveryThings(ThingDef thingDef, int count)
        {
            List<Thing> result = new List<Thing>();
            if (thingDef == null || count <= 0)
                return result;

            if (thingDef.Minifiable)
            {
                for (int i = 0; i < count; i++)
                {
                    Thing thing = MakeDeliveryThing(thingDef);
                    if (thing == null)
                        break;
                    result.Add(thing);
                }
                return result;
            }

            int remaining = count;
            int stackLimit = thingDef.stackLimit > 0 ? thingDef.stackLimit : count;
            while (remaining > 0)
            {
                int chunk = System.Math.Min(remaining, stackLimit);
                Thing thing = MakeDeliveryThing(thingDef);
                if (thing == null)
                    break;

                thing.stackCount = chunk;
                result.Add(thing);
                remaining -= chunk;
            }

            return result;
        }

        //负责按 ThingDef 类型创建单个可发货实体，建筑型商品会先包装成缩小物。
        private static Thing MakeDeliveryThing(ThingDef thingDef)
        {
            Thing thing = ThingMaker.MakeThing(thingDef, thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef) : null);
            if (thing == null)
                return null;

            if (!thingDef.Minifiable)
                return thing;

            MinifiedThing minified = thing.MakeMinified();
            if (minified == null)
            {
                if (!thing.Destroyed)
                    thing.Destroy(DestroyMode.Vanish);
                return null;
            }

            minified.stackCount = 1;
            return minified;
        }

        /// <summary>
        /// 选择空投中心点，负责优先靠近殖民地安全降落点并在失败时回退到地图中心。
        /// </summary>
        private static IntVec3 ResolveDropCenter(Map map)
        {
            IntVec3 safeCell = DropCellFinder.TryFindSafeLandingSpotCloseToColony(map, IntVec2.One);
            if (safeCell.IsValid)
                return safeCell;

            Pawn colonist = map.mapPawns?.FreeColonistsSpawned?.FirstOrDefault();
            if (colonist != null && colonist.Position.IsValid)
                return colonist.Position;

            return map.Center;
        }

        /// <summary>
        /// 发送购买送达信件，负责让玩家能通过信件定位空投位置。
        /// </summary>
        private static void SendPurchaseLetter(CollectibleExchangeListDef exchangeDef, CollectibleExchangeItemEntry item, IntVec3 dropCenter, Map map)
        {
            string shopName = string.IsNullOrWhiteSpace(exchangeDef.shopName) ? exchangeDef.LabelCap.RawText : exchangeDef.shopName;
            TaggedString label = SimTranslation.T("RSMF.CollectibleExchange.LetterLabel", shopName.Named("shop"));
            TaggedString text = SimTranslation.T("RSMF.CollectibleExchange.LetterText", shopName.Named("shop"), FormatItemLabel(item).Named("item"));
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, new LookTargets(dropCenter, map));
        }

        /// <summary>
        /// 格式化购买物品显示文本，负责在单次购买数量大于一时显示数量。
        /// </summary>
        private static string FormatItemLabel(CollectibleExchangeItemEntry item)
        {
            if (item == null)
                return "";

            return item.PurchaseCount > 1
                ? SimTranslation.T("RSMF.CollectibleExchange.ItemWithCount", item.DisplayLabel.Named("item"), item.PurchaseCount.Named("count"))
                : item.DisplayLabel;
        }
    }
}
