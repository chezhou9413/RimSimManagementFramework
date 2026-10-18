using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.Tool
{
    //提供自动售货机货柜的查找、可用性、顾客匹配和直接购买工具函数。
    public static class VendingMachineUtility
    {
        private static readonly Dictionary<int, VendingMachineMapCache> MapCaches = new Dictionary<int, VendingMachineMapCache>();
        //判断货柜是否启用了自动售货机能力。
        public static bool IsVendingMachine(Building_SimContainer storage)
        {
            return storage?.GetComp<ThingComp_VendingMachine>() != null;
        }
        //判断自动售货机当前是否能接待顾客。
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
        //返回指定地图上所有自动售货机货柜。
        public static List<Building_SimContainer> GetAllVendingMachines(Map map)
        {
            return new List<Building_SimContainer>(GetVendingMachineSnapshot(map));
        }

        //返回地图自动售货机只读快照，职责是在建筑未变化时避免重复扫描殖民地建筑列表。
        public static IReadOnlyList<Building_SimContainer> GetVendingMachineSnapshot(Map map)
        {
            if (map?.listerBuildings == null)
                return new List<Building_SimContainer>(0);

            int mapId = map.uniqueID;
            if (MapCaches.TryGetValue(mapId, out VendingMachineMapCache cache) && cache.Map == map)
                return cache.Machines;

            List<Building_SimContainer> machines = map.listerBuildings.allBuildingsColonist
                .OfType<Building_SimContainer>()
                .Where(IsVendingMachine)
                .OrderBy(storage => storage.thingIDNumber)
                .ToList();
            MapCaches[mapId] = new VendingMachineMapCache(map, machines);
            return machines;
        }

        //通知地图建筑清单发生变化，职责是让自动售货机快照在下次读取时重建。
        public static void NotifyMapBuildingsChanged(Map map)
        {
            if (map != null)
                MapCaches.Remove(map.uniqueID);
        }
        //按 ThingID 查找自动售货机货柜。
        public static Building_SimContainer FindVendingMachineById(Map map, int thingId)
        {
            if (map == null || thingId < 0) return null;
            IReadOnlyList<Building_SimContainer> machines = GetVendingMachineSnapshot(map);
            for (int i = 0; i < machines.Count; i++)
            {
                Building_SimContainer machine = machines[i];
                if (machine != null && machine.thingIDNumber == thingId)
                    return machine;
            }
            return null;
        }
        //判断顾客类型是否能被该自动售货机吸引。
        public static bool MatchesCustomerKind(Building_SimContainer storage, RuntimeCustomerKind kind)
        {
            if (storage == null || kind == null) return false;
            List<string> targets = kind.GetTargetGoodsCategoryIds();
            if (targets.NullOrEmpty()) return true;

            ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
            string active = comp?.ActiveGoodsDefName;
            return !string.IsNullOrEmpty(active) && targets.Contains(active);
        }

        //返回自动售货机当前顾客数量，职责是读取地图级索引而不扫描全部 Lord。
        public static int CountActiveCustomers(Map map, Building_SimContainer storage)
        {
            if (map == null || storage == null) return 0;
            return map.GetComponent<CustomerArrivalManager>()?.RuntimeIndex.CountActiveForVendingMachine(storage.thingIDNumber) ?? 0;
        }
        //为顾客从自动售货机中购买一件或多件商品。
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
        //按价格意愿选择售货机购买数量，负责让折扣商品更容易多买、高溢价商品更少买。
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
        //判断自动售货机是否有启用商品且真实库存大于零。
        private static bool HasSellableStock(Building_SimContainer storage)
        {
            foreach (ThingDef def in storage.ActiveDefs)
            {
                if (def != null && storage.GetTargetCount(def) > 0 && storage.CountStored(def) > 0)
                    return true;
            }

            return false;
        }

        //类职责：保存单张地图的自动售货机快照及其地图身份。
        private sealed class VendingMachineMapCache
        {
            public readonly Map Map;
            public readonly List<Building_SimContainer> Machines;

            //创建地图售货机缓存，职责是绑定地图实例和稳定建筑列表。
            public VendingMachineMapCache(Map map, List<Building_SimContainer> machines)
            {
                Map = map;
                Machines = machines;
            }
        }
    }
}
