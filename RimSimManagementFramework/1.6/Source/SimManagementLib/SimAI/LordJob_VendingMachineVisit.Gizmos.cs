using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimAI
{
    //类职责：提供自动售货机顾客的玩家手动驱离 Gizmo。
    public partial class LordJob_VendingMachineVisit
    {
        //返回自动售货机顾客的额外 Gizmo，职责是提供安全驱离入口。
        public override IEnumerable<Gizmo> GetPawnGizmos(Pawn pawn)
        {
            foreach (Gizmo gizmo in base.GetPawnGizmos(pawn))
                yield return gizmo;

            if (pawn != null)
                yield return CustomerDismissalUtility.CreateDismissCommand(pawn);
        }
    }
}
