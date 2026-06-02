using RimWorld;
using SimManagementLib.Debug;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 提供顾客开发调试 Gizmo，负责在上帝模式下复制单个顾客的行为诊断。
    /// </summary>
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 返回顾客 Pawn 的额外 Gizmo，负责提供行为日志复制入口。
        /// </summary>
        public override IEnumerable<Gizmo> GetPawnGizmos(Pawn p)
        {
            foreach (Gizmo gizmo in base.GetPawnGizmos(p))
                yield return gizmo;

            if (!DebugSettings.ShowDevGizmos || p == null)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "复制顾客诊断",
                defaultDesc = "复制该顾客的 Session、Lord、Job、商品匹配和最近行为日志到剪切板。",
                icon = TexCommand.DesirePower,
                action = () =>
                {
                    string report = CustomerVisitDebugReportBuilder.Build(p);
                    GUIUtility.systemCopyBuffer = report;
                    Messages.Message("已复制顾客诊断到剪切板。", MessageTypeDefOf.TaskCompletion, false);
                    SimDebugLogger.Journey("RSMF.CustomerDebug", "复制顾客诊断到剪切板", p, GetCurrentShop(p), -1);
                }
            };
        }
    }
}
