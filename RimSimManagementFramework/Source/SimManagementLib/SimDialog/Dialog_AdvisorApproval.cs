using SimManagementLib.GameComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //展示确定的目标、前后值和施工需求，批准绑定到本存档的具体方案。
    internal sealed class Dialog_AdvisorApproval : Window
    {
        private readonly GameComponent_BusinessAdvisor advisor;
        private readonly int revision;
        private readonly string preview;
        private Vector2 scroll;

        //冻结审批内容，关闭窗口不代表批准。
        internal Dialog_AdvisorApproval(GameComponent_BusinessAdvisor advisor)
        {
            this.advisor = advisor; revision = advisor.PlanRevision; preview = advisor.PlanPreview;
            forcePause = true; absorbInputAroundWindow = true; doCloseX = true; closeOnAccept = false;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(920f, UI.screenWidth - 40f), Mathf.Min(740f, UI.screenHeight - 40f));

        //明确批准或拒绝，不允许来自另一个存档的旧窗口执行操作。
        public override void DoWindowContents(Rect rect)
        {
            GameFont font = Text.Font; bool wrap = Text.WordWrap; TextAnchor anchor = Text.Anchor; Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.WordWrap = true; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                string hint = SimTranslation.T("RSMF.Business.Advisor.ApprovalHint");
                float heading = Text.CalcHeight(hint, rect.width - 30f) + 12f;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width - 30f, heading), hint);
                float buttonH = Mathf.Max(36f, Text.LineHeight + 12f);
                Widgets.LabelScrollable(new Rect(rect.x, rect.y + heading, rect.width, rect.height - heading - buttonH - 12f), preview, ref scroll);
                bool current = Current.Game?.GetComponent<GameComponent_BusinessAdvisor>() == advisor && advisor.HasPendingPlan && advisor.PlanRevision == revision;
                float width = (rect.width - 12f) / 2f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x, rect.yMax - buttonH, width, buttonH), SimTranslation.T("RSMF.Business.Advisor.Reject"), current))
                { advisor.RejectPlan(revision); Close(); }
                if (SimUiStyle.DrawPrimaryButton(new Rect(rect.x + width + 12f, rect.yMax - buttonH, width, buttonH), SimTranslation.T("RSMF.Business.Advisor.Approve"), current))
                { advisor.ApprovePlan(revision); Close(); }
            }
            finally { Text.Font = font; Text.WordWrap = wrap; Text.Anchor = anchor; GUI.color = color; }
        }
    }
}
