using SimManagementLib.SimMapComp;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //类职责：执行顾客最终幂等退出，集中释放结账票据、运行 Job、Lord 和地图索引。
    internal static class CustomerExitUtility
    {
        //强制顾客退出地图，职责是在自然离店失败后调用原版 ExitMap 完成最终清理。
        public static void ForceExit(Pawn pawn, string reason)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map == null)
                return;

            Map map = pawn.Map;
            CustomerArrivalManager manager = map.GetComponent<CustomerArrivalManager>();
            manager?.NotifyForcedExit();
            manager?.ReleaseCheckoutTicket(pawn);
            manager?.UnregisterCustomer(pawn);
            if (pawn.jobs?.curJob != null)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false, true);

            Lord oldLord = map.lordManager?.LordOf(pawn);
            oldLord?.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            if (pawn.Spawned && pawn.Map == map)
                pawn.ExitMap(false, CellRect.WholeMap(map).GetClosestEdge(pawn.Position));

            SimDebugLogger.Journey("RSMF.CustomerExit", reason ?? "顾客强制离店", pawn, null, -1);
        }
    }
}
