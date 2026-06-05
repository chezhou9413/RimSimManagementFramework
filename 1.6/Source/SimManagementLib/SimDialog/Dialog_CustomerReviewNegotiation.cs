using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 绘制顾客评价申诉窗口，负责让玩家在稳定弹窗中输入申诉内容并提交给评价管理器。
    /// </summary>
    public class Dialog_CustomerReviewNegotiation : Window
    {
        private readonly string reviewId;
        private readonly string customerName;
        private readonly string reviewText;
        private string draft = "";

        public override Vector2 InitialSize => new Vector2(720f, 430f);

        /// <summary>
        /// 初始化评价申诉窗口，负责保存目标评价的显示摘要。
        /// </summary>
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

        /// <summary>
        /// 绘制申诉输入界面，负责固定标题、原评价摘要、输入框和底部提交按钮的位置。
        /// </summary>
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
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleH), SimTranslation.TOrFallback("RSMF.Business.Reviews.Negotiation.DialogTitle", "向顾客申诉评价"));

                float y = inRect.y + titleH + 8f;
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
                string summary = SimTranslation.TOrFallback("RSMF.Business.Reviews.Negotiation.DialogSummary", "顾客 {name} 的评价：{text}")
                    .Replace("{name}", string.IsNullOrWhiteSpace(customerName) ? SimTranslation.TOrFallback("RSMF.Business.Reviews.AnonymousUser", "匿名用户") : customerName)
                    .Replace("{text}", reviewText);
                float summaryH = Mathf.Min(92f, Mathf.Max(Text.LineHeight, Text.CalcHeight(summary, inRect.width)));
                Widgets.Label(new Rect(inRect.x, y, inRect.width, summaryH), summary);
                y += summaryH + 10f;

                string hint = SimTranslation.TOrFallback("RSMF.Business.Reviews.Negotiation.InputHint", "向顾客解释或反驳这条评价，最多 300 字。说好话可能提星，骂人可能降星。顾客可能坚持、修改或撤回评价。");
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
                if (Widgets.ButtonText(cancelRect, SimTranslation.TOrFallback("RSMF.Common.Cancel", "取消")))
                    Close();

                if (Widgets.ButtonText(submitRect, SimTranslation.TOrFallback("RSMF.Business.Reviews.Negotiation.Submit", "提交申诉")))
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

        /// <summary>
        /// 提交申诉内容，负责调用评价管理器并显示成功或拒绝提示。
        /// </summary>
        private void Submit()
        {
            GameComponent_CustomerReviewManager manager = Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
            if (manager == null)
            {
                Messages.Message(SimTranslation.TOrFallback("RSMF.Business.Reviews.ComponentMissing", "顾客评价组件未初始化。"), MessageTypeDefOf.RejectInput, false);
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
