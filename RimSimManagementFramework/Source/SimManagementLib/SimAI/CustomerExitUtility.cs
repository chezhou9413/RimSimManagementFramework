using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimMapComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //结束顾客经营访问，职责是释放业务状态并让原版离图职责保留地图上的真实顾客。
    internal static class CustomerExitUtility
    {
        //开始紧急冲刺离图，职责是先释放经营状态，再交给原版离图 Lord 寻路到地图边缘。
        public static void BeginEmergencyExit(Pawn pawn, string reason)
        {
            if (pawn?.GetLord()?.LordJob is LordJob_ExitMapBest) return;
            if (!TryPrepareExit(pawn, reason, out Map map))
                return;

            LordJob_ExitMapBest exitJob = new LordJob_ExitMapBest(LocomotionUrgency.Sprint, false, false);
            LordMaker.MakeNewLord(pawn.Faction, exitJob, map, new List<Pawn> { pawn });
            SimDebugLogger.Journey("RSMF.CustomerExit", reason ?? "顾客紧急离店", pawn, null, -1);
        }

        //强制结束经营访问，职责是将异常顾客交给独立离图职责，等待走到边缘后再离图。
        public static void ForceExit(Pawn pawn, string reason)
        {
            //倒地、精神状态或道路阻塞只结束购物，不删除顾客；恢复行动后由原版继续寻路。
            BeginEmergencyExit(pawn, reason);
        }

        //准备离店，职责是释放动作预约、票据、顾客索引、当前工作和原访问职责。
        private static bool TryPrepareExit(Pawn pawn, string reason, out Map map)
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

            //先让当前工作交回携带实物，再通过扩展取消回调释放餐位、托盘和厨房预留。
            foreach (var order in SimShopCustomerApi.QueryActionOrders(new CustomerActionOrderQuery
                { customerThingId = pawn.thingIDNumber, includeTerminalOrders = false }))
                if (order.IsActiveState) SimShopCustomerApi.CancelActionOrder(order, reason ?? "顾客结束访问", true);

            Lord oldLord = map.lordManager?.LordOf(pawn);
            oldLord?.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            return pawn.Spawned && pawn.Map == map;
        }
    }
}
