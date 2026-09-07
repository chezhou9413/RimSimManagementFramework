using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //类职责：执行顾客最终幂等退出，集中释放结账票据、运行 Job、Lord 和地图索引。
    internal static class CustomerExitUtility
    {
        //开始紧急冲刺离图，职责是先释放经营状态，再交给原版离图 Lord 寻路到地图边缘。
        public static void BeginEmergencyExit(Pawn pawn, string reason)
        {
            if (!TryPrepareExit(pawn, out Map map))
                return;

            LordJob_ExitMapBest exitJob = new LordJob_ExitMapBest(LocomotionUrgency.Sprint, false, false);
            LordMaker.MakeNewLord(pawn.Faction, exitJob, map, new List<Pawn> { pawn });
            SimDebugLogger.Journey("RSMF.CustomerExit", reason ?? "顾客紧急离店", pawn, null, -1);
        }

        //强制顾客退出地图，职责是在自然离店失败后调用原版 ExitMap 完成最终清理。
        public static void ForceExit(Pawn pawn, string reason)
        {
            if (!TryPrepareExit(pawn, out Map map))
                return;

            if (pawn.Spawned && pawn.Map == map)
                pawn.ExitMap(false, CellRect.WholeMap(map).GetClosestEdge(pawn.Position));

            SimDebugLogger.Journey("RSMF.CustomerExit", reason ?? "顾客强制离店", pawn, null, -1);
        }

        //准备离店，职责是幂等释放票据、顾客索引、当前 Job 和原访问 Lord 关系。
        private static bool TryPrepareExit(Pawn pawn, out Map map)
        {
            map = pawn?.Map;
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || map == null)
                return false;

            CustomerArrivalManager manager = map.GetComponent<CustomerArrivalManager>();
            manager?.NotifyForcedExit();
            manager?.ReleaseCheckoutTicket(pawn);
            manager?.UnregisterCustomer(pawn);
            if (pawn.jobs?.curJob != null)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false, true);

            Lord oldLord = map.lordManager?.LordOf(pawn);
            oldLord?.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            return pawn.Spawned && pawn.Map == map;
        }
    }
}
