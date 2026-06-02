using LudeonTK;
using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.Debug
{
    /// <summary>
    /// 提供顾客 Session 调试动作，负责在开发模式下定位顾客当前阶段和卡住原因。
    /// </summary>
    public static partial class DebugActions_SimShop
    {
        /// <summary>
        /// 查看当前选中顾客的 Session 诊断。
        /// </summary>
        [DebugAction("SimShop", "查看选中顾客 Session", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ShowSelectedCustomerSession()
        {
            Pawn pawn = Find.Selector?.SelectedObjects?.OfType<Pawn>().FirstOrDefault();
            if (pawn == null)
            {
                Log.Message("[RSMF 顾客诊断] 没有选中顾客。");
                return;
            }

            LordJob_CustomerVisit visit = pawn.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            CustomerVisitSession session = visit?.GetOrCreateSession(pawn);
            Log.Message(session != null ? session.BuildDebugReport(visit, pawn) : "[RSMF 顾客诊断] 选中 Pawn 不是顾客。");
        }

        /// <summary>
        /// 列出当前地图所有顾客 Session。
        /// </summary>
        [DebugAction("SimShop", "列出地图所有顾客 Session", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ListAllCustomerSessions()
        {
            Map map = Find.CurrentMap;
            if (map?.lordManager?.lords == null)
            {
                Log.Message("[RSMF 顾客诊断] 当前地图无 Lord。");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[RSMF 顾客诊断] 当前地图顾客 Session");
            for (int i = 0; i < map.lordManager.lords.Count; i++)
            {
                Lord lord = map.lordManager.lords[i];
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                if (visit == null || lord.ownedPawns == null) continue;
                for (int j = 0; j < lord.ownedPawns.Count; j++)
                {
                    Pawn pawn = lord.ownedPawns[j];
                    if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                    CustomerVisitSession session = visit.GetOrCreateSession(pawn);
                    sb.AppendLine((session != null ? session.BuildShortStatus(visit, pawn) : "无 Session") + " | " + pawn.LabelShortCap);
                }
            }
            Log.Message(sb.ToString().TrimEnd());
        }

        /// <summary>
        /// 强制推进选中顾客 Session 下一步。
        /// </summary>
        [DebugAction("SimShop", "强制推进选中顾客 Session", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceAdvanceSelectedCustomerSession()
        {
            Pawn pawn = Find.Selector?.SelectedObjects?.OfType<Pawn>().FirstOrDefault();
            LordJob_CustomerVisit visit = pawn?.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            CustomerVisitSession session = visit?.GetOrCreateSession(pawn);
            if (pawn == null || visit == null || session == null)
            {
                Log.Message("[RSMF 顾客诊断] 没有选中有效顾客。");
                return;
            }

            CustomerVisitTickResult result = session.ForceAdvance(visit, pawn);
            if (result.requestCheckoutMemo)
                visit.lord?.ReceiveMemo("Customer_ReadyToCheckout");
            if (result.requestCheckoutCompletedMemo)
                visit.CheckAllCheckoutsDone();
            Log.Message(session.BuildDebugReport(visit, pawn));
        }

        /// <summary>
        /// 扫描当前地图顾客流程异常，负责快速定位卡在无 Job、无账单或售后阶段的顾客。
        /// </summary>
        [DebugAction("SimShop", "顾客流程自检", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckCustomerVisitHealth()
        {
            Map map = Find.CurrentMap;
            if (map?.lordManager?.lords == null)
            {
                Log.Message("[RSMF 顾客自检] 当前地图无 Lord。");
                return;
            }

            StringBuilder sb = new StringBuilder();
            int customerCount = 0;
            int warningCount = 0;
            sb.AppendLine("[RSMF 顾客自检] 当前地图顾客流程");

            ForEachCustomer(map, (visit, pawn, session) =>
            {
                customerCount++;
                string warning = BuildCustomerWarning(visit, pawn, session);
                if (!string.IsNullOrEmpty(warning))
                {
                    warningCount++;
                    sb.AppendLine("警告: " + pawn.LabelShortCap + "/" + pawn.thingIDNumber + " " + warning);
                }
                else
                {
                    sb.AppendLine("正常: " + session.BuildShortStatus(visit, pawn) + " | " + pawn.LabelShortCap);
                }
            });

            sb.AppendLine("顾客数: " + customerCount + " 警告数: " + warningCount);
            Log.Message(sb.ToString().TrimEnd());
        }

        /// <summary>
        /// 统计当前地图顾客压力数据，负责观察多顾客并发下的阶段分布和长期停留情况。
        /// </summary>
        [DebugAction("SimShop", "顾客压力诊断", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportCustomerVisitPressure()
        {
            Map map = Find.CurrentMap;
            if (map?.lordManager?.lords == null)
            {
                Log.Message("[RSMF 顾客压力] 当前地图无 Lord。");
                return;
            }

            Dictionary<CustomerVisitStage, int> stageCounts = new Dictionary<CustomerVisitStage, int>();
            int customerCount = 0;
            int sessionCount = 0;
            int warningCount = 0;
            int maxStayTicks = 0;
            Pawn maxStayPawn = null;
            int nowTick = Find.TickManager?.TicksGame ?? 0;

            ForEachCustomer(map, (visit, pawn, session) =>
            {
                customerCount++;
                if (session != null)
                {
                    sessionCount++;
                    CustomerVisitStage stage = session.Stage;
                    stageCounts.TryGetValue(stage, out int count);
                    stageCounts[stage] = count + 1;

                    int startTick = session.TotalVisitStartTick >= 0 ? session.TotalVisitStartTick : nowTick;
                    int stayTicks = nowTick - startTick;
                    if (stayTicks > maxStayTicks)
                    {
                        maxStayTicks = stayTicks;
                        maxStayPawn = pawn;
                    }
                }

                if (!string.IsNullOrEmpty(BuildCustomerWarning(visit, pawn, session)))
                    warningCount++;
            });

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[RSMF 顾客压力] 当前地图顾客压力诊断");
            sb.AppendLine("顾客数: " + customerCount);
            sb.AppendLine("Session数: " + sessionCount);
            sb.AppendLine("异常数: " + warningCount);
            sb.AppendLine("最大停留Tick: " + maxStayTicks + " 顾客: " + (maxStayPawn?.LabelShortCap ?? "无"));
            foreach (CustomerVisitStage stage in System.Enum.GetValues(typeof(CustomerVisitStage)))
            {
                stageCounts.TryGetValue(stage, out int count);
                sb.AppendLine(stage + ": " + count);
            }
            Log.Message(sb.ToString().TrimEnd());
        }

        /// <summary>
        /// 遍历当前地图所有顾客 Session，负责让多个调试动作复用一致的过滤规则。
        /// </summary>
        private static void ForEachCustomer(Map map, System.Action<LordJob_CustomerVisit, Pawn, CustomerVisitSession> action)
        {
            if (map?.lordManager?.lords == null || action == null) return;
            for (int i = 0; i < map.lordManager.lords.Count; i++)
            {
                Lord lord = map.lordManager.lords[i];
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                if (visit == null || lord.ownedPawns == null) continue;
                for (int j = 0; j < lord.ownedPawns.Count; j++)
                {
                    Pawn pawn = lord.ownedPawns[j];
                    if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                    action(visit, pawn, visit.GetOrCreateSession(pawn));
                }
            }
        }

        /// <summary>
        /// 构建单个顾客的异常摘要，负责让自检输出直接回答为什么可能卡住。
        /// </summary>
        private static string BuildCustomerWarning(LordJob_CustomerVisit visit, Pawn pawn, CustomerVisitSession session)
        {
            if (visit == null || pawn == null) return "顾客访问不存在";
            if (session == null) return "Session 不存在";
            int pawnId = pawn.thingIDNumber;
            float owed = visit.GetAmountOwedForCheckout(pawnId);
            bool needsPostCheckout = visit.NeedsPostCheckoutCompletion(pawnId);
            if (pawn.CurJob == null && session.Stage != CustomerVisitStage.Leaving && session.Stage != CustomerVisitStage.Ended)
                return "当前无 Job 阶段=" + session.Stage + " 原因=" + session.LastReason + " 失败=" + session.LastFailureReason;
            if (session.Stage == CustomerVisitStage.Browsing && session.CurrentShopMinimumBrowseDone && session.CurrentShopNoProgressBrowseAttempts >= visit.GetCurrentShopNoProgressBrowseLimit())
                return "浏览无进展已达阈值 owed=" + owed.ToString("F2") + " 原因=" + session.LastReason;
            if (session.Stage == CustomerVisitStage.Checkout && owed <= 0f && !needsPostCheckout)
                return "结账阶段零账单且无售后";
            if (session.Stage == CustomerVisitStage.PostCheckout && !needsPostCheckout)
                return "售后阶段没有待执行购后 Job";
            return "";
        }
    }
}
