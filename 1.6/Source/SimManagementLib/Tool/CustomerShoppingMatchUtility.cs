using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 统一判断顾客与商品、套餐、服务是否匹配，负责让生成、浏览和实际购买使用同一套目标分类规则。
    /// </summary>
    internal static class CustomerShoppingMatchUtility
    {
        /// <summary>
        /// 判断顾客是否会把指定商品当成目标商品。
        /// </summary>
        public static bool ThingMatchesCustomer(LordJob_CustomerVisit visit, ThingDef thingDef)
        {
            return ThingMatchesCustomer(visit?.RuntimeCustomerKind, visit?.customerKind, thingDef);
        }

        /// <summary>
        /// 判断顾客是否会把指定商品当成目标商品，目标商品分类为空时表示可接受任意商品。
        /// </summary>
        public static bool ThingMatchesCustomer(RuntimeCustomerKind kind, CustomerKindDef fallbackDef, ThingDef thingDef)
        {
            if (thingDef == null) return false;
            if (!AllowsGoods(kind, fallbackDef)) return false;

            List<string> targetIds = GetTargetGoodsCategoryIds(kind, fallbackDef);
            if (targetIds.NullOrEmpty()) return true;

            for (int i = 0; i < targetIds.Count; i++)
            {
                if (GoodsCatalog.Contains(targetIds[i], thingDef))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断商店是否存在顾客目标范围内的有货商品或可用服务，用于生成顾客前的粗筛。
        /// </summary>
        public static bool ShopHasMatchingSellableGoodsOrServices(Zone_Shop shop, RuntimeCustomerKind kind, CustomerKindDef fallbackDef)
        {
            if (shop == null) return false;

            if (AllowsGoods(kind, fallbackDef))
            {
                foreach (Building_SimContainer storage in ShopDataUtility.GetStoragesInZone(shop))
                {
                    if (StorageHasMatchingSellableStock(storage, kind, fallbackDef))
                        return true;
                }
            }

            return ShopServiceUtility.HasUsableServiceProvider(shop, null, GetTargetServiceCategoryIds(kind, fallbackDef));
        }

        /// <summary>
        /// 判断商店当前是否仍有顾客买得起的目标商品或服务，用于浏览阶段停止无效闲逛。
        /// </summary>
        public static bool ShopHasMatchingAffordableGoodsOrServices(Pawn pawn, Zone_Shop shop, LordJob_CustomerVisit visit, float remainingBudget)
        {
            return ShopHasMatchingAffordableGoodsOrServices(pawn, shop, visit?.RuntimeCustomerKind, visit?.customerKind, remainingBudget);
        }

        /// <summary>
        /// 判断商店当前是否仍有顾客买得起的目标商品或服务。
        /// </summary>
        public static bool ShopHasMatchingAffordableGoodsOrServices(Pawn pawn, Zone_Shop shop, RuntimeCustomerKind kind, CustomerKindDef fallbackDef, float remainingBudget)
        {
            if (shop == null || remainingBudget <= 0f) return false;

            if (ShopHasMatchingAffordableGoods(pawn, shop, kind, fallbackDef, remainingBudget))
                return true;

            return ShopServiceUtility.TryFindServiceForCustomer(
                pawn,
                shop,
                remainingBudget,
                GetTargetServiceCategoryIds(kind, fallbackDef),
                out _,
                out _,
                out _);
        }

        /// <summary>
        /// 判断商店当前是否仍有顾客买得起的目标商品或套餐，不包含服务。
        /// </summary>
        public static bool ShopHasMatchingAffordableGoods(Pawn pawn, Zone_Shop shop, LordJob_CustomerVisit visit, float remainingBudget)
        {
            return ShopHasMatchingAffordableGoods(pawn, shop, visit?.RuntimeCustomerKind, visit?.customerKind, remainingBudget);
        }

        /// <summary>
        /// 判断商店当前是否仍有顾客买得起的目标商品或套餐，不包含服务。
        /// </summary>
        public static bool ShopHasMatchingAffordableGoods(Pawn pawn, Zone_Shop shop, RuntimeCustomerKind kind, CustomerKindDef fallbackDef, float remainingBudget)
        {
            if (shop == null || remainingBudget <= 0f || !AllowsGoods(kind, fallbackDef)) return false;

            foreach (Building_SimContainer storage in ShopDataUtility.GetStoragesInZone(shop))
            {
                if (StorageHasMatchingAffordableStock(storage, pawn, kind, fallbackDef, remainingBudget))
                    return true;
            }

            return !GetMatchingAffordableInStockCombos(shop, kind, fallbackDef, remainingBudget).NullOrEmpty();
        }

        /// <summary>
        /// 返回当前商店内有库存且符合顾客目标分类的商品汇总。
        /// </summary>
        public static List<ShopItemStatus> GetMatchingInStockGoods(Zone_Shop shop, LordJob_CustomerVisit visit)
        {
            return GetMatchingInStockGoods(shop, visit?.RuntimeCustomerKind, visit?.customerKind);
        }

        /// <summary>
        /// 返回当前商店内有库存且符合顾客目标分类的商品汇总。
        /// </summary>
        public static List<ShopItemStatus> GetMatchingInStockGoods(Zone_Shop shop, RuntimeCustomerKind kind, CustomerKindDef fallbackDef)
        {
            if (shop == null || !AllowsGoods(kind, fallbackDef))
                return new List<ShopItemStatus>();

            return ShopDataUtility.GetInStockGoods(shop)
                .Where(item => item?.Def != null && ThingMatchesCustomer(kind, fallbackDef, item.Def))
                .ToList();
        }

        /// <summary>
        /// 返回顾客当前买得起且至少含有一个目标商品的套餐。
        /// </summary>
        public static List<ComboData> GetMatchingAffordableInStockCombos(Zone_Shop shop, LordJob_CustomerVisit visit, float remainingBudget)
        {
            return GetMatchingAffordableInStockCombos(shop, visit?.RuntimeCustomerKind, visit?.customerKind, remainingBudget);
        }

        /// <summary>
        /// 返回顾客当前买得起且至少含有一个目标商品的套餐。
        /// </summary>
        public static List<ComboData> GetMatchingAffordableInStockCombos(Zone_Shop shop, RuntimeCustomerKind kind, CustomerKindDef fallbackDef, float remainingBudget)
        {
            if (shop == null || remainingBudget <= 0f || !AllowsGoods(kind, fallbackDef))
                return new List<ComboData>();

            return ShopDataUtility.GetAffordableInStockCombos(shop, remainingBudget)
                .Where(combo => ComboMatchesCustomer(kind, fallbackDef, combo))
                .ToList();
        }

        /// <summary>
        /// 判断套餐是否至少含有一个顾客目标商品。
        /// </summary>
        public static bool ComboMatchesCustomer(RuntimeCustomerKind kind, CustomerKindDef fallbackDef, ComboData combo)
        {
            if (combo == null || combo.items.NullOrEmpty()) return false;

            bool hasValidItem = false;
            for (int i = 0; i < combo.items.Count; i++)
            {
                ComboItem item = combo.items[i];
                if (item == null || item.def == null || item.count <= 0)
                    continue;
                hasValidItem = true;
                if (!ThingMatchesCustomer(kind, fallbackDef, item.def))
                    continue;
                return true;
            }

            return hasValidItem && GetTargetGoodsCategoryIds(kind, fallbackDef).NullOrEmpty();
        }

        /// <summary>
        /// 判断货柜是否至少有一个符合顾客目标分类且有库存的商品。
        /// </summary>
        public static bool StorageHasMatchingSellableStock(Building_SimContainer storage, RuntimeCustomerKind kind, CustomerKindDef fallbackDef)
        {
            if (storage == null || storage.Destroyed || !AllowsGoods(kind, fallbackDef)) return false;

            foreach (ThingDef def in storage.ActiveDefs)
            {
                if (def == null || storage.CountStored(def) <= 0) continue;
                if (ThingMatchesCustomer(kind, fallbackDef, def))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断货柜是否至少有一个符合顾客目标分类、有库存且买得起的商品。
        /// </summary>
        public static bool StorageHasMatchingAffordableStock(Building_SimContainer storage, LordJob_CustomerVisit visit, float remainingBudget)
        {
            return StorageHasMatchingAffordableStock(storage, visit?.RuntimeCustomerKind, visit?.customerKind, remainingBudget);
        }

        /// <summary>
        /// 判断货柜是否至少有一个符合顾客目标分类、有库存且买得起的商品。
        /// </summary>
        public static bool StorageHasMatchingAffordableStock(Building_SimContainer storage, Pawn pawn, LordJob_CustomerVisit visit, float remainingBudget)
        {
            return StorageHasMatchingAffordableStock(storage, pawn, visit?.RuntimeCustomerKind, visit?.customerKind, remainingBudget);
        }

        /// <summary>
        /// 判断货柜是否至少有一个符合顾客目标分类、有库存且买得起的商品。
        /// </summary>
        public static bool StorageHasMatchingAffordableStock(Building_SimContainer storage, RuntimeCustomerKind kind, CustomerKindDef fallbackDef, float remainingBudget)
        {
            return StorageHasMatchingAffordableStock(storage, null, kind, fallbackDef, remainingBudget);
        }

        /// <summary>
        /// 判断货柜是否至少有一个符合顾客目标分类、有库存且买得起的商品。
        /// </summary>
        public static bool StorageHasMatchingAffordableStock(Building_SimContainer storage, Pawn pawn, RuntimeCustomerKind kind, CustomerKindDef fallbackDef, float remainingBudget)
        {
            if (storage == null || storage.Destroyed || remainingBudget <= 0f || !AllowsGoods(kind, fallbackDef)) return false;

            foreach (ThingDef def in storage.ActiveDefs)
            {
                if (def == null || storage.CountStored(def) <= 0) continue;
                if (!ThingMatchesCustomer(kind, fallbackDef, def)) continue;
                if (ShopPricingUtility.GetUnitPrice(storage, def) <= remainingBudget)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断货柜是否含有指定套餐需要的目标商品，用于让顾客浏览与套餐相关的货柜。
        /// </summary>
        public static bool StorageHasComboItem(Building_SimContainer storage, IEnumerable<ComboData> combos)
        {
            if (storage == null || storage.Destroyed || combos == null) return false;

            foreach (ComboData combo in combos)
            {
                if (combo?.items == null) continue;
                for (int i = 0; i < combo.items.Count; i++)
                {
                    ThingDef def = combo.items[i]?.def;
                    if (def != null && combo.items[i].count > 0 && storage.CountStored(def) > 0)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 返回顾客目标服务分类，空列表表示服务分类不受限制。
        /// </summary>
        public static List<string> GetTargetServiceCategoryIds(RuntimeCustomerKind kind, CustomerKindDef fallbackDef)
        {
            if (kind != null)
                return kind.GetTargetServiceCategoryIds();
            return fallbackDef?.GetTargetServiceCategoryIds() ?? new List<string>();
        }

        /// <summary>
        /// 返回顾客目标商品分类，空列表表示商品分类不受限制。
        /// </summary>
        private static List<string> GetTargetGoodsCategoryIds(RuntimeCustomerKind kind, CustomerKindDef fallbackDef)
        {
            if (kind != null)
                return kind.GetTargetGoodsCategoryIds();
            return fallbackDef?.GetTargetGoodsCategoryIds() ?? new List<string>();
        }

        /// <summary>
        /// 判断该顾客是否应考虑商品购买，服务专属顾客不会被任意商品吸引。
        /// </summary>
        private static bool AllowsGoods(RuntimeCustomerKind kind, CustomerKindDef fallbackDef)
        {
            List<string> goodsTargets = GetTargetGoodsCategoryIds(kind, fallbackDef);
            List<string> serviceTargets = GetTargetServiceCategoryIds(kind, fallbackDef);
            return !goodsTargets.NullOrEmpty() || serviceTargets.NullOrEmpty();
        }
    }
}
