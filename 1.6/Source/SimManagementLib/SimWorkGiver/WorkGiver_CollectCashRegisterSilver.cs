using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 为营业中的商店收银台分配取走已结算银币的工作。
    /// </summary>
    public class WorkGiver_CollectCashRegisterSilver : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// 枚举当前地图中属于营业商店的玩家收银台。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map == null) yield break;

            foreach (Building_CashRegister register in pawn.Map.listerBuildings.allBuildingsColonist.OfType<Building_CashRegister>())
            {
                if (register != null && ShopStaffUtility.IsShopOpenForWork(ShopStaffUtility.FindShopFor(register)))
                    yield return register;
            }
        }

        /// <summary>
        /// 判断指定 Pawn 是否能从该收银台取走可提取银币。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null || register.Destroyed || !register.Spawned) return false;
            if (!ShopStaffUtility.IsShopOpenForWork(ShopStaffUtility.FindShopFor(register))) return false;
            if (register.IsManned) return false; // 下班后再统一取钱
            if (register.AvailableForWithdraw <= 0) return false;
            if (!pawn.CanReserve(register, 1, -1, null, forced)) return false;
            if (!pawn.CanReach(register, PathEndMode.Touch, Danger.Deadly)) return false;
            return true;
        }

        /// <summary>
        /// 创建从收银台取出一批银币的 Job。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null) return null;

            int batchSize = Mathf.Max(1, ThingDefOf.Silver.stackLimit);
            int desired = Mathf.Min(register.AvailableForWithdraw, batchSize);
            int reserved = register.ReserveWithdrawSilver(desired);
            if (reserved <= 0) return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_CollectCashRegisterSilver"), register);
            job.count = reserved;
            return job;
        }
    }
}
