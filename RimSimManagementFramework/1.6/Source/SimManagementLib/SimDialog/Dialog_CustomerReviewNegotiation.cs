using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //绘制顾客评价申诉窗口，负责让玩家在稳定弹窗中输入申诉内容并提交给评价管理器。
    public class Dialog_CustomerReviewNegotiation : Window
    {
        private readonly string reviewId;
        private readonly string customerName;
        private readonly string reviewText;
        private string draft = "";

        public override Vector2 InitialSize => new Vector2(720f, 430f);
        //初始化评价申诉窗口，负责保存目标评价的显示摘要。
        public Dialog_CustomerReviewNegotiation(string reviewId, string customerName, string reviewText)
        {
            this.reviewId = reviewId ?? "";
            this.customerName = customerName ?? "";
            this.reviewText = reviewText ?? "";
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            draggable = true;
        }
        //绘制申诉输入界面，负责固定标题、原评价摘要、输入框和底部提交按钮的位置。
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                float titleH = Mathf.Max(30f, Text.LineHeightOf(GameFont.Medium) + 8f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleH), SimTranslation.T("RSMF.Business.Reviews.Negotiation.DialogTitle"));

                float y = inRect.y + titleH + 8f;
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
                string summary = SimTranslation.T("RSMF.Business.Reviews.Negotiation.DialogSummary")
                    .Replace("{name}", string.IsNullOrWhiteSpace(customerName) ? SimTranslation.T("RSMF.Business.Reviews.AnonymousUser") : customerName)
                    .Replace("{text}", reviewText);
                float summaryH = Mathf.Min(92f, Mathf.Max(Text.LineHeight, Text.CalcHeight(summary, inRect.width)));
                Widgets.Label(new Rect(inRect.x, y, inRect.width, summaryH), summary);
                y += summaryH + 10f;

                string hint = SimTranslation.T("RSMF.Business.Reviews.Negotiation.InputHint");
                float hintH = Mathf.Max(Text.LineHeight, Text.CalcHeight(hint, inRect.width));
                Widgets.Label(new Rect(inRect.x, y, inRect.width, hintH), hint);
                y += hintH + 6f;

                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                GUI.color = Color.white;
                float footerH = 38f;
                Rect inputRect = new Rect(inRect.x, y, inRect.width, Mathf.Max(120f, inRect.yMax - y - footerH - 12f));
                draft = Widgets.TextArea(inputRect, draft ?? "");
                draft = CustomerReviewNegotiationUtility.SanitizePlayerReply(draft);

                Rect submitRect = new Rect(inRect.xMax - 116f, inRect.yMax - 34f, 116f, 32f);
                Rect cancelRect = new Rect(submitRect.x - 104f, submitRect.y, 96f, submitRect.height);
                if (Widgets.ButtonText(cancelRect, SimTranslation.T("RSMF.Common.Cancel")))
                    Close();

                if (Widgets.ButtonText(submitRect, SimTranslation.T("RSMF.Business.Reviews.Negotiation.Submit")))
                    Submit();
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }
        //提交申诉内容，负责调用评价管理器并显示成功或拒绝提示。
        private void Submit()
        {
            GameComponent_CustomerReviewManager manager = Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
            if (manager == null)
            {
                Messages.Message(SimTranslation.T("RSMF.Business.Reviews.ComponentMissing"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (manager.TrySubmitPlayerReply(reviewId, draft, out string message))
            {
                Messages.Message(message, MessageTypeDefOf.PositiveEvent, false);
                Close();
            }
            else
            {
                Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            }
        }
    }
}
