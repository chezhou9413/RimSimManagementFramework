using RimWorld;
using SimManagementLib.Debug;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimAI
{
    //类职责：提供顾客手动驱离与开发诊断 Gizmo。
    public partial class LordJob_CustomerVisit
    {
        //返回顾客 Pawn 的额外 Gizmo，职责是提供手动驱离和开发诊断入口。
        public override IEnumerable<Gizmo> GetPawnGizmos(Pawn p)
        {
            foreach (Gizmo gizmo in base.GetPawnGizmos(p))
                yield return gizmo;

            if (p == null)
                yield break;

            yield return CustomerDismissalUtility.CreateDismissCommand(p);

            if (!DebugSettings.ShowDevGizmos)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.CustomerDiagnostics.Copy"),
                defaultDesc = SimTranslation.T("RSMF.CustomerDiagnostics.CopyDescription"),
                icon = TexCommand.DesirePower,
                action = () =>
                {
                    string report = CustomerVisitDebugReportBuilder.Build(p);
                    GUIUtility.systemCopyBuffer = report;
                    Messages.Message(SimTranslation.T("RSMF.CustomerDiagnostics.Copied"), MessageTypeDefOf.TaskCompletion, false);
                    SimDebugLogger.Journey("RSMF.CustomerDebug", "复制顾客诊断到剪切板", p, GetCurrentShop(p), -1);
                }
            };
        }
    }
}
