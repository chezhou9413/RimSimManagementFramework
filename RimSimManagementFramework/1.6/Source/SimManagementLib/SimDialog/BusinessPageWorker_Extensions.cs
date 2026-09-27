using SimManagementLib.Api;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //绘制扩展推荐页，职责是按官方和社区分类展示本地推荐及 Steam 入口。
    public partial class BusinessPageWorker_Extensions : BusinessManagerPageWorker
    {
        private const float PreviewSize = 96f;
        private static float RowHeight => Mathf.Max(132f, ShopUiVisualUtility.LineHeight(GameFont.Small) * 3f + 38f);

        //判断页面可见性，职责是遵循玩家的扩展推荐页显示设置。
        public override bool CanShow(ShopUiContext context)
        {
            return SimManagementLibMod.Settings?.showExtensionRecommendationPage != false;
        }

        //绘制推荐子页，职责是隔离窗口状态并为导航、摘要和列表分配独立空间。
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            Color color = GUI.color;
            try
            {
                ResetTextState();
                PollRemotePreviewTasks();
                var state = context.GetOrCreatePageState("RSMF.Extensions", () => new ExtensionRecommendationPageState());
                float control = ShopUiVisualUtility.ControlHeight();
                DrawTabs(new Rect(rect.x, rect.y, rect.width, control), state);
                List<IBusinessExtensionRecommendation> rows = BusinessExtensionRecommendationSource.GetRows(state.official);
                float top = rect.y + control + 8f;
                float heading = ShopUiVisualUtility.DrawPageHeading(new Rect(rect.x, top, rect.width, rect.yMax - top),
                    SimTranslation.T(state.official ? "RSMF.Business.Extensions.Official" : "RSMF.Business.Extensions.Community"),
                    SimTranslation.T("RSMF.Business.Extensions.PageSummary",
                        rows.Count(row => BusinessExtensionRecommendationUtility.GetStatus(row).IsChecked).Named("checked"),
                        rows.Count.Named("total")));
                DrawList(new Rect(rect.x, top + heading, rect.width, Mathf.Max(0f, rect.yMax - top - heading)), rows, state);
            }
            finally
            {
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wrap;
                GUI.color = color;
            }
        }

        //绘制官方和社区子页入口，职责是在切换分类后重置列表滚动位置。
        private static void DrawTabs(Rect rect, ExtensionRecommendationPageState state)
        {
            float width = Mathf.Min(200f, (rect.width - 8f) / 2f);
            bool official = ShopUiVisualUtility.DrawTabButton(new Rect(rect.x, rect.y, width, rect.height),
                SimTranslation.T("RSMF.Business.Extensions.Official"), state.official, ShopUiVisualUtility.MutedText);
            bool community = ShopUiVisualUtility.DrawTabButton(new Rect(rect.x + width + 8f, rect.y, width, rect.height),
                SimTranslation.T("RSMF.Business.Extensions.Community"), !state.official, ShopUiVisualUtility.MutedText);
            if (!official && !community) return;
            state.official = official;
            state.scroll = Vector2.zero;
        }

        //绘制当前分类列表，职责是仅绘制可见推荐并保证滚动裁剪正确结束。
        private static void DrawList(Rect rect, List<IBusinessExtensionRecommendation> rows, ExtensionRecommendationPageState state)
        {
            if (rows.Count == 0)
            {
                ShopUiLayoutUtility.DrawEmptyState(rect, SimTranslation.T("RSMF.Business.Extensions.Empty"));
                return;
            }
            float rowHeight = RowHeight;
            Rect view = new Rect(0f, 0f, Mathf.Max(1f, rect.width - 18f), Mathf.Max(rect.height, rows.Count * rowHeight));
            Widgets.BeginScrollView(rect, ref state.scroll, view);
            try
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(state.scroll.y / rowHeight));
                int last = Mathf.Min(rows.Count, Mathf.CeilToInt((state.scroll.y + rect.height) / rowHeight));
                for (int i = first; i < last; i++)
                    DrawRecommendationRow(new Rect(0f, i * rowHeight, view.width, rowHeight - 6f), rows[i], i);
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制推荐条目，职责是按安全中文行高展示封面、简介、状态与 Steam 链接。
        private static void DrawRecommendationRow(Rect row, IBusinessExtensionRecommendation recommendation, int index)
        {
            ShopUiVisualUtility.DrawTableRowBackground(row, index, false);
            Rect preview = new Rect(row.x + 10f, row.y + 14f, PreviewSize, PreviewSize);
            DrawPreview(preview, recommendation);
            float actionWidth = 134f;
            float x = preview.xMax + 12f;
            float width = Mathf.Max(1f, row.xMax - actionWidth - 30f - x);
            float line = ShopUiVisualUtility.LineHeight(GameFont.Small);
            ShopUiVisualUtility.DrawCellLabel(new Rect(x, row.y + 12f, width, line), recommendation.DisplayLabel);
            ShopUiVisualUtility.DrawCellLabel(new Rect(x, row.y + line + 18f, width, line),
                recommendation.DisplayDescription, ShopUiVisualUtility.MutedText);
            DrawStatusBadges(new Rect(x, row.y + line * 2f + 24f, width, line),
                BusinessExtensionRecommendationUtility.GetStatus(recommendation));
            Rect button = new Rect(row.xMax - actionWidth - 10f, row.y + 24f, actionWidth, ShopUiVisualUtility.ControlHeight());
            if (ShopUiVisualUtility.DrawPrimaryButton(button, SimTranslation.T("RSMF.Business.Extensions.OpenSteam"),
                !string.IsNullOrWhiteSpace(recommendation.WorkshopUrl)))
                BusinessExtensionRecommendationUtility.OpenWorkshopUrl(recommendation.WorkshopUrl);
        }

        //绘制状态摘要，职责是区分已安装或订阅、实际启用和仍待下载的状态。
        private static void DrawStatusBadges(Rect rect, BusinessExtensionRecommendationStatus status)
        {
            string label = SimTranslation.T(status.IsChecked ? "RSMF.Business.Extensions.Checked" : "RSMF.Business.Extensions.NotInstalled");
            if (status.IsActive) label += " · " + SimTranslation.T("RSMF.Business.Extensions.Active");
            else if (status.IsSubscribed && !status.IsInstalled) label += " · " + SimTranslation.T("RSMF.Business.Extensions.Subscribed");
            ShopUiVisualUtility.DrawCellLabel(rect, label,
                status.IsChecked ? ShopUiVisualUtility.PositiveText : ShopUiVisualUtility.WarningText);
        }

        //设置正文绘制状态，职责是让推荐封面和正文共享确定的字体、对齐和颜色。
        private static void ResetTextState()
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
