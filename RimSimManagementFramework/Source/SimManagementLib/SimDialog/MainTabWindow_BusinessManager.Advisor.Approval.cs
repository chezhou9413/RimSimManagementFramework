using SimManagementLib.GameComp;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //在对话页直接审批当前方案，完整详情仍可按需弹出。
    public partial class MainTabWindow_BusinessManager
    {
        private GameComponent_BusinessAdvisor advisorApprovalOwner;
        private int advisorApprovalRevision;
        private string advisorApprovalPreview = "";
        private string advisorApprovalTitle = "";
        private Vector2 advisorApprovalScroll;
        private float advisorApprovalWidth, advisorApprovalHeight, advisorApprovalLine;
        private bool advisorApprovalShown;

        //冻结显示内容及修订号，只有已经显示过的同一方案才能被批准。
        private void DrawAdvisorApproval(Rect rect, GameComponent_BusinessAdvisor advisor, float control)
        {
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
            if (advisorApprovalOwner != advisor || advisorApprovalRevision != advisor.PlanRevision)
            {
                advisorApprovalOwner = advisor;
                advisorApprovalRevision = advisor.PlanRevision;
                advisorApprovalPreview = advisor.PlanPreview;
                advisorApprovalTitle = AdvisorText("PendingApproval").Replace("{count}", advisor.PendingOperationCount.ToString());
                advisorApprovalScroll = Vector2.zero;
                advisorApprovalWidth = 0f;
                advisorApprovalShown = false;
            }
            Widgets.DrawBoxSolid(rect, CPanel);
            SimUiStyle.DrawBorder(rect, new Color(CWarn.r, CWarn.g, CWarn.b, 0.6f));
            Rect inner = rect.ContractedBy(10f);
            float heading = Text.LineHeight + 4f;
            GUI.color = CWarn;
            AdvisorLabel(new Rect(inner.x, inner.y, inner.width, heading), advisorApprovalTitle, AdvisorText("ApprovalHint"));
            GUI.color = Color.white;

            float buttonY = inner.yMax - control;
            Rect body = new Rect(inner.x, inner.y + heading + 6f, inner.width, buttonY - inner.y - heading - 14f);
            DrawAdvisorApprovalPreview(body);

            if (Event.current.type == EventType.Repaint) advisorApprovalShown = true;
            bool current = advisorApprovalShown && Current.Game?.GetComponent<GameComponent_BusinessAdvisor>() == advisor
                && advisor.HasPendingPlan && advisor.PlanRevision == advisorApprovalRevision;
            float buttonWidth = Mathf.Min(170f, (inner.width - 16f) / 3f);
            if (SimUiStyle.DrawSecondaryButton(new Rect(inner.x, buttonY, buttonWidth, control), AdvisorText("PlanDetails"), current))
                Find.WindowStack.Add(new Dialog_AdvisorApproval(advisor));
            if (SimUiStyle.DrawSecondaryButton(new Rect(inner.xMax - buttonWidth * 2f - 8f, buttonY, buttonWidth, control), AdvisorText("Reject"), current))
            {
                advisor.RejectPlan(advisorApprovalRevision);
                advisorFocusInput = true;
            }
            if (SimUiStyle.DrawPrimaryButton(new Rect(inner.xMax - buttonWidth, buttonY, buttonWidth, control), AdvisorText("ApproveInline"), current))
            {
                advisor.ApprovePlan(advisorApprovalRevision);
                advisorFocusInput = true;
            }
        }

        //批量操作可在卡片内滚动，方案及宽度未变时复用已测量的文本高度。
        private void DrawAdvisorApprovalPreview(Rect rect)
        {
            if (rect.height < Text.LineHeight) return;
            float width = rect.width - 18f;
            if (advisorApprovalWidth != width || advisorApprovalLine != Text.LineHeight)
            {
                advisorApprovalWidth = width;
                advisorApprovalLine = Text.LineHeight;
                advisorApprovalHeight = Text.CalcHeight(advisorApprovalPreview, width) + 5f;
            }
            Rect view = new Rect(0f, 0f, width, Mathf.Max(rect.height, advisorApprovalHeight));
            Widgets.BeginScrollView(rect, ref advisorApprovalScroll, view);
            Widgets.Label(view, advisorApprovalPreview);
            Widgets.EndScrollView();
        }
    }
}
