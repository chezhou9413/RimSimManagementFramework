using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 绘制顾客评价提示词预览窗口，负责展示当前设置会发送给模型的系统提示词和分层用户消息。
    /// </summary>
    public class Dialog_CustomerReviewPromptPreview : Window
    {
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(1080f, 760f);

        /// <summary>
        /// 初始化提示词预览窗口的拖动、缩放和关闭行为。
        /// </summary>
        public Dialog_CustomerReviewPromptPreview()
        {
            doCloseX = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            draggable = true;
            resizeable = true;
        }

        /// <summary>
        /// 绘制提示词预览窗口主体，负责预览、复制和刷新当前设置。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                SimManagementLibSettings settings = SimManagementLibMod.Settings;
                string text = BuildPreviewText(settings);

                float titleH = Mathf.Max(34f, Text.LineHeightOf(GameFont.Medium) + 8f);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 170f, titleH), SimTranslation.T("RSMF.PromptPreview.Title"));
                ResetText();

                Rect copyRect = new Rect(inRect.xMax - 148f, inRect.y + 2f, 138f, 32f);
                if (SimUiStyle.DrawPrimaryButton(copyRect, SimTranslation.T("RSMF.Common.CopyAll"), true, GameFont.Small))
                    GUIUtility.systemCopyBuffer = text;

                Rect noteRect = new Rect(inRect.x, inRect.y + titleH + 4f, inRect.width, 42f);
                Widgets.DrawBoxSolid(noteRect, new Color(0f, 0f, 0f, 0.18f));
                SimUiStyle.DrawBorder(noteRect, new Color(1f, 1f, 1f, 0.10f));
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
                Widgets.Label(noteRect.ContractedBy(8f), SimTranslation.T("RSMF.PromptPreview.Note"));
                ResetText();

                Rect bodyRect = new Rect(inRect.x, noteRect.yMax + 8f, inRect.width, Mathf.Max(120f, inRect.height - noteRect.yMax - 8f));
                Widgets.DrawBoxSolid(bodyRect, new Color(0f, 0f, 0f, 0.22f));
                SimUiStyle.DrawBorder(bodyRect, new Color(1f, 1f, 1f, 0.10f));
                DrawPreviewText(bodyRect.ContractedBy(8f), text);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        /// <summary>
        /// 绘制可滚动预览文本，负责保留长提示词的复制和阅读空间。
        /// </summary>
        private void DrawPreviewText(Rect rect, string text)
        {
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            GUI.color = Color.white;
            float viewW = Mathf.Max(120f, rect.width - 18f);
            float textH = Mathf.Max(rect.height + 1f, Text.CalcHeight(text ?? "", viewW) + 20f);
            Rect viewRect = new Rect(0f, 0f, viewW, textH);
            Widgets.BeginScrollView(rect, ref scroll, viewRect);
            Widgets.TextArea(new Rect(0f, 0f, viewW, textH), text ?? "", true);
            Widgets.EndScrollView();
            ResetText();
        }

        /// <summary>
        /// 构造完整预览文本，负责同时显示系统提示词、稳定缓存前缀和动态顾客资料。
        /// </summary>
        private static string BuildPreviewText(SimManagementLibSettings settings)
        {
            if (settings == null)
                return SimTranslation.T("RSMF.PromptPreview.NoSettings");

            CustomerReviewSnapshot snapshot = BuildPreviewSnapshot();
            string stablePromptPrefix = CustomerReviewPromptInjector.BuildStablePromptPrefix(settings);
            string dynamicPrompt = CustomerReviewPromptInjector.BuildDynamicPrompt(snapshot, settings, "");
            return SimTranslation.T("RSMF.PromptPreview.Section.SystemPrompt") + "\n"
                + (settings.reviewSystemPrompt ?? "")
                + "\n\n" + SimTranslation.T("RSMF.PromptPreview.Section.StablePrefix") + "\n"
                + stablePromptPrefix
                + "\n\n" + SimTranslation.T("RSMF.PromptPreview.Section.DynamicInput") + "\n"
                + dynamicPrompt;
        }

        /// <summary>
        /// 构造示例顾客快照，负责让预览窗口可以展示所有节点的最终结构。
        /// </summary>
        private static CustomerReviewSnapshot BuildPreviewSnapshot()
        {
            return new CustomerReviewSnapshot
            {
                reviewId = "preview_0001",
                tickAbs = Find.TickManager?.TicksAbs ?? 0,
                gameDay = GenDate.DaysPassed,
                zoneId = 1,
                zoneLabel = SimTranslation.T("RSMF.PromptPreview.Sample.Shop"),
                customerDisplayName = SimTranslation.T("RSMF.PromptPreview.Sample.Customer"),
                spentSilver = 96f,
                kindId = "preview_customer",
                kindLabel = SimTranslation.T("RSMF.PromptPreview.Sample.Kind"),
                kindDescription = SimTranslation.T("RSMF.PromptPreview.Sample.KindDescription"),
                raceLabel = SimTranslation.T("RSMF.PromptPreview.Sample.Race"),
                raceDescription = SimTranslation.T("RSMF.PromptPreview.Sample.RaceDescription"),
                ageSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Age"),
                backstorySummary = SimTranslation.T("RSMF.PromptPreview.Sample.Backstory"),
                backstoryDetailSummary = SimTranslation.T("RSMF.PromptPreview.Sample.BackstoryDetails"),
                traitSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Traits"),
                xenotypeSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Xenotype"),
                geneSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Genes"),
                personalityBiasSummary = SimTranslation.T("RSMF.PromptPreview.Sample.PersonalityBias"),
                moodSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Mood"),
                healthSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Health"),
                budgetSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Budget"),
                purchasedSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Purchased"),
                serviceSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Service"),
                shopEnvironmentSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Environment"),
                cashierSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Cashier"),
                checkoutJobSummary = SimTranslation.T("RSMF.PromptPreview.Sample.CheckoutJob"),
                postPurchaseSummary = SimTranslation.T("RSMF.PromptPreview.Sample.PostPurchase"),
                roomSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Room"),
                relationSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Relations"),
                weatherSummary = SimTranslation.T("RSMF.PromptPreview.Sample.Weather"),
                gameConditionSummary = SimTranslation.T("RSMF.PromptPreview.Sample.GameConditions"),
                colonyWealthSummary = SimTranslation.T("RSMF.PromptPreview.Sample.ColonyWealth"),
                colonyShopSummary = SimTranslation.T("RSMF.PromptPreview.Sample.ColonyShops"),
                colonyLeaderSummary = SimTranslation.T("RSMF.PromptPreview.Sample.ColonyLeader"),
                colonyCultureSummary = SimTranslation.T("RSMF.PromptPreview.Sample.ColonyCulture"),
                recentReviewContextSummary = SimTranslation.T("RSMF.PromptPreview.Sample.RecentReviews"),
                featuredItems = new List<ReviewFeaturedItem>(),
                avatarImageId = ""
            };
        }

        /// <summary>
        /// 恢复 RimWorld 全局文本状态，负责避免影响其他窗口绘制。
        /// </summary>
        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
