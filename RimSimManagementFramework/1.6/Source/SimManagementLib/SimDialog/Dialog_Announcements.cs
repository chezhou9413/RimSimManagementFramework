using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //展示本地公告，职责是分页呈现简介、截图和工坊入口并记录实际展示的公告。
    public sealed class Dialog_Announcements : Window
    {
        private readonly List<AnnouncementItemData> announcements;
        private AnnouncementScreenshotGallery gallery;
        private Vector2 scrollPos;
        private int page;

        //初始化公告窗口，职责是复制本次未读列表并配置关闭与输入行为。
        public Dialog_Announcements(List<AnnouncementItemData> announcements)
        {
            this.announcements = new List<AnnouncementItemData>(announcements);
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(960f, UI.screenWidth - 48f), Mathf.Min(790f, UI.screenHeight - 48f));

        //在窗口成功打开后展示第一页，职责是只将玩家实际打开的公告记为已读。
        public override void PostOpen()
        {
            base.PostOpen();
            ShowPage(0);
        }

        //切换当前公告，职责是清理滚动和截图选择状态并持久化当前条目的已读标识。
        private void ShowPage(int index)
        {
            page = index;
            scrollPos = Vector2.zero;
            gallery = new AnnouncementScreenshotGallery(announcements[page].screenshotPaths);
            AnnouncementClientState.MarkAsRead(new List<AnnouncementItemData> { announcements[page] });
        }

        //绘制公告界面，职责是为标题、正文画廊和固定操作栏保留空间并恢复绘制状态。
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
                AnnouncementItemData item = announcements[page];
                string hint = SimTranslation.T(item.popupOnce ? "RSMF.Announcement.OnceHint" : "RSMF.Announcement.PopupTitle");
                float top = ShopUiVisualUtility.DrawPageHeading(inRect, item.title, hint, true);
                float control = ShopUiVisualUtility.ControlHeight();
                float footerHeight = control + (announcements.Count > 1 ? control + 8f : 0f);
                Rect body = new Rect(inRect.x, inRect.y + top, inRect.width,
                    Mathf.Max(0f, inRect.height - top - footerHeight - 12f));
                DrawBody(body, item);
                DrawFooter(new Rect(inRect.x, inRect.yMax - footerHeight, inRect.width, footerHeight), item, control);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        //绘制公告正文与本地截图，职责是按真实文本高度滚动显示内容并保持底部按钮可见。
        private void DrawBody(Rect rect, AnnouncementItemData item)
        {
            float width = Mathf.Max(1f, rect.width - 18f);
            float introHeight = Text.CalcHeight(item.body, width - 24f) + 24f;
            float galleryHeight = gallery.Height(width);
            Rect view = new Rect(0f, 0f, width, Mathf.Max(rect.height, introHeight + 12f + galleryHeight));
            Widgets.BeginScrollView(rect, ref scrollPos, view);
            try
            {
                Rect intro = new Rect(0f, 0f, width, introHeight);
                ShopUiVisualUtility.DrawSection(intro);
                Widgets.Label(intro.ContractedBy(12f), item.body);
                gallery.Draw(new Rect(0f, intro.yMax + 12f, width, galleryHeight));
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制固定操作栏，职责是提供关闭、公告翻页和前往工坊订阅的明确入口。
        private void DrawFooter(Rect rect, AnnouncementItemData item, float control)
        {
            if (announcements.Count > 1)
            {
                if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(rect.x, rect.y, 120f, control),
                    SimTranslation.T("RSMF.Announcement.Previous"), page > 0))
                    ShowPage(page - 1);
                if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(rect.xMax - 120f, rect.y, 120f, control),
                    SimTranslation.T("RSMF.Announcement.Next"), page < announcements.Count - 1))
                    ShowPage(page + 1);
                ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 132f, rect.y, rect.width - 264f, control),
                    SimTranslation.T("RSMF.Announcement.Page", (page + 1).Named("page"), announcements.Count.Named("count")));
            }

            float buttonWidth = Mathf.Min(240f, (rect.width - 12f) / 2f);
            float y = rect.yMax - control;
            if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(rect.x, y, buttonWidth, control),
                SimTranslation.T("RSMF.Announcement.Close")))
                Close();

            if (!string.IsNullOrWhiteSpace(item.workshopUrl)
                && ShopUiVisualUtility.DrawPrimaryButton(new Rect(rect.xMax - buttonWidth, y, buttonWidth, control),
                    SimTranslation.T("RSMF.Announcement.Subscribe")))
                BusinessExtensionRecommendationUtility.OpenWorkshopUrl(item.workshopUrl);
        }
    }
}
