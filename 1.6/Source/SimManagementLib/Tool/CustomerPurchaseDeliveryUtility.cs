using SimManagementLib.Pojo;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责在顾客付款成功后把购物车中的虚拟商品转换为顾客可持有的实体物品。
    /// </summary>
    public static class CustomerPurchaseDeliveryUtility
    {
        /// <summary>
        /// 将购物车商品交付给顾客，优先放入背包，其次手持，最后落在顾客附近。
        /// </summary>
        public static void DeliverPurchasedItems(Pawn customer, List<CustomerCartItem> items)
        {
            if (customer == null || items.NullOrEmpty()) return;

            for (int i = 0; i < items.Count; i++)
            {
                CustomerCartItem item = items[i];
                if (item == null || item.def == null || item.count <= 0) continue;
                DeliverSingleDef(customer, item.def, item.count);
            }
        }

        /// <summary>
        /// 计算顾客还可以为本次购物车新增的重量，负责让选购阶段避免买到超载商品。
        /// </summary>
        public static float FreeMassForCart(Pawn customer, List<CustomerCartItem> currentCart)
        {
            if (customer == null || !MassUtility.CanEverCarryAnything(customer))
                return 0f;

            float free = MassUtility.FreeSpace(customer);
            float reserved = CalculateCartMass(currentCart);
            return System.Math.Max(0f, free - reserved);
        }

        /// <summary>
        /// 判断一件商品数量是否能被顾客带走，负责同时考虑当前背包和尚未交付的购物车重量。
        /// </summary>
        public static bool CanCarryMore(Pawn customer, List<CustomerCartItem> currentCart, ThingDef def, int count)
        {
            if (customer == null || def == null || count <= 0)
                return false;

            float mass = GetMass(def) * count;
            return mass <= FreeMassForCart(customer, currentCart) + 0.0001f;
        }

        /// <summary>
        /// 计算当前剩余负重最多还能购买多少个指定商品。
        /// </summary>
        public static int MaxAdditionalCountByMass(Pawn customer, List<CustomerCartItem> currentCart, ThingDef def)
        {
            if (customer == null || def == null)
                return 0;

            float mass = GetMass(def);
            if (mass <= 0.0001f)
                return int.MaxValue;

            return UnityEngine.Mathf.FloorToInt(FreeMassForCart(customer, currentCart) / mass);
        }

        /// <summary>
        /// 判断套餐内所有商品是否能被顾客带走。
        /// </summary>
        public static bool CanCarryCombo(Pawn customer, List<CustomerCartItem> currentCart, List<ComboItem> items)
        {
            if (items.NullOrEmpty()) return false;
            return CalculateComboMass(items) <= FreeMassForCart(customer, currentCart) + 0.0001f;
        }

        /// <summary>
        /// 查找顾客身上已经交付的指定物品，负责让购后消费行为使用真实购买物而不是重新生成。
        /// </summary>
        public static Thing FindDeliveredThing(Pawn customer, ThingDef thingDef)
        {
            if (customer == null || thingDef == null)
                return null;

            Thing carried = customer.carryTracker?.CarriedThing;
            if (carried != null && !carried.Destroyed && carried.def == thingDef && carried.stackCount > 0)
                return carried;

            ThingOwner inventory = customer.inventory?.innerContainer;
            if (inventory == null) return null;

            for (int i = 0; i < inventory.Count; i++)
            {
                Thing thing = inventory[i];
                if (thing != null && !thing.Destroyed && thing.def == thingDef && thing.stackCount > 0)
                    return thing;
            }

            return null;
        }

        /// <summary>
        /// 计算购物车内所有商品重量。
        /// </summary>
        private static float CalculateCartMass(List<CustomerCartItem> items)
        {
            if (items.NullOrEmpty()) return 0f;
            float total = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                CustomerCartItem item = items[i];
                if (item == null || item.def == null || item.count <= 0) continue;
                total += GetMass(item.def) * item.count;
            }
            return total;
        }

        /// <summary>
        /// 计算套餐内所有商品重量。
        /// </summary>
        private static float CalculateComboMass(List<ComboItem> items)
        {
            float total = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                ComboItem item = items[i];
                if (item == null || item.def == null || item.count <= 0) continue;
                total += GetMass(item.def) * item.count;
            }
            return total;
        }

        /// <summary>
        /// 读取物品单件重量，负责统一处理缺失重量数据的商品。
        /// </summary>
        private static float GetMass(ThingDef def)
        {
            if (def == null) return 0f;
            return UnityEngine.Mathf.Max(0f, def.GetStatValueAbstract(StatDefOf.Mass));
        }

        /// <summary>
        /// 按堆叠上限创建并交付一种商品，负责避免大堆叠超过 ThingDef 的 stackLimit。
        /// </summary>
        private static void DeliverSingleDef(Pawn customer, ThingDef def, int count)
        {
            int remaining = count;
            int stackLimit = def.stackLimit > 0 ? def.stackLimit : count;

            while (remaining > 0)
            {
                int chunk = System.Math.Min(remaining, stackLimit);
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                if (thing == null) return;

                thing.stackCount = chunk;
                PlaceThingForCustomer(customer, thing);
                remaining -= chunk;
            }
        }

        /// <summary>
        /// 将单个实体物品放到顾客可访问的位置，负责处理背包、手持和落地兜底。
        /// </summary>
        private static void PlaceThingForCustomer(Pawn customer, Thing thing)
        {
            if (customer == null || thing == null || thing.Destroyed)
                return;

            ThingOwner inventory = customer.inventory?.innerContainer;
            if (inventory != null && inventory.TryAdd(thing))
                return;

            if (customer.carryTracker != null && customer.carryTracker.CarriedThing == null)
            {
                try
                {
                    if (customer.carryTracker.TryStartCarry(thing))
                        return;
                }
                catch
                {
                }
            }

            if (customer.Spawned && customer.Map != null)
            {
                GenPlace.TryPlaceThing(thing, customer.Position, customer.Map, ThingPlaceMode.Near, out _);
                return;
            }

            if (!thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);
        }
    }
}
