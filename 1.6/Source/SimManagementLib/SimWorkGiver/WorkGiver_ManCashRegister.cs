using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 为营业中或仍有待结账顾客的商店收银台分配收银员值守工作。
    /// </summary>
    public class WorkGiver_ManCashRegister : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        /// <summary>
        /// 枚举当前地图中允许收银的玩家收银台，负责让关店后的待付款顾客仍能完成结账。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerBuildings.allBuildingsColonist
                .Where(b => b is Building_CashRegister register && ShopStaffUtility.CanCashierWorkAt(ShopStaffUtility.FindShopFor(register)));
        }

        /// <summary>
        /// 判断指定 Pawn 是否能在该收银台执行收银工作，负责保留关店后的清队列服务。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null) return false;
            SimZone.Zone_Shop shop = ShopStaffUtility.FindShopFor(register);
            if (!ShopStaffUtility.CanCashierWorkAt(shop)) return false;
            if (!ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, def))
                return false;

            // 收银台值守需要独占建筑，避免多个店员同时占用同一台收银台。
            if (!pawn.CanReserve(register, 1, -1, null, forced)) return false;

            return true;
        }

        /// <summary>
        /// 创建收银台值守 Job。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_ManCashRegister"), t);
        }
    }
}
