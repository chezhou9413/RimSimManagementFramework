using SimManagementLib.GameComp;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        /// <summary>
        /// 绘制收藏品兑换首页，负责按 Def 优先级展示可进入的兑换列表入口。
        /// </summary>
        private void DrawCollectibleExchangePage(Rect rect)
        {
            List<CollectibleExchangeListDef> entries = GetVisibleCollectibleExchangeLists();
            if (entries.NullOrEmpty())
            {
                Widgets.NoneLabel(rect.center.y, rect.width, SimTranslation.T("RSMF.Business.Empty.NoCollectibleExchangeLists"));
                return;
            }

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(58f, Text.LineHeightOf(GameFont.Small) + Text.LineHeightOf(GameFont.Tiny) + 22f));
            DrawCollectibleExchangeHeader(headerRect, entries.Count);

            Rect listRect = new Rect(rect.x, headerRect.yMax + 8f, rect.width, Mathf.Max(120f, rect.height - headerRect.height - 8f));
            float viewWidth = listRect.width - 18f;
            List<float> heights = entries.Select(entry => CalcCollectibleExchangeRowHeight(entry, viewWidth)).ToList();
            float viewHeight = Mathf.Max(listRect.height + 1f, heights.Sum() + 8f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            Widgets.BeginScrollView(listRect, ref collectibleExchangeScrollPos, viewRect);
            float y = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                float rowHeight = heights[i];
                DrawCollectibleExchangeRow(new Rect(0f, y, viewWidth, rowHeight - 6f), entries[i], i);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 读取可显示的收藏品兑换入口 Def，负责统一过滤和排序规则。
        /// </summary>
        private static List<CollectibleExchangeListDef> GetVisibleCollectibleExchangeLists()
        {
            return DefDatabase<CollectibleExchangeListDef>.AllDefsListForReading
                .Where(def => def != null && def.visible)
                .OrderByDescending(def => def.priority)
                .ThenBy(def => GetCollectibleExchangeTitle(def))
                .ThenBy(def => def.defName)
                .ToList();
        }

        /// <summary>
        /// 绘制收藏品兑换首页摘要，负责说明当前首页入口数量和后续二级商店用途。
        /// </summary>
        private void DrawCollectibleExchangeHeader(Rect rect, int count)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f)),
                SimTranslation.T("RSMF.Business.CollectibleExchange.Header", count.Named("count")));

            Text.Font = GameFont.Tiny;
            GUI.color = CDim;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, Mathf.Max(20f, rect.height - 34f)),
                SimTranslation.T("RSMF.Business.CollectibleExchange.HeaderTip"));
            ResetText();
        }

        /// <summary>
        /// 计算收藏品兑换入口行高，负责适配翻译后的长介绍文本。
        /// </summary>
        private static float CalcCollectibleExchangeRowHeight(CollectibleExchangeListDef entry, float width)
        {
            float innerWidth = Mathf.Max(120f, width - 156f);
            Text.Font = GameFont.Tiny;
            float introHeight = Text.CalcHeight(GetCollectibleExchangeIntro(entry), innerWidth);
            float progressHeight = Mathf.Max(48f, Text.LineHeightOf(GameFont.Tiny) + 28f);
            return Mathf.Max(112f, 70f + introHeight + progressHeight);
        }

        /// <summary>
        /// 绘制单个收藏品兑换入口，负责展示商店名、标题、介绍、进度和进入二级商店按钮。
        /// </summary>
        private void DrawCollectibleExchangeRow(Rect row, CollectibleExchangeListDef entry, int index)
        {
            Widgets.DrawBoxSolid(row, index % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
            if (Mouse.IsOver(row))
                Widgets.DrawBoxSolid(row, new Color(0.25f, 0.65f, 0.85f, 0.08f));
            DrawBorder(row, new Color(1f, 1f, 1f, 0.10f));

            string shopName = GetCollectibleExchangeShopName(entry);
            string title = GetCollectibleExchangeTitle(entry);
            string intro = GetCollectibleExchangeIntro(entry);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 10f, row.y + 6f, row.width - 220f, Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f)),
                SimTranslation.T("RSMF.Business.CollectibleExchange.RowTitle", shopName.Named("shop"), title.Named("title")));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = CDim;
            Widgets.Label(new Rect(row.xMax - 196f, row.y + 8f, 90f, Mathf.Max(20f, Text.LineHeightOf(GameFont.Tiny) + 4f)),
                SimTranslation.T("RSMF.Business.CollectibleExchange.Priority", entry.priority.Named("priority")));

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = CDim;
            float introY = row.y + 36f;
            float textWidth = Mathf.Max(120f, row.width - 150f);
            float introHeight = Text.CalcHeight(intro, textWidth);
            Widgets.Label(new Rect(row.x + 10f, introY, textWidth, introHeight), intro);

            float progressY = introY + introHeight + 8f;
            DrawCollectibleExchangeProgress(new Rect(row.x + 10f, progressY, textWidth, Mathf.Max(42f, row.yMax - progressY - 8f)), entry);

            Rect enterRect = new Rect(row.xMax - 96f, row.yMax - 42f, 82f, 30f);
            bool open = SimUiStyle.DrawPrimaryButton(enterRect, SimTranslation.T("RSMF.Business.CollectibleExchange.Enter"), true, GameFont.Small);
            ResetText();

            if (!open && Widgets.ButtonInvisible(row, false))
                open = true;

            if (open)
                Find.WindowStack.Add(new Dialog_CollectibleExchangeShop(entry));
        }

        /// <summary>
        /// 绘制收藏品兑换首页进度条，负责显示当前购买次数、剩余货量和总限购次数。
        /// </summary>
        private void DrawCollectibleExchangeProgress(Rect rect, CollectibleExchangeListDef entry)
        {
            CollectibleExchangeProgress progress = GetCollectibleExchangeProgress(entry);
            string label = SimTranslation.T(
                "RSMF.Business.CollectibleExchange.StockProgress",
                progress.Purchased.Named("purchased"),
                progress.Remaining.Named("remaining"),
                progress.Total.Named("total"));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = CAccent;
            float labelHeight = Mathf.Max(20f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, labelHeight), label);

            Rect barRect = new Rect(rect.x, rect.y + labelHeight + 4f, rect.width, 14f);
            Widgets.DrawBoxSolid(barRect, new Color(0f, 0f, 0f, 0.35f));
            Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(progress.Percent), barRect.height);
            Widgets.DrawBoxSolid(fillRect, CAccent);
            DrawBorder(barRect, new Color(1f, 1f, 1f, 0.16f));
        }

        /// <summary>
        /// 读取收藏品兑换首页进度，负责在游戏组件缺失时仍返回可显示的静态总量。
        /// </summary>
        private static CollectibleExchangeProgress GetCollectibleExchangeProgress(CollectibleExchangeListDef entry)
        {
            GameComponent_CollectibleExchangeManager manager = Current.Game?.GetComponent<GameComponent_CollectibleExchangeManager>();
            if (manager != null)
                return manager.GetProgress(entry);

            CollectibleExchangeProgress progress = new CollectibleExchangeProgress();
            if (entry?.items == null)
                return progress;

            for (int i = 0; i < entry.items.Count; i++)
            {
                CollectibleExchangeItemEntry item = entry.items[i];
                if (item == null || item.maxPurchases <= 0)
                    continue;

                progress.Total += item.maxPurchases;
            }

            progress.Remaining = progress.Total;
            return progress;
        }

        /// <summary>
        /// 读取收藏品兑换入口商店名，负责在缺省时给出本地化兜底文本。
        /// </summary>
        private static string GetCollectibleExchangeShopName(CollectibleExchangeListDef entry)
        {
            return string.IsNullOrWhiteSpace(entry?.shopName)
                ? SimTranslation.T("RSMF.Business.CollectibleExchange.DefaultShopName")
                : entry.shopName;
        }

        /// <summary>
        /// 读取收藏品兑换入口标题，负责在缺省时回退到 Def 标签。
        /// </summary>
        private static string GetCollectibleExchangeTitle(CollectibleExchangeListDef entry)
        {
            if (!string.IsNullOrWhiteSpace(entry?.title))
                return entry.title;
            if (entry != null && !entry.LabelCap.RawText.NullOrEmpty())
                return entry.LabelCap.RawText;
            return entry?.defName ?? "";
        }

        /// <summary>
        /// 读取收藏品兑换入口介绍，负责在缺省时回退到 Def 描述。
        /// </summary>
        private static string GetCollectibleExchangeIntro(CollectibleExchangeListDef entry)
        {
            if (!string.IsNullOrWhiteSpace(entry?.intro))
                return entry.intro;
            if (!string.IsNullOrWhiteSpace(entry?.description))
                return entry.description;
            return SimTranslation.T("RSMF.Business.CollectibleExchange.NoIntro");
        }
    }
}
