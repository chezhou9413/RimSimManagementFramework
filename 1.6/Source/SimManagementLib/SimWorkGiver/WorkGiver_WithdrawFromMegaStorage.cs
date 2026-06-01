using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 扫描 Building_SimContainer，找出超出目标量或已被移出配置的物品，
    /// 分配 JobDriver_WithdrawFromMegaStorage 任务让 pawn 走过去取走。
    /// </summary>
    public class WorkGiver_WithdrawFromMegaStorage : WorkGiver_Scanner
    {
        private static WorkGiverDef cachedWorkGiverDef;

        /// <summary>
        /// 获取当前清理库存 WorkGiverDef，避免每次扫描都查询 DefDatabase。
        /// </summary>
        private static WorkGiverDef CurrentWorkGiverDef
        {
            get
            {
                if (cachedWorkGiverDef == null)
                    cachedWorkGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("WithdrawFromMegaStorage");
                return cachedWorkGiverDef;
            }
        }

        /// <summary>
        /// 返回地图上的货柜候选。这里只做轻量营业状态过滤，避免候选枚举阶段反复扫描虚拟库存。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map?.listerBuildings == null) yield break;

            List<Building> buildings = pawn.Map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_SimContainer storage = buildings[i] as Building_SimContainer;
                if (storage != null && IsAllowedByBusinessState(storage))
                    yield return storage;
            }
        }

        /// <summary>
        /// 判断指定货柜是否存在需要移出的多余商品。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return false;
            storage.ReconcilePendingReservations();
            if (!HasExcess(storage, pawn)) return false;
            if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) return false;
            return true;
        }

        /// <summary>
        /// 为指定货柜生成移出多余商品的任务。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return null;
            storage.ReconcilePendingReservations();
            if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) return null;

            foreach ((ThingDef td, int excess) in storage.GetExcessItems())
            {
                if (excess <= 0) continue;

                int reserved = storage.ReservePendingOut(td, excess);
                if (reserved <= 0) continue;

                Job job = JobMaker.MakeJob(
                    DefDatabase<JobDef>.GetNamed("WithdrawFromMegaStorage"),
                    storage);
                job.count = reserved;
                job.plantDefToSow = td;
                return job;
            }
            return null;
        }

        /// <summary>
        /// 判断货柜是否有超过目标量或已移出配置的库存。
        /// </summary>
        private static bool HasExcess(Building_SimContainer storage, Pawn pawn)
        {
            if (storage.Destroyed || !storage.Spawned) return false;
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            if (!VendingMachineUtility.IsVendingMachine(storage) && !ShopStaffUtility.IsShopOpenForWork(shop)) return false;
            if (!VendingMachineUtility.IsVendingMachine(storage) && !ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, CurrentWorkGiverDef))
                return false;

            foreach ((ThingDef _, int excess) in storage.GetExcessItems())
            {
                if (excess > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断货柜是否允许被清理库存扫描，自动售货机不依赖商店营业状态。
        /// </summary>
        private static bool IsAllowedByBusinessState(Building_SimContainer storage)
        {
            if (VendingMachineUtility.IsVendingMachine(storage))
                return true;
            return ShopStaffUtility.IsShopOpenForWork(ShopStaffUtility.FindShopFor(storage));
        }
    }
}
