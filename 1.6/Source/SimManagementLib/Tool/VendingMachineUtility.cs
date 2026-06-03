using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供自动售货机货柜的查找、可用性、顾客匹配和直接购买工具函数。
    /// </summary>
    public static class VendingMachineUtility
    {
        /// <summary>
        /// 判断货柜是否启用了自动售货机能力。
        /// </summary>
        public static bool IsVendingMachine(Building_SimContainer storage)
        {
            return storage?.GetComp<ThingComp_VendingMachine>() != null;
        }

        /// <summary>
        /// 判断自动售货机当前是否能接待顾客。
        /// </summary>
        public static bool IsUsableVendingMachine(Building_SimContainer storage)
        {
            if (storage == null || storage.Destroyed || !storage.Spawned) return false;
            ThingComp_VendingMachine comp = storage.GetComp<ThingComp_VendingMachine>();
            if (comp == null || !comp.enabled) return false;
            CompPowerTrader power = storage.GetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return false;
            CompFlickable flickable = storage.GetComp<CompFlickable>();
            if (flickable != null && !flickable.SwitchIsOn) return false;
            return HasSellableStock(storage);
        }

        /// <summary>
        /// 返回指定地图上所有自动售货机货柜。
        /// </summary>
        public static List<Building_SimContainer> GetAllVendingMachines(Map map)
        {
            if (map?.listerBuildings == null) return new List<Building_SimContainer>();
            return map.listerBuildings.allBuildingsColonist
                .OfType<Building_SimContainer>()
                .Where(IsVendingMachine)
                .OrderBy(s => s.thingIDNumber)
                .ToList();
        }

        /// <summary>
        /// 按 ThingID 查找自动售货机货柜。
        /// </summary>
        public static Building_SimContainer FindVendingMachineById(Map map, int thingId)
        {
            if (map?.listerBuildings == null || thingId < 0) return null;
            return map.listerBuildings.allBuildingsColonist
                .OfType<Building_SimContainer>()
                .FirstOrDefault(s => s.thingIDNumber == thingId && IsVendingMachine(s));
        }

        /// <summary>
        /// 判断顾客类型是否能被该自动售货机吸引。
        /// </summary>
        public static bool MatchesCustomerKind(Building_SimContainer storage, RuntimeCustomerKind kind)
        {
            if (storage == null || kind == null) return false;
            List<string> targets = kind.GetTargetGoodsCategoryIds();
            if (targets.NullOrEmpty()) return true;

            ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
            string active = comp?.ActiveGoodsDefName;
            return !string.IsNullOrEmpty(active) && targets.Contains(active);
        }

        /// <summary>
        /// 返回自动售货机当前顾客数量。
        /// </summary>
        public static int CountActiveCustomers(Map map, Building_SimContainer storage)
        {
            if (map?.lordManager == null || storage == null) return 0;
            int count = 0;
            for (int i = 0; i < map.lordManager.lords.Count; i++)
            {
                Lord lord = map.lordManager.lords[i];
                LordJob_VendingMachineVisit visit = lord?.LordJob as LordJob_VendingMachineVisit;
                if (visit == null || visit.vendingMachineThingId != storage.thingIDNumber) continue;
                for (int p = 0; p < lord.ownedPawns.Count; p++)
                {
                    Pawn pawn = lord.ownedPawns[p];
                    if (pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned)
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 为顾客从自动售货机中购买一件或多件商品。
        /// </summary>
        public static bool TryPurchaseBestItem(Pawn pawn, LordJob_VendingMachineVisit visit, Building_SimContainer machine, out ThingDef boughtDef, out int count, out float paid, out float cost)
        {
            boughtDef = null;
            count = 0;
            paid = 0f;
            cost = 0f;
            if (pawn == null || visit == null || machine == null) return false;

            int pawnId = pawn.thingIDNumber;
            int budget = visit.GetBudgetForPawn(pawnId);
            if (budget <= 0) return false;

            CustomerPriceSensitivityProps sensitivity = visit.GetPriceSensitivity(pawnId);
            List<(ThingDef def, float unitPrice, CustomerPriceEvaluation price)> candidates = machine.ActiveDefs
                .Where(def => def != null && machine.CountStored(def) > 0)
                .Select(def =>
                {
                    float candidateUnitPrice = ShopPricingUtility.GetUnitPrice(machine, def);
                    CustomerPriceEvaluation price = CustomerPriceUtility.Evaluate(def, candidateUnitPrice, sensitivity);
                    return (def, unitPrice: candidateUnitPrice, price);
                })
                .Where(candidate => candidate.unitPrice <= budget && !candidate.price.rejected)
                .ToList();
            if (candidates.NullOrEmpty()) return false;

            (ThingDef selected, float unitPrice, CustomerPriceEvaluation priceEvaluation) = candidates.RandomElementByWeight(candidate =>
                Mathf.Max(0.001f, visit.GetPreferenceMultiplier(pawnId, candidate.def) * candidate.price.purchaseWeight));
            int maxByBudget = Mathf.FloorToInt(budget / unitPrice);
            int maxByStock = machine.CountStored(selected);
            int buyCount = PickPurchaseCount(maxByBudget, maxByStock, priceEvaluation);

            Thing taken = machine.TryVirtualBuy(selected, buyCount, out _);
            if (taken == null || taken.stackCount <= 0) return false;

            boughtDef = selected;
            count = taken.stackCount;
            paid = unitPrice * count;
            cost = Mathf.Max(0f, selected.BaseMarketValue * count);
            taken.Destroy(DestroyMode.Vanish);
            return paid > 0f;
        }

        /// <summary>
        /// 按价格意愿选择售货机购买数量，负责让折扣商品更容易多买、高溢价商品更少买。
        /// </summary>
        private static int PickPurchaseCount(int maxByBudget, int maxByStock, CustomerPriceEvaluation price)
        {
            int maxCount = Mathf.Min(maxByBudget, maxByStock);
            if (maxCount <= 1)
                return 1;
            if (price.ratio <= 0.9f)
                return Mathf.Clamp(Rand.RangeInclusive(1, Mathf.Min(3, maxCount)), 1, maxCount);
            if (price.ratio > 1.5f)
                return 1;
            return Mathf.Clamp(maxByBudget > 1 ? Rand.RangeInclusive(1, maxByBudget) : 1, 1, maxByStock);
        }

        /// <summary>
        /// 判断自动售货机是否有启用商品且真实库存大于零。
        /// </summary>
        private static bool HasSellableStock(Building_SimContainer storage)
        {
            foreach (ThingDef def in storage.ActiveDefs)
            {
                if (def != null && storage.GetTargetCount(def) > 0 && storage.CountStored(def) > 0)
                    return true;
            }

            return false;
        }
    }
}
