using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimZone
{
    //类职责：缓存商店区内的货柜和收银台，避免经营与顾客逻辑反复遍历全部区划格。
    public partial class Zone_Shop
    {
        private readonly List<Building_SimContainer> cachedStorages = new List<Building_SimContainer>();
        private readonly List<Building_CashRegister> cachedCashRegisters = new List<Building_CashRegister>();
        private readonly List<IShopStorage> retailStorages = new();
        private readonly List<IShopStorage> allStorages = new();

        //供经营与管理共用，管理列表也包含已停售的宿主建筑。
        internal IReadOnlyList<IShopStorage> RetailStorages(bool includeDisabled)
        {
            RefreshFacilityCacheIfNeeded();
            return includeDisabled ? allStorages : retailStorages;
        }

        private bool facilityCacheDirty = true;

        //返回商店当前货柜快照，职责是在区划或建筑未变化时复用稳定列表。
        internal IReadOnlyList<Building_SimContainer> GetStorageSnapshot()
        {
            RefreshFacilityCacheIfNeeded();
            return cachedStorages;
        }

        //返回商店当前收银台快照，职责是在区划或建筑未变化时复用稳定列表。
        internal IReadOnlyList<Building_CashRegister> GetCashRegisterSnapshot()
        {
            RefreshFacilityCacheIfNeeded();
            return cachedCashRegisters;
        }

        //失效商店设施缓存，职责是在区划和建筑发生变化后延迟到下次读取再重建。
        internal void InvalidateShopFacilityCache()
        {
            facilityCacheDirty = true;
            facilityCoverageDirty = true;
        }

        //按商店格重建设施快照，职责是一次扫描同时收集货柜和收银台并去重。
        private void RefreshFacilityCacheIfNeeded()
        {
            if (!facilityCacheDirty)
                return;

            facilityCacheDirty = false;
            cachedStorages.Clear();
            retailStorages.Clear();
            allStorages.Clear();
            cachedCashRegisters.Clear();
            if (Map == null || Cells == null)
                return;

            HashSet<Thing> seen = new HashSet<Thing>();
            foreach (IntVec3 cell in Cells)
            {
                List<Thing> things = Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || !thing.Spawned || !seen.Add(thing))
                        continue;
                    if (thing is Building_SimContainer storage)
                        cachedStorages.Add(storage);
                    IShopStorage retail = ShopStorage.For(thing);
                    if (retail != null && (retail is Building_SimContainer || retail.Host.Faction == Faction.OfPlayer && ContainsCell(retail.Host.Position)))
                    {
                        allStorages.Add(retail);
                        if (retail is Building_SimContainer || retail.AllowsCustomerSelfPurchase) retailStorages.Add(retail);
                    }
                    if (thing is Building_CashRegister register)
                        cachedCashRegisters.Add(register);
                }
            }
        }
    }
}
