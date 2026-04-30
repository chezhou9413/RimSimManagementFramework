using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    public class WorkGiver_ManCashRegister : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerBuildings.allBuildingsColonist.Where(b => b is Building_CashRegister);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null) return false;
            if (!ShopStaffUtility.AllowsPawnForWorkGiver(ShopStaffUtility.FindShopFor(register), pawn, def))
                return false;

            // 【修改】：改回 1 和 -1。收银员是主宰，绝对独占机器！
            if (!pawn.CanReserve(register, 1, -1, null, forced)) return false;

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_ManCashRegister"), t);
        }
    }
}
