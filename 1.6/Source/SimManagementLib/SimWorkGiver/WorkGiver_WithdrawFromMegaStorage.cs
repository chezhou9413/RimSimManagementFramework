using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
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
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Thing t in pawn.Map.listerBuildings.allBuildingsColonist.OfType<Building_SimContainer>())
            {
                if (t is Building_SimContainer storage && IsAllowedByBusinessState(storage) && HasExcess(storage, pawn))
                    yield return storage;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return false;
            if (!HasExcess(storage, pawn)) return false;
            if (!pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly)) return false;
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage)) return null;
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

        private static bool HasExcess(Building_SimContainer storage, Pawn pawn)
        {
            if (storage.Destroyed || !storage.Spawned) return false;
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            if (!VendingMachineUtility.IsVendingMachine(storage) && !ShopStaffUtility.IsShopOpenForWork(shop)) return false;
            WorkGiverDef currentDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("WithdrawFromMegaStorage");
            if (!VendingMachineUtility.IsVendingMachine(storage) && !ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, currentDef))
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
