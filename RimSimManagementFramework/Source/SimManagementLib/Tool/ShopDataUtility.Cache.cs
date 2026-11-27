using SimManagementLib.GameComp;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Tool
{
    //类职责：提供商店设施快照和精确失效入口，隔离高频读取与建筑生命周期通知。
    public static partial class ShopDataUtility
    {
        //返回区划货柜只读快照，职责是让商店区复用缓存并兼容普通区划即时扫描。
        public static IReadOnlyList<Building_SimContainer> GetStorageSnapshotInZone(Zone zone)
        {
            if (zone is Zone_Shop shop)
                return shop.GetStorageSnapshot();
            return ScanStorages(zone);
        }

        //枚举所有可用于经营的货柜，管理窗口也可读取逐座停售的建筑。
        public static IReadOnlyList<IShopStorage> GetRetailStorages(Zone zone, bool includeDisabled = false)
        {
            if (zone is Zone_Shop shop) return shop.RetailStorages(includeDisabled);
            return GetStorageSnapshotInZone(zone);
        }

        //返回商店收银台只读快照，职责是复用商店区设施缓存。
        public static IReadOnlyList<Building_CashRegister> GetCashRegisterSnapshotInZone(Zone_Shop shop)
        {
            return shop?.GetCashRegisterSnapshot() ?? new List<Building_CashRegister>(0);
        }

        //通知区划内建筑发生变化，职责是同步失效设施、营业状态、岗位和经营指标缓存。
        public static void NotifyBuildingChanged(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map))
                return;

            Zone_Shop shop = map.zoneManager?.ZoneAt(cell) as Zone_Shop;
            if (shop == null)
                return;

            NotifyShopFacilityChanged(map, shop);
        }

        //通知指定建筑的经营状态变化，并在建筑处于商店区空洞或部分覆盖时立即归并回所属商店。
        internal static void NotifyBuildingChanged(Building building)
        {
            if (building == null || !building.Spawned || building.Map == null)
                return;

            Map map = building.Map;
            ZoneManager zoneManager = map.zoneManager;
            if (zoneManager == null) return;

            HashSet<Zone_Shop> shops = new HashSet<Zone_Shop>();
            CellRect searchRect = building.OccupiedRect().ExpandedBy(1);
            for (int z = searchRect.minZ; z <= searchRect.maxZ; z++)
            {
                for (int x = searchRect.minX; x <= searchRect.maxX; x++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    if (c.InBounds(map) && zoneManager.ZoneAt(c) is Zone_Shop shop)
                        shops.Add(shop);
                }
            }
            if (shops.Count == 0) return;

            if (ShopStorage.IsFacility(building) || building is Building_CashRegister || ShopServiceUtility.HasEnabledService(building))
            {
                foreach (Zone_Shop shop in shops)
                    if (shop.TryRepairFacilityCoverage(building)) break;
            }

            foreach (Zone_Shop shop in shops)
                NotifyShopFacilityChanged(map, shop);
        }

        //统一刷新单个商店的设施、运行态、岗位和经营统计缓存。
        private static void NotifyShopFacilityChanged(Map map, Zone_Shop shop)
        {
            shop.InvalidateShopFacilityCache();
            shop.InvalidateShopRuntimeCache();
            ShopStaffUtility.NotifyShopChanged(shop);
            Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.InvalidateShopMetrics(shop);
            map.GetComponent<CustomerArrivalManager>()?.NotifyShopDirty(shop);
        }

        //通知货柜库存或配置发生变化，职责是只失效依赖商品状态的经营指标。
        public static void NotifyShopContentsChanged(Building_SimContainer storage)
        {
            NotifyShopContentsChanged((Building)storage);
        }

        //宿主实物或货柜设置变化时刷新所属店铺。
        internal static void NotifyShopContentsChanged(Building storage)
        {
            Zone_Shop shop = storage?.Map?.zoneManager.ZoneAt(storage.Position) as Zone_Shop;
            if (shop == null)
                return;

            Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.InvalidateShopMetrics(shop);
            storage.Map?.GetComponent<CustomerArrivalManager>()?.NotifyShopDirty(shop);
        }

        //扫描普通区划中的货柜，职责是为非商店调用保留无缓存兼容路径。
        private static List<Building_SimContainer> ScanStorages(Zone zone)
        {
            List<Building_SimContainer> storages = new List<Building_SimContainer>();
            if (zone?.Map == null)
                return storages;

            HashSet<Building_SimContainer> seen = new HashSet<Building_SimContainer>();
            foreach (IntVec3 cell in zone.Cells)
            {
                List<Thing> things = zone.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Building_SimContainer storage = things[i] as Building_SimContainer;
                    if (storage != null && !storage.Destroyed && storage.Spawned && seen.Add(storage))
                        storages.Add(storage);
                }
            }

            return storages;
        }

    }
}
