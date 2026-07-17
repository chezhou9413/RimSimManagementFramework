using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //单件商品货柜管理窗口，职责是展示殖民地可用实例和货柜逐槽上架状态。
    public sealed partial class Dialog_UniqueGoodsManager : Window
    {
        private const float Gap = 12f;
        private const float HeaderGap = 8f;
        private const float ScrollbarWidth = 18f;
        private static readonly Color Panel = new Color(0.12f, 0.13f, 0.15f, 0.96f);
        private static readonly Color RowAlt = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color Muted = new Color(0.70f, 0.74f, 0.80f, 1f);
        private static readonly Color Accent = new Color(0.25f, 0.65f, 0.85f, 1f);

        private readonly Building_UniqueGoodsContainer container;
        private readonly Dictionary<int, string> priceBuffers = new Dictionary<int, string>();
        private readonly List<Thing> available = new List<Thing>();
        private Vector2 availableScroll;
        private Vector2 listedScroll;
        private string search = "";
        private int sortMode;
        private float lastRefreshRealtime = float.NegativeInfinity;

        public override Vector2 InitialSize => new Vector2(1120f, 720f);

        //初始化窗口并绑定专业货柜。
        public Dialog_UniqueGoodsManager(Building_UniqueGoodsContainer container)
        {
            this.container = container;
            doCloseX = true;
            doCloseButton = false;
            forcePause = false;
            absorbInputAroundWindow = false;
            resizeable = true;
            draggable = true;
        }

        //绘制窗口标题、摘要和双栏列表。
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
                Text.WordWrap = false;
                GUI.color = Color.white;
                RefreshAvailableIfNeeded();

                float titleH = Text.LineHeightOf(GameFont.Medium) + 8f;
                float summaryH = Text.LineHeightOf(GameFont.Tiny) + 6f;
                float closeReserve = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f;
                Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width - closeReserve, titleH);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(titleRect, SimTranslation.T("RSMF.UniqueGoods.Title", container.StorageDisplayLabel.Named("label")));

                int listed = 0;
                int pending = 0;
                int reserved = 0;
                IReadOnlyList<UniqueGoodsSlotData> slots = container.UniqueSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    UniqueGoodsSlotData slot = slots[i];
                    if (slot == null) continue;
                    if (slot.IsReservedByCustomer) reserved++;
                    else if (container.GetStoredThing(slot) != null) listed++;
                    else if (slot.HasPendingSource) pending++;
                }
                Text.Font = GameFont.Tiny;
                GUI.color = Muted;
                Rect summaryRect = new Rect(inRect.x, titleRect.yMax, inRect.width, summaryH);
                Widgets.Label(summaryRect, SimTranslation.T("RSMF.UniqueGoods.Summary", listed.Named("listed"), pending.Named("pending"), reserved.Named("reserved"), container.UniqueSlotCount.Named("capacity")));

                Rect body = new Rect(inRect.x, summaryRect.yMax + HeaderGap, inRect.width, inRect.yMax - summaryRect.yMax - HeaderGap);
                float leftW = Mathf.Clamp(body.width * 0.56f, 320f, Mathf.Max(320f, body.width - 300f - Gap));
                DrawAvailablePanel(new Rect(body.x, body.y, leftW, body.height));
                DrawListedPanel(new Rect(body.x + leftW + Gap, body.y, body.width - leftW - Gap, body.height));
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        //绘制殖民地可上架物品列表。
        private void DrawAvailablePanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            Rect inner = rect.ContractedBy(10f);
            float line = Text.LineHeightOf(GameFont.Small) + 6f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, line), SimTranslation.T("RSMF.UniqueGoods.Available"));

            float toolsY = inner.y + line + 6f;
            string nextSearch = Widgets.TextField(new Rect(inner.x, toolsY, Mathf.Max(160f, inner.width - 180f), line), search);
            if (nextSearch != search)
            {
                search = nextSearch;
                NotifySearchChanged();
            }
            Rect sortRect = new Rect(inner.xMax - 170f, toolsY, 170f, line);
            if (SimUiStyle.DrawSecondaryButton(sortRect, SortLabel(), true, GameFont.Tiny))
            {
                sortMode = (sortMode + 1) % 4;
                InvalidateAvailableFilter();
            }

            List<Thing> rows = GetFilteredAvailableCached();
            float rowH = GetRowHeight();
            float pagerH = Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 10f);
            Rect pagerRect = new Rect(inner.x, inner.yMax - pagerH, inner.width, pagerH);
            Rect outRect = new Rect(inner.x, toolsY + line + 8f, inner.width, pagerRect.y - toolsY - line - 14f);
            ClampPage(ref availablePage, rows.Count, AvailablePageSize);
            int pageStart = availablePage * AvailablePageSize;
            int pageCount = Mathf.Min(AvailablePageSize, Mathf.Max(0, rows.Count - pageStart));
            Rect viewRect = new Rect(0f, 0f, outRect.width - ScrollbarWidth, Mathf.Max(outRect.height, pageCount * rowH));
            Widgets.BeginScrollView(outRect, ref availableScroll, viewRect);
            try
            {
                GetVisibleRowRange(availableScroll.y, outRect.height, rowH, pageCount, out int first, out int last);
                for (int i = first; i < last; i++)
                    DrawAvailableRow(new Rect(0f, i * rowH, viewRect.width, rowH - 3f), rows[pageStart + i], pageStart + i);
            }
            finally
            {
                Widgets.EndScrollView();
            }
            DrawPager(pagerRect, ref availablePage, rows.Count, AvailablePageSize, ref availableScroll);
        }

        //绘制当前上架和等待补货槽位列表。
        private void DrawListedPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            Rect inner = rect.ContractedBy(10f);
            float titleH = Text.LineHeightOf(GameFont.Small) + 6f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, titleH), SimTranslation.T("RSMF.UniqueGoods.Listed"));

            List<UniqueGoodsSlotData> rows = GetListedSlotsCached();
            float rowH = GetRowHeight();
            float pagerH = Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 10f);
            Rect pagerRect = new Rect(inner.x, inner.yMax - pagerH, inner.width, pagerH);
            Rect outRect = new Rect(inner.x, inner.y + titleH + 8f, inner.width, pagerRect.y - inner.y - titleH - 14f);
            ClampPage(ref listedPage, rows.Count, ListedPageSize);
            int pageStart = listedPage * ListedPageSize;
            int pageCount = Mathf.Min(ListedPageSize, Mathf.Max(0, rows.Count - pageStart));
            Rect viewRect = new Rect(0f, 0f, outRect.width - ScrollbarWidth, Mathf.Max(outRect.height, pageCount * rowH));
            Widgets.BeginScrollView(outRect, ref listedScroll, viewRect);
            try
            {
                GetVisibleRowRange(listedScroll.y, outRect.height, rowH, pageCount, out int first, out int last);
                for (int i = first; i < last; i++)
                    DrawListedRow(new Rect(0f, i * rowH, viewRect.width, rowH - 3f), rows[pageStart + i], pageStart + i);
            }
            finally
            {
                Widgets.EndScrollView();
            }
            DrawPager(pagerRect, ref listedPage, rows.Count, ListedPageSize, ref listedScroll);
        }

        //绘制一个可用物品行及上架按钮。
        private void DrawAvailableRow(Rect rect, Thing thing, int index)
        {
            if ((index & 1) == 1) Widgets.DrawBoxSolid(rect, RowAlt);
            Rect icon = new Rect(rect.x + 6f, rect.y + 6f, 44f, 44f);
            Widgets.ThingIcon(icon, thing);
            float buttonSize = Mathf.Max(30f, Text.LineHeightOf(GameFont.Small) + 10f);
            Rect button = new Rect(rect.xMax - buttonSize - 6f, rect.y + (rect.height - buttonSize) * 0.5f, buttonSize, buttonSize);
            DrawTwoLineText(new Rect(icon.xMax + 9f, rect.y + 5f, button.x - icon.xMax - 15f, rect.height - 10f), thing.LabelCapNoCount, GetThingDetailsCached(thing));
            if (SimUiStyle.DrawPrimaryButton(button, "+"))
            {
                if (!container.TryQueueSource(thing, out string reason)) Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                RefreshAvailable(true);
            }
            TooltipHandler.TipRegion(rect, GetThingTooltipCached(thing));
        }

        //绘制一个已上架、等待补货或顾客暂存槽位行。
        private void DrawListedRow(Rect rect, UniqueGoodsSlotData slot, int index)
        {
            if ((index & 1) == 1) Widgets.DrawBoxSolid(rect, RowAlt);
            Thing thing = container.GetStoredThing(slot);
            Rect icon = new Rect(rect.x + 6f, rect.y + 6f, 44f, 44f);
            if (thing != null) Widgets.ThingIcon(icon, thing);
            string title = thing?.LabelCapNoCount
                ?? (slot.pendingSourceLabel.NullOrEmpty() ? SimTranslation.T("RSMF.UniqueGoods.Reserved") : slot.pendingSourceLabel);
            string status = slot.IsReservedByCustomer ? SimTranslation.T("RSMF.UniqueGoods.Status.Reserved")
                : slot.HasPendingSource ? SimTranslation.T("RSMF.UniqueGoods.Status.Pending")
                : GetThingDetailsCached(thing);

            float buttonSize = Mathf.Max(30f, Text.LineHeightOf(GameFont.Small) + 10f);
            Rect remove = new Rect(rect.xMax - buttonSize - 6f, rect.y + (rect.height - buttonSize) * 0.5f, buttonSize, buttonSize);
            float priceW = 72f;
            Rect priceRect = new Rect(remove.x - priceW - 8f, rect.y + (rect.height - buttonSize) * 0.5f, priceW, buttonSize);
            DrawTwoLineText(new Rect(icon.xMax + 9f, rect.y + 5f, priceRect.x - icon.xMax - 15f, rect.height - 10f), title, status);

            if (!slot.IsReservedByCustomer)
            {
                if (!priceBuffers.TryGetValue(slot.index, out string buffer)) buffer = slot.price.ToString("F0");
                string next = Widgets.TextField(priceRect, buffer);
                priceBuffers[slot.index] = next;
                if (float.TryParse(next, out float parsed)) container.SetSlotPrice(slot.index, parsed);
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(priceRect, "$" + slot.price.ToString("F0"));
            }

            if (SimUiStyle.DrawDangerButton(remove, "×", !slot.IsReservedByCustomer))
            {
                container.TryClearSlot(slot.index);
                priceBuffers.Remove(slot.index);
                RefreshAvailable(true);
            }
        }

        //绘制安全的双行标题与摘要。
        private static void DrawTwoLineText(Rect rect, string title, string detail)
        {
            float titleH = Text.LineHeightOf(GameFont.Small) + 2f;
            float detailH = Text.LineHeightOf(GameFont.Tiny) + 2f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, titleH), title ?? "");
            Text.Font = GameFont.Tiny;
            GUI.color = Muted;
            Widgets.Label(new Rect(rect.x, rect.y + titleH + 2f, rect.width, detailH), detail ?? "");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        //返回能容纳图标和两行文本的行高。
        private static float GetRowHeight()
        {
            return Mathf.Max(58f, Text.LineHeightOf(GameFont.Small) + Text.LineHeightOf(GameFont.Tiny) + 16f);
        }

        //返回当前排序按钮文本。
        private string SortLabel()
        {
            string[] keys = { "RSMF.UniqueGoods.Sort.Name", "RSMF.UniqueGoods.Sort.Quality", "RSMF.UniqueGoods.Sort.Value", "RSMF.UniqueGoods.Sort.HitPoints" };
            return SimTranslation.T(keys[Mathf.Clamp(sortMode, 0, keys.Length - 1)]);
        }

        //按固定实时间隔刷新地图来源缓存，职责是在暂停状态下也能发现地图物品变化。
        private void RefreshAvailableIfNeeded()
        {
            if (Time.realtimeSinceStartup - lastRefreshRealtime >= 5f) RefreshAvailable(false);
        }

        //立即或按需重建可上架来源列表。
        private void RefreshAvailable(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now - lastRefreshRealtime < 5f) return;
            available.Clear();
            available.AddRange(UniqueGoodsUtility.EnumerateAvailableSources(container.Map, container));
            thingDetailsCache.Clear();
            thingTooltipCache.Clear();
            lastRefreshRealtime = now;
            availableSourceVersion++;
            InvalidateAvailableFilter();
        }
    }
}
