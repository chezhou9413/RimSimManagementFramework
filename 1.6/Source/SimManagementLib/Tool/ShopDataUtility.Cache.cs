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

            shop.InvalidateShopFacilityCache();
            shop.InvalidateShopRuntimeCache();
            ShopStaffUtility.NotifyShopChanged(shop);
            Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.InvalidateShopMetrics(shop);
            map.GetComponent<CustomerArrivalManager>()?.NotifyShopDirty(shop);
        }

        //通知货柜库存或配置发生变化，职责是只失效依赖商品状态的经营指标。
        public static void NotifyShopContentsChanged(Building_SimContainer storage)
        {
            Zone_Shop shop = storage == null ? null : FindShopForStorage(storage);
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

        //查找货柜所在商店，职责是使用建筑根格直接定位缓存所属区划。
        private static Zone_Shop FindShopForStorage(Building_SimContainer storage)
        {
            if (storage?.Map == null || !storage.Spawned)
                return null;
            return storage.Map.zoneManager?.ZoneAt(storage.Position) as Zone_Shop;
        }
    }
}
