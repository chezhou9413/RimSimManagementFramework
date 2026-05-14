using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        private const int ReviewsPerPage = 8;
        private const float ReviewSummaryCardMinWidth = 128f;
        private const float ReviewSummaryCardPaddingX = 10f;
        private const float ReviewSummaryCardPaddingY = 9f;
        private const float ReviewAvatarSize = 76f;
        private const float ReviewTextLeftPadding = 104f;
        private const int ReviewSummaryCardCount = 4;

        /// <summary>
        /// 保存论坛评价线程视图，负责把主帖和回复集合组合成可展开的显示单元。
        /// </summary>
        private sealed class ReviewThreadView
        {
            public CustomerReviewRecord Root;
            public List<CustomerReviewRecord> Replies = new List<CustomerReviewRecord>();
        }

        /// <summary>
        /// 绘制顾客评价页，展示点评汇总、筛选和单条点评列表。
        /// </summary>
        private void DrawCustomerReviewsPage(Rect rect)
        {
            GameComponent_CustomerReviewManager manager = Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
            if (manager == null)
            {
                Widgets.NoneLabel(rect.center.y, rect.width, "(顾客评价组件未初始化)");
                return;
            }

            float summaryHeight = CalcReviewSummaryHeight(rect.width);
            Rect summaryRect = new Rect(rect.x, rect.y, rect.width, summaryHeight);
            DrawReviewSummary(summaryRect, manager);

            Rect filterRect = new Rect(rect.x, summaryRect.yMax + 6f, rect.width, 34f);
            List<CustomerReviewRecord> records = FilterAndSortReviews(manager.Records);
            List<ReviewThreadView> threads = BuildReviewThreads(records);
            DrawReviewFilters(filterRect, threads.Count);

            Rect listRect = new Rect(rect.x, filterRect.yMax + 8f, rect.width, Mathf.Max(0f, rect.yMax - filterRect.yMax - 8f));
            DrawReviewList(listRect, threads);
        }

        /// <summary>
        /// 计算评价汇总区高度，负责在窄窗口中给统计卡自动换行预留空间。
        /// </summary>
        private float CalcReviewSummaryHeight(float width)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt((width - 16f + 8f) / (ReviewSummaryCardMinWidth + 8f)));
            columns = Mathf.Min(5, columns);
            int rows = Mathf.CeilToInt(ReviewSummaryCardCount / (float)columns);
            float cardH = CalcReviewSummaryCardHeight();
            return 16f + rows * cardH + Mathf.Max(0, rows - 1) * 8f;
        }

        /// <summary>
        /// 计算评价汇总卡片宽度，负责避免统计标题和值在不同窗口宽度下被截断。
        /// </summary>
        private float CalcReviewSummaryCardWidth(float width)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt((width - 16f + 8f) / (ReviewSummaryCardMinWidth + 8f)));
            columns = Mathf.Min(5, columns);
            return Mathf.Max(ReviewSummaryCardMinWidth, (width - 16f - (columns - 1) * 8f) / columns);
        }

        private void DrawReviewSummary(Rect rect, GameComponent_CustomerReviewManager manager)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));
            int today = GenDate.DaysPassed;
            List<CustomerReviewRecord> allRecords = manager.Records?.Where(r => r != null).ToList() ?? new List<CustomerReviewRecord>();
            int rootCount = CountRootReviews(allRecords);
            int replyCount = CountForumReplies(allRecords);
            int todayCount = CountRootReviews(allRecords.Where(r => r.gameDay == today));
            string[] titles = { "平均星级", "主帖", "回复", "今日新增" };
            string[] values =
            {
                $"{manager.GetOverallAverageStars():F1} ★",
                rootCount.ToString(),
                replyCount.ToString(),
                todayCount.ToString()
            };
            float cardW = CalcReviewSummaryCardWidth(rect.width);
            float cardH = CalcReviewSummaryCardHeight();
            int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width - 16f + 8f) / (cardW + 8f)));
            columns = Mathf.Min(5, columns);
            for (int i = 0; i < titles.Length; i++)
            {
                int col = i % columns;
                int row = i / columns;
                Rect cardRect = new Rect(rect.x + 8f + col * (cardW + 8f), rect.y + 8f + row * (cardH + 8f), cardW, cardH);
                DrawReviewSummaryCard(cardRect, titles[i], values[i]);
            }
            ResetText();
        }

        /// <summary>
        /// 统计论坛主帖数量，负责让顶部数量和实际列表线程数量保持一致。
        /// </summary>
        private static int CountRootReviews(IEnumerable<CustomerReviewRecord> records)
        {
            if (records == null) return 0;
            List<CustomerReviewRecord> list = records.Where(r => r != null).ToList();
            HashSet<string> replyIds = BuildValidReplyIds(list);
            return list.Count(r => !replyIds.Contains(r.reviewId));
        }

        /// <summary>
        /// 统计论坛回复数量，负责把折叠楼中楼和主帖数量分开展示。
        /// </summary>
        private static int CountForumReplies(IEnumerable<CustomerReviewRecord> records)
        {
            if (records == null) return 0;
            return BuildValidReplyIds(records.Where(r => r != null).ToList()).Count;
        }

        /// <summary>
        /// 收集有效回复记录编号，负责只把能挂到现有主帖的记录视为回复。
        /// </summary>
        private static HashSet<string> BuildValidReplyIds(List<CustomerReviewRecord> records)
        {
            HashSet<string> ids = new HashSet<string>();
            if (records.NullOrEmpty()) return ids;

            HashSet<string> allIds = new HashSet<string>(records.Where(r => !string.IsNullOrEmpty(r.reviewId)).Select(r => r.reviewId));
            for (int i = 0; i < records.Count; i++)
            {
                CustomerReviewRecord record = records[i];
                if (record == null || string.IsNullOrEmpty(record.reviewId) || string.IsNullOrEmpty(record.replyToReviewId)) continue;
                if (allIds.Contains(record.replyToReviewId))
                    ids.Add(record.reviewId);
            }
            return ids;
        }

        /// <summary>
        /// 计算评价汇总卡片高度，负责按当前字体回退和 UI 缩放预留垂直空间。
        /// </summary>
        private float CalcReviewSummaryCardHeight()
        {
            float titleH = Text.LineHeightOf(GameFont.Tiny);
            float valueH = Text.LineHeightOf(GameFont.Small);
            return ReviewSummaryCardPaddingY * 2f + titleH + 8f + valueH;
        }

        private void DrawReviewSummaryCard(Rect rect, string title, string value)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
            float titleH = Text.LineHeightOf(GameFont.Tiny);
            float valueH = Text.LineHeightOf(GameFont.Small);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + ReviewSummaryCardPaddingX, rect.y + ReviewSummaryCardPaddingY, rect.width - ReviewSummaryCardPaddingX * 2f, titleH + 2f), title);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + ReviewSummaryCardPaddingX, rect.y + ReviewSummaryCardPaddingY + titleH + 8f, rect.width - ReviewSummaryCardPaddingX * 2f, valueH + 2f), value);
            ResetText();
        }

        private void DrawReviewFilters(Rect rect, int totalCount)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            Rect shopRect = new Rect(rect.x + 8f, rect.y + 4f, 180f, rect.height - 8f);
            if (SimUiStyle.DrawSecondaryButton(shopRect, BuildReviewFilterLabel(), true, GameFont.Tiny))
                OpenReviewShopFilterMenu();

            string[] sortLabels = { "最新", "高星", "低星" };
            float x = shopRect.xMax + 10f;
            for (int i = 0; i < sortLabels.Length; i++)
            {
                Rect sortRect = new Rect(x, rect.y + 4f, 76f, rect.height - 8f);
                if (SimUiStyle.DrawTabButton(sortRect, sortLabels[i], reviewSortMode == i, CDim))
                {
                    reviewSortMode = i;
                    reviewScrollPos = Vector2.zero;
                    reviewPageIndex = 0;
                }
                x += 84f;
            }

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)ReviewsPerPage));
            reviewPageIndex = Mathf.Clamp(reviewPageIndex, 0, pageCount - 1);
            Rect nextRect = new Rect(rect.xMax - 78f, rect.y + 4f, 66f, rect.height - 8f);
            Rect pageRect = new Rect(nextRect.x - 104f, rect.y + 4f, 96f, rect.height - 8f);
            Rect prevRect = new Rect(pageRect.x - 74f, rect.y + 4f, 66f, rect.height - 8f);
            if (SimUiStyle.DrawSecondaryButton(prevRect, "上一页", reviewPageIndex > 0, GameFont.Tiny))
            {
                reviewPageIndex--;
                reviewScrollPos = Vector2.zero;
            }
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = CDim;
            Widgets.Label(pageRect, $"{reviewPageIndex + 1}/{pageCount}");
            ResetText();
            if (SimUiStyle.DrawSecondaryButton(nextRect, "下一页", reviewPageIndex < pageCount - 1, GameFont.Tiny))
            {
                reviewPageIndex++;
                reviewScrollPos = Vector2.zero;
            }
        }

        private void DrawReviewList(Rect rect, List<ReviewThreadView> threads)
        {
            float viewWidth = rect.width - 18f;
            if (threads.Count == 0)
            {
                Rect viewRect = new Rect(0f, 0f, viewWidth, rect.height + 1f);
                Widgets.BeginScrollView(rect, ref reviewScrollPos, viewRect);
                Widgets.NoneLabel(viewRect.center.y, viewRect.width, "(暂无顾客评价)");
                Widgets.EndScrollView();
                return;
            }

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(threads.Count / (float)ReviewsPerPage));
            reviewPageIndex = Mathf.Clamp(reviewPageIndex, 0, pageCount - 1);
            List<ReviewThreadView> pageThreads = threads.Skip(reviewPageIndex * ReviewsPerPage).Take(ReviewsPerPage).ToList();
            List<float> heights = pageThreads.Select(t => CalcReviewThreadHeight(t, viewWidth)).ToList();
            float totalH = heights.Sum() + 8f;
            Rect viewRectPaged = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height + 1f, totalH));
            Widgets.BeginScrollView(rect, ref reviewScrollPos, viewRectPaged);
            float y = 0f;
            for (int i = 0; i < pageThreads.Count; i++)
            {
                float rowH = heights[i];
                DrawReviewThread(new Rect(0f, y, viewWidth, rowH - 6f), pageThreads[i], i);
                y += rowH;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 计算论坛线程高度，负责给主帖、展开按钮和已展开回复预留空间。
        /// </summary>
        private float CalcReviewThreadHeight(ReviewThreadView thread, float width)
        {
            if (thread?.Root == null) return 0f;
            float total = CalcReviewRowHeight(thread.Root, width, false);
            if (!thread.Replies.NullOrEmpty())
            {
                total += 32f;
                if (IsReviewRepliesExpanded(thread.Root.reviewId))
                {
                    for (int i = 0; i < thread.Replies.Count; i++)
                    {
                        total += CalcForumReplyRowHeight(thread.Replies[i], Mathf.Max(220f, width - 94f)) + 6f;
                    }
                }
            }
            return total;
        }

        private float CalcReviewRowHeight(CustomerReviewRecord record, float width, bool includeOwnReply)
        {
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            float textWidth = Mathf.Max(260f, width - ReviewTextLeftPadding - 14f);
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                float reviewH = Mathf.Max(Text.LineHeight, Text.CalcHeight(record.reviewText ?? "", textWidth));
                Text.Font = GameFont.Tiny;
                float metaH = Mathf.Max(Text.LineHeight, Text.CalcHeight(BuildForumMeta(record), textWidth));
                float replyH = includeOwnReply && !string.IsNullOrWhiteSpace(record.replyText) ? Mathf.Max(Text.LineHeight, Text.CalcHeight(record.replyText, Mathf.Max(220f, textWidth - 28f))) + 32f : 0f;
                float attachmentH = record.featuredItems.NullOrEmpty() ? 0f : 46f;
                return Mathf.Max(ReviewAvatarSize + 34f, 54f + reviewH + metaH + attachmentH + replyH + 22f);
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

        /// <summary>
        /// 绘制论坛线程，负责主帖、展开按钮和回复列表的整体布局。
        /// </summary>
        private void DrawReviewThread(Rect rect, ReviewThreadView thread, int index)
        {
            if (thread?.Root == null) return;
            float rootH = CalcReviewRowHeight(thread.Root, rect.width, false);
            DrawReviewRow(new Rect(rect.x, rect.y, rect.width, rootH - 4f), thread.Root, index, false);

            if (thread.Replies.NullOrEmpty())
                return;

            float y = rect.y + rootH;
            Rect toggleRect = new Rect(rect.x + 68f, y, 150f, Mathf.Max(26f, Text.LineHeightOf(GameFont.Tiny) + 8f));
            bool expanded = IsReviewRepliesExpanded(thread.Root.reviewId);
            if (SimUiStyle.DrawSecondaryButton(toggleRect, expanded ? $"收起回复({thread.Replies.Count})" : $"展开回复({thread.Replies.Count})", true, GameFont.Tiny))
            {
                ToggleReviewReplies(thread.Root.reviewId);
            }

            if (!expanded)
                return;

            y += toggleRect.height + 4f;
            for (int i = 0; i < thread.Replies.Count; i++)
            {
                float replyH = CalcForumReplyRowHeight(thread.Replies[i], Mathf.Max(220f, rect.width - 94f));
                DrawForumReplyRow(new Rect(rect.x + 68f, y, rect.width - 78f, replyH), thread.Replies[i]);
                y += replyH + 6f;
            }
        }

        private void DrawReviewRow(Rect row, CustomerReviewRecord record, int index, bool includeOwnReply)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
            Widgets.DrawBoxSolid(row, index % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
            DrawBorder(row, new Color(1f, 1f, 1f, 0.10f));

            Rect avatarRect = new Rect(row.x + 10f, row.y + 10f, ReviewAvatarSize, ReviewAvatarSize);
            Texture2D avatar = CustomerReviewAvatarCache.LoadAvatar(record.avatarImageId);
            GUI.color = Color.white;
            GUI.DrawTexture(avatarRect, avatar ?? BaseContent.GreyTex, ScaleMode.ScaleToFit);
            DrawBorder(avatarRect, new Color(1f, 1f, 1f, 0.18f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + ReviewTextLeftPadding, row.y + 8f, 240f, Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 2f)), record.aiNickname.Truncate(220f));
            GUI.color = new Color(0.95f, 0.78f, 0.20f, 1f);
            Widgets.Label(new Rect(row.x + ReviewTextLeftPadding + 246f, row.y + 8f, 120f, Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 2f)), BuildStars(record.stars));
            GUI.color = CDim;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Rect voteRect = new Rect(row.x + ReviewTextLeftPadding + 372f, row.y + 10f, row.width - ReviewTextLeftPadding - 386f, Mathf.Max(22f, Text.LineHeightOf(GameFont.Tiny) + 2f));
            if (voteRect.width >= 96f)
                Widgets.Label(voteRect, $"#{record.reviewId.Truncate(8)}  赞 {record.upvotes}  踩 {record.downvotes}");
            Text.Anchor = TextAnchor.UpperLeft;

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float textWidth = row.width - ReviewTextLeftPadding - 14f;
            float reviewH = Mathf.Max(Text.LineHeight, Text.CalcHeight(record.reviewText ?? "", textWidth));
            Rect reviewRect = new Rect(row.x + ReviewTextLeftPadding, row.y + 38f, textWidth, reviewH);
            Widgets.Label(reviewRect, record.reviewText ?? "");

            GUI.color = CDim;
            Text.Font = GameFont.Tiny;
            string meta = BuildForumMeta(record);
            float metaY = reviewRect.yMax + 6f;
            float metaH = Mathf.Max(Text.LineHeight, Text.CalcHeight(meta, textWidth));
            Widgets.Label(new Rect(row.x + ReviewTextLeftPadding, metaY, textWidth, metaH), meta);

            float y = metaY + metaH + 8f;
            if (!record.featuredItems.NullOrEmpty())
            {
                DrawReviewFeaturedItems(new Rect(row.x + ReviewTextLeftPadding, y, textWidth, 40f), record);
                y += 46f;
            }

            if (includeOwnReply && !string.IsNullOrWhiteSpace(record.replyText))
            {
                DrawForumReply(new Rect(row.x + ReviewTextLeftPadding, y, textWidth, row.yMax - y - 8f), record);
            }
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
        /// 计算折叠回复行高度，负责动态测量回复文本避免中文截断。
        /// </summary>
        private static float CalcForumReplyRowHeight(CustomerReviewRecord reply, float width)
        {
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                string title = BuildReplyTitle(reply);
                float titleH = Mathf.Max(Text.LineHeight, Text.CalcHeight(title, width - 20f));
                Text.Font = GameFont.Small;
                float bodyH = Mathf.Max(Text.LineHeight, Text.CalcHeight(reply?.replyText ?? reply?.reviewText ?? "", width - 20f));
                return Mathf.Max(72f, titleH + bodyH + 24f);
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

        /// <summary>
        /// 绘制展开后的单条回复，负责呈现楼中楼回复内容和作者信息。
        /// </summary>
        private void DrawForumReplyRow(Rect rect, CustomerReviewRecord reply)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
                DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Tiny;
                GUI.color = CDim;
                string title = BuildReplyTitle(reply);
                float titleH = Mathf.Max(Text.LineHeight, Text.CalcHeight(title, rect.width - 20f));
                Widgets.Label(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, titleH), title);

                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                float bodyY = rect.y + 10f + titleH;
                string body = string.IsNullOrWhiteSpace(reply.replyText) ? reply.reviewText : reply.replyText;
                Widgets.Label(new Rect(rect.x + 10f, bodyY, rect.width - 20f, Mathf.Max(Text.LineHeight, rect.yMax - bodyY - 8f)), body);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private void DrawReviewFeaturedItems(Rect rect, CustomerReviewRecord record)
        {
            if (record.featuredItems.NullOrEmpty()) return;
            float size = 34f;
            float x = rect.x;
            for (int i = 0; i < record.featuredItems.Count && i < 6; i++)
            {
                ReviewFeaturedItem item = record.featuredItems[i];
                Rect iconRect = new Rect(x, rect.y + 2f, size, size);
                ThingDef def = !string.IsNullOrEmpty(item.defName) ? DefDatabase<ThingDef>.GetNamedSilentFail(item.defName) : null;
                Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.25f));
                DrawBorder(iconRect, new Color(1f, 1f, 1f, 0.10f));
                if (def != null)
                    Widgets.ThingIcon(iconRect.ContractedBy(3f), def);
                else
                    Widgets.Label(iconRect, "套");
                TooltipHandler.TipRegion(iconRect, item.label);
                x += size + 6f;
                if (x + size > rect.xMax) break;
            }
        }

        /// <summary>
        /// 绘制论坛回复块，负责把 AI 生成的支持、反驳或补充挂到目标评论下方。
        /// </summary>
        private void DrawForumReply(Rect rect, CustomerReviewRecord record)
        {
            if (rect.height <= 24f) return;
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = CDim;
            string title = string.IsNullOrWhiteSpace(record.replyToNickname)
                ? "回复上一条讨论"
                : $"回复 {record.replyToNickname}";
            if (!string.IsNullOrWhiteSpace(record.replyStance))
                title += " · " + record.replyStance;

            float titleH = Mathf.Max(Text.LineHeight, Text.CalcHeight(title, rect.width - 18f));
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 18f, titleH), title);
            GUI.color = Color.white;
            float bodyY = rect.y + 8f + titleH;
            Widgets.Label(new Rect(rect.x + 10f, bodyY, rect.width - 18f, Mathf.Max(Text.LineHeight, rect.yMax - bodyY - 6f)), record.replyText);
        }

        /// <summary>
        /// 构造论坛帖子的底部元信息，负责把日期、店铺、消费和服务摘要压成一行可换行文本。
        /// </summary>
        private static string BuildForumMeta(CustomerReviewRecord record)
        {
            string purchase = BuildPublicPurchaseSummary(record);
            string service = BuildPublicServiceSummary(record);
            return $"第 {record.gameDay} 天 · {record.zoneLabel} · 实际付款 {record.spentSilver:F0} 银 · {purchase} · {service}";
        }

        /// <summary>
        /// 构造公开购买摘要，负责避免把只给模型看的商品说明正文显示到论坛 UI。
        /// </summary>
        private static string BuildPublicPurchaseSummary(CustomerReviewRecord record)
        {
            if (record == null)
                return "无购买摘要";

            if (!record.featuredItems.NullOrEmpty())
            {
                List<string> parts = new List<string>();
                for (int i = 0; i < record.featuredItems.Count && i < 6; i++)
                {
                    ReviewFeaturedItem item = record.featuredItems[i];
                    if (item == null) continue;
                    string label = string.IsNullOrWhiteSpace(item.label) ? item.defName : item.label;
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    string count = item.count > 0 ? "x" + item.count : "";
                    parts.Add($"{label}{count}，{item.amount:F0} 银");
                }
                if (parts.Count > 0)
                    return string.Join("；", parts);
            }

            return StripModelOnlyDescriptions(record.purchasedSummary, "无购买摘要");
        }

        /// <summary>
        /// 构造公开服务摘要，负责移除服务定义说明等模型专用描述。
        /// </summary>
        private static string BuildPublicServiceSummary(CustomerReviewRecord record)
        {
            if (record == null)
                return "无服务摘要";

            return StripModelOnlyDescriptions(record.serviceSummary, "无服务摘要");
        }

        /// <summary>
        /// 清理模型专用说明片段，负责兼容旧存档中已经保存的富摘要文本。
        /// </summary>
        private static string StripModelOnlyDescriptions(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            List<string> segments = value.Split(new[] { '；' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            if (segments.Count == 0)
                return fallback;

            for (int i = 0; i < segments.Count; i++)
            {
                int index = segments[i].IndexOf("，说明:", System.StringComparison.Ordinal);
                if (index < 0)
                    index = segments[i].IndexOf("，说明：", System.StringComparison.Ordinal);
                if (index >= 0)
                    segments[i] = segments[i].Substring(0, index).Trim();
            }

            return string.Join("；", segments);
        }

        private List<CustomerReviewRecord> FilterAndSortReviews(IReadOnlyList<CustomerReviewRecord> source)
        {
            IEnumerable<CustomerReviewRecord> query = source?.Where(r => r != null) ?? Enumerable.Empty<CustomerReviewRecord>();
            if (reviewFilterZoneId != int.MinValue)
                query = query.Where(r => r.zoneId == reviewFilterZoneId);

            if (reviewSortMode == 1)
                query = query.OrderByDescending(r => r.stars).ThenByDescending(r => r.tickAbs);
            else if (reviewSortMode == 2)
                query = query.OrderBy(r => r.stars).ThenByDescending(r => r.tickAbs);
            else
                query = OrderForumThread(query);

            return query.ToList();
        }

        /// <summary>
        /// 构造论坛线程列表，负责把 AI 回复归入被回复主帖，避免回复在主列表里重复显示。
        /// </summary>
        private static List<ReviewThreadView> BuildReviewThreads(List<CustomerReviewRecord> records)
        {
            List<ReviewThreadView> threads = new List<ReviewThreadView>();
            if (records.NullOrEmpty()) return threads;

            Dictionary<string, ReviewThreadView> byId = new Dictionary<string, ReviewThreadView>();
            HashSet<string> replyIds = new HashSet<string>();
            for (int i = 0; i < records.Count; i++)
            {
                CustomerReviewRecord record = records[i];
                if (record == null || string.IsNullOrEmpty(record.reviewId)) continue;
                if (!byId.ContainsKey(record.reviewId))
                    byId.Add(record.reviewId, new ReviewThreadView { Root = record });
            }

            for (int i = 0; i < records.Count; i++)
            {
                CustomerReviewRecord record = records[i];
                if (record == null || string.IsNullOrEmpty(record.replyToReviewId)) continue;
                if (byId.TryGetValue(record.replyToReviewId, out ReviewThreadView parent))
                {
                    parent.Replies.Add(record);
                    replyIds.Add(record.reviewId);
                }
            }

            for (int i = 0; i < records.Count; i++)
            {
                CustomerReviewRecord record = records[i];
                if (record == null || replyIds.Contains(record.reviewId)) continue;
                if (byId.TryGetValue(record.reviewId, out ReviewThreadView thread))
                {
                    thread.Replies = thread.Replies.OrderBy(r => r.tickAbs).ToList();
                    threads.Add(thread);
                }
            }

            return threads;
        }

        /// <summary>
        /// 判断指定评论的回复是否展开，负责保持玩家本次打开窗口内的论坛折叠状态。
        /// </summary>
        private bool IsReviewRepliesExpanded(string reviewId)
        {
            return !string.IsNullOrEmpty(reviewId) && expandedReviewReplyIds.Contains(reviewId);
        }

        /// <summary>
        /// 切换评论回复展开状态，负责让玩家像论坛一样展开或收起楼中楼。
        /// </summary>
        private void ToggleReviewReplies(string reviewId)
        {
            if (string.IsNullOrEmpty(reviewId)) return;
            if (expandedReviewReplyIds.Contains(reviewId))
                expandedReviewReplyIds.Remove(reviewId);
            else
                expandedReviewReplyIds.Add(reviewId);
        }

        /// <summary>
        /// 构造回复标题，负责统一展示作者、被回复者和回复立场。
        /// </summary>
        private static string BuildReplyTitle(CustomerReviewRecord reply)
        {
            string author = string.IsNullOrWhiteSpace(reply?.aiNickname) ? "匿名用户" : reply.aiNickname;
            string target = string.IsNullOrWhiteSpace(reply?.replyToNickname) ? "上文" : reply.replyToNickname;
            string stance = string.IsNullOrWhiteSpace(reply?.replyStance) ? "回复" : reply.replyStance;
            return $"{author} 回复 {target} · {stance}";
        }

        /// <summary>
        /// 按论坛流排序评价，负责让回复尽量贴近被回复的主帖显示。
        /// </summary>
        private static IEnumerable<CustomerReviewRecord> OrderForumThread(IEnumerable<CustomerReviewRecord> source)
        {
            List<CustomerReviewRecord> all = source.OrderByDescending(r => r.tickAbs).ToList();
            HashSet<string> emitted = new HashSet<string>();
            for (int i = 0; i < all.Count; i++)
            {
                CustomerReviewRecord record = all[i];
                if (record == null || emitted.Contains(record.reviewId)) continue;
                yield return record;
                emitted.Add(record.reviewId);

                List<CustomerReviewRecord> replies = all
                    .Where(r => r != null && r.replyToReviewId == record.reviewId && !emitted.Contains(r.reviewId))
                    .OrderBy(r => r.tickAbs)
                    .ToList();
                for (int j = 0; j < replies.Count; j++)
                {
                    yield return replies[j];
                    emitted.Add(replies[j].reviewId);
                }
            }
        }

        private string BuildReviewFilterLabel()
        {
            if (reviewFilterZoneId == int.MinValue) return "全部店铺";
            Zone_Shop zone = CollectAllShops().Select(s => s.Zone).FirstOrDefault(z => z.ID == reviewFilterZoneId);
            return zone != null ? zone.label.Truncate(150f) : "店铺 #" + reviewFilterZoneId;
        }

        private void OpenReviewShopFilterMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("全部店铺", () => reviewFilterZoneId = int.MinValue)
            };
            foreach (ShopViewData shop in CollectAllShops())
            {
                int id = shop.Zone.ID;
                string label = shop.Zone.label;
                options.Add(new FloatMenuOption(label, () => reviewFilterZoneId = id));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string BuildStars(int stars)
        {
            stars = Mathf.Clamp(stars, 1, 5);
            return new string('★', stars) + new string('☆', 5 - stars);
        }
    }
}
