using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        /// <summary>
        /// 绘制内置公告页，负责展示已读历史并提供手动检查入口。
        /// </summary>
        private void DrawAnnouncementsPage(Rect rect)
        {
            AnnouncementClientState.Tick();
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Rect headerRect = new Rect(rect.x, rect.y, rect.width, 46f);
                DrawAnnouncementHeader(headerRect);

                Rect historyRect = new Rect(rect.x, headerRect.yMax + 8f, rect.width, Mathf.Max(1f, rect.height - headerRect.height - 8f));
                DrawAnnouncementHistory(historyRect);
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
        /// 绘制公告页顶部工具栏，负责处理手动检查按钮和当前状态文本。
        /// </summary>
        private void DrawAnnouncementHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, CPanelAlt);
            Rect inner = rect.ContractedBy(8f);
            float buttonWidth = 140f;
            Rect buttonRect = new Rect(inner.x, inner.y + (inner.height - 32f) / 2f, buttonWidth, 32f);
            bool canClick = !AnnouncementClientState.IsChecking();
            if (SimUiStyle.DrawSecondaryButton(buttonRect, SimTranslation.TOrFallback("RSMF.Announcement.CheckNow", "Check now"), canClick, GameFont.Small))
                AnnouncementClientState.TryManualCheck();

            Rect statusRect = new Rect(buttonRect.xMax + 10f, inner.y, Mathf.Max(1f, inner.width - buttonWidth - 10f), inner.height);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = true;
            GUI.color = CDim;
            Widgets.Label(statusRect, AnnouncementClientState.StatusText);
        }

        /// <summary>
        /// 绘制已读公告历史，负责离线展示本机保存的公告快照。
        /// </summary>
        private void DrawAnnouncementHistory(Rect rect)
        {
            List<AnnouncementReadRecord> history = AnnouncementClientState.GetReadHistory();
            if (history.NullOrEmpty())
            {
                Widgets.DrawBoxSolid(rect, CPanelAlt);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = true;
                GUI.color = CDim;
                Widgets.Label(rect.ContractedBy(18f), SimTranslation.TOrFallback("RSMF.Announcement.HistoryEmpty", "No read announcements yet."));
                return;
            }

            float viewWidth = Mathf.Max(1f, rect.width - 18f);
            float totalHeight = AnnouncementDisplayUtility.Gap();
            for (int i = 0; i < history.Count; i++)
                totalHeight += AnnouncementDisplayUtility.CalcHistoryCardHeight(history[i], viewWidth) + AnnouncementDisplayUtility.Gap();

            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, totalHeight));
            Widgets.BeginScrollView(rect, ref announcementScrollPos, viewRect);
            float y = AnnouncementDisplayUtility.Gap();
            for (int i = 0; i < history.Count; i++)
            {
                float height = AnnouncementDisplayUtility.CalcHistoryCardHeight(history[i], viewWidth);
                Rect cardRect = new Rect(0f, y, viewWidth, height);
                AnnouncementDisplayUtility.DrawHistoryCard(cardRect, history[i], CPanelAlt, CDim);
                y += height + AnnouncementDisplayUtility.Gap();
            }
            Widgets.EndScrollView();
        }
    }
}
