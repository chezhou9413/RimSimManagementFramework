using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 展示未读公告弹窗，负责一次性呈现本次联网发现的新公告。
    /// </summary>
    public sealed class Dialog_Announcements : Window
    {
        private readonly List<AnnouncementNetworkItemData> announcements;
        private Vector2 scrollPos;

        /// <summary>
        /// 初始化公告弹窗，负责复制公告列表并配置标准关闭按钮。
        /// </summary>
        public Dialog_Announcements(List<AnnouncementNetworkItemData> announcements)
        {
            this.announcements = announcements ?? new List<AnnouncementNetworkItemData>();
            doCloseX = true;
            doCloseButton = true;
            closeOnAccept = true;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
            optionalTitle = SimTranslation.TOrFallback("RSMF.Announcement.PopupTitle", "Announcements");
        }

        public override Vector2 InitialSize => new Vector2(760f, 560f);

        /// <summary>
        /// 绘制公告弹窗内容，负责用滚动区域避免长正文裁剪到底部关闭按钮。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Rect bodyRect = new Rect(inRect.x, inRect.y, inRect.width, Mathf.Max(1f, inRect.height - Window.FooterRowHeight));
                DrawAnnouncementList(bodyRect);
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
        /// 绘制公告列表，负责按正文实际高度创建滚动视图。
        /// </summary>
        private void DrawAnnouncementList(Rect rect)
        {
            if (announcements.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                GUI.color = Color.white;
                Widgets.Label(rect, SimTranslation.TOrFallback("RSMF.Announcement.EmptyUnread", "No new announcements."));
                return;
            }

            float viewWidth = Mathf.Max(1f, rect.width - 18f);
            float totalHeight = AnnouncementDisplayUtility.Gap();
            for (int i = 0; i < announcements.Count; i++)
                totalHeight += AnnouncementDisplayUtility.CalcNetworkCardHeight(announcements[i], viewWidth) + AnnouncementDisplayUtility.Gap();

            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, totalHeight));
            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
            float y = AnnouncementDisplayUtility.Gap();
            for (int i = 0; i < announcements.Count; i++)
            {
                float height = AnnouncementDisplayUtility.CalcNetworkCardHeight(announcements[i], viewWidth);
                Rect cardRect = new Rect(0f, y, viewWidth, height);
                AnnouncementDisplayUtility.DrawNetworkCard(cardRect, announcements[i], new Color(1f, 1f, 1f, 0.06f), new Color(0.72f, 0.72f, 0.72f, 1f));
                y += height + AnnouncementDisplayUtility.Gap();
            }
            Widgets.EndScrollView();
        }
    }
}
