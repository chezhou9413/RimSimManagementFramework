using RimWorld;
using Verse;

namespace SimManagementLib.SimWorkGiver
{
    //有顾客等待时通过原版优先工作调度临时接待，保留玩家工作设置和人工指令。
    public sealed class WorkGiver_ServeWaitingCustomers : WorkGiver_ManCashRegister
    {
        protected override bool WaitingCustomersOnly => true;

        //吃饭、睡眠、征召及急迫需求中的员工不参加临时补位。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (base.ShouldSkip(pawn, forced)) return true;
            if (forced) return false;
            return pawn.CurJob?.playerForced == true || !pawn.Awake() || pawn.CurJobDef == JobDefOf.Ingest;
        }
    }
}
