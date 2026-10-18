using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 为带现金库存的经营建筑分配取走已结算银币的服务工作。
    /// </summary>
    public class WorkGiver_CollectCashRegisterSilver : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// 枚举当前地图中带现金库存的玩家建筑。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null) yield break;

            foreach (Building building in pawn.Map.listerBuildings.allBuildingsColonist.OfType<Building>())
            {
                if (building?.GetComp<ThingComp_CashStorage>() != null)
                    yield return building;
            }
        }

        /// <summary>
        /// 判断指定 Pawn 是否能从该建筑取走达到阈值的可提取银币。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t == null || t.Destroyed || !t.Spawned) return false;
            ThingComp_CashStorage cash = t.TryGetComp<ThingComp_CashStorage>();
            if (cash == null || !cash.ShouldAutoWithdraw()) return false;
            Building_CashRegister register = t as Building_CashRegister;
            if (register != null)
            {
                if (!ShopStaffUtility.IsShopOpenForWork(ShopStaffUtility.FindShopFor(register))) return false;
            }
            if (!pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly)) return false;
            return true;
        }

        /// <summary>
        /// 创建从经营建筑取出一批银币的 Job。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            ThingComp_CashStorage cash = t?.TryGetComp<ThingComp_CashStorage>();
            if (cash == null) return null;

            int desired = cash.AutoWithdrawAmount;
            if (desired <= 0) return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_CollectCashRegisterSilver"), t);
            job.count = desired;
            return job;
        }
    }
}
