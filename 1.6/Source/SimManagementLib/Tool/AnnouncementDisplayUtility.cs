using SimManagementLib.Pojo;
using System;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供公告 UI 的通用格式化和测量方法，负责让弹窗与历史页保持一致布局。
    /// </summary>
    public static class AnnouncementDisplayUtility
    {
        private const float CardPadding = 10f;
        private const float CardGap = 10f;

        /// <summary>
        /// 格式化公告时间，负责把后端 ISO 时间转换成本机短时间文本。
        /// </summary>
        public static string FormatDisplayTime(string rawTime)
        {
            if (string.IsNullOrWhiteSpace(rawTime))
                return "";

            if (DateTimeOffset.TryParse(rawTime, out DateTimeOffset parsed))
                return parsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

            return StringEncodingUtility.SanitizeUtf16(rawTime);
        }

        /// <summary>
        /// 计算联网公告卡片高度，负责为滚动区域提供准确 viewRect 高度。
        /// </summary>
        public static float CalcNetworkCardHeight(AnnouncementNetworkItemData item, float width)
        {
            string title = string.IsNullOrWhiteSpace(item?.title) ? SimTranslation.TOrFallback("RSMF.Announcement.Untitled", "Untitled") : item.title;
            string body = string.IsNullOrWhiteSpace(item?.body) ? SimTranslation.TOrFallback("RSMF.Announcement.EmptyBody", "No content.") : item.body;
            return CalcCardHeight(title, BuildPublishedMeta(item?.publishedAt), body, width);
        }

        /// <summary>
        /// 计算已读公告卡片高度，负责为历史滚动区域提供准确 viewRect 高度。
        /// </summary>
        public static float CalcHistoryCardHeight(AnnouncementReadRecord record, float width)
        {
            string title = string.IsNullOrWhiteSpace(record?.title) ? SimTranslation.TOrFallback("RSMF.Announcement.Untitled", "Untitled") : record.title;
            string body = string.IsNullOrWhiteSpace(record?.body) ? SimTranslation.TOrFallback("RSMF.Announcement.EmptyBody", "No content.") : record.body;
            return CalcCardHeight(title, BuildHistoryMeta(record), body, width);
        }

        /// <summary>
        /// 绘制联网公告卡片，负责在弹窗中展示标题、发布时间和正文。
        /// </summary>
        public static void DrawNetworkCard(Rect rect, AnnouncementNetworkItemData item, Color panelColor, Color dimColor)
        {
            string title = string.IsNullOrWhiteSpace(item?.title) ? SimTranslation.TOrFallback("RSMF.Announcement.Untitled", "Untitled") : item.title;
            string body = string.IsNullOrWhiteSpace(item?.body) ? SimTranslation.TOrFallback("RSMF.Announcement.EmptyBody", "No content.") : item.body;
            DrawCard(rect, title, BuildPublishedMeta(item?.publishedAt), body, panelColor, dimColor);
        }

        /// <summary>
        /// 绘制已读公告卡片，负责在历史页展示本机保存的公告快照。
        /// </summary>
        public static void DrawHistoryCard(Rect rect, AnnouncementReadRecord record, Color panelColor, Color dimColor)
        {
            string title = string.IsNullOrWhiteSpace(record?.title) ? SimTranslation.TOrFallback("RSMF.Announcement.Untitled", "Untitled") : record.title;
            string body = string.IsNullOrWhiteSpace(record?.body) ? SimTranslation.TOrFallback("RSMF.Announcement.EmptyBody", "No content.") : record.body;
            DrawCard(rect, title, BuildHistoryMeta(record), body, panelColor, dimColor);
        }

        /// <summary>
        /// 返回公告卡片之间的间距，负责让外部滚动布局使用一致间距。
        /// </summary>
        public static float Gap()
        {
            return CardGap;
        }

        /// <summary>
        /// 计算通用公告卡片高度，负责根据标题、元信息和正文动态测量。
        /// </summary>
        private static float CalcCardHeight(string title, string meta, string body, float width)
        {
            float textWidth = Mathf.Max(80f, width - CardPadding * 2f);
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.WordWrap = true;
                Text.Font = GameFont.Small;
                float titleHeight = Mathf.Max(Text.LineHeightOf(GameFont.Small), Text.CalcHeight(title ?? "", textWidth));
                Text.Font = GameFont.Tiny;
                float metaHeight = Mathf.Max(Text.LineHeightOf(GameFont.Tiny), Text.CalcHeight(meta ?? "", textWidth));
                Text.Font = GameFont.Small;
                float bodyHeight = Mathf.Max(Text.LineHeightOf(GameFont.Small), Text.CalcHeight(body ?? "", textWidth));
                return CardPadding * 2f + titleHeight + 4f + metaHeight + 8f + bodyHeight;
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

        /// <summary>
        /// 绘制通用公告卡片，负责按测量高度排布标题、元信息和正文。
        /// </summary>
        private static void DrawCard(Rect rect, string title, string meta, string body, Color panelColor, Color dimColor)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Widgets.DrawBoxSolid(rect, panelColor);
                Rect inner = rect.ContractedBy(CardPadding);
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;

                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                float titleHeight = Mathf.Max(Text.LineHeightOf(GameFont.Small), Text.CalcHeight(title ?? "", inner.width));
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, titleHeight), title ?? "");

                Text.Font = GameFont.Tiny;
                GUI.color = dimColor;
                float metaY = inner.y + titleHeight + 4f;
                float metaHeight = Mathf.Max(Text.LineHeightOf(GameFont.Tiny), Text.CalcHeight(meta ?? "", inner.width));
                Widgets.Label(new Rect(inner.x, metaY, inner.width, metaHeight), meta ?? "");

                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                float bodyY = metaY + metaHeight + 8f;
                float bodyHeight = Mathf.Max(Text.LineHeightOf(GameFont.Small), Text.CalcHeight(body ?? "", inner.width));
                Widgets.Label(new Rect(inner.x, bodyY, inner.width, bodyHeight), body ?? "");
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
        /// 构建公告发布时间文本，负责在无时间时给出兜底元信息。
        /// </summary>
        private static string BuildPublishedMeta(string publishedAt)
        {
            string time = FormatDisplayTime(publishedAt);
            return string.IsNullOrWhiteSpace(time)
                ? SimTranslation.TOrFallback("RSMF.Announcement.Meta.NoTime", "Published")
                : SimTranslation.T("RSMF.Announcement.Meta.Published", time.Named("time"));
        }

        /// <summary>
        /// 构建历史公告元信息，负责同时展示发布时间和读取时间。
        /// </summary>
        private static string BuildHistoryMeta(AnnouncementReadRecord record)
        {
            string published = FormatDisplayTime(record?.publishedAt);
            string read = FormatDisplayTime(record?.readAt);
            if (string.IsNullOrWhiteSpace(published) && string.IsNullOrWhiteSpace(read))
                return SimTranslation.TOrFallback("RSMF.Announcement.Meta.NoTime", "Published");

            if (string.IsNullOrWhiteSpace(read))
                return SimTranslation.T("RSMF.Announcement.Meta.Published", published.Named("time"));

            if (string.IsNullOrWhiteSpace(published))
                return SimTranslation.T("RSMF.Announcement.Meta.Read", read.Named("time"));

            return SimTranslation.T("RSMF.Announcement.Meta.PublishedAndRead", published.Named("published"), read.Named("read"));
        }
    }
}
