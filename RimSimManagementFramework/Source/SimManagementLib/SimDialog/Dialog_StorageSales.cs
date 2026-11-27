using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //组件货柜管理窗口主入口，负责状态汇总、启售切换、模式页签和底部操作栏。
    internal sealed partial class Dialog_StorageSales : Window
    {
        private enum StockFilter { All, InStock, Restocking, Paused }
        private enum SortMode { Default, StockDesc, NameAsc, PriceDesc, TargetDesc }

        private static readonly Color CStockOk = new(0.35f, 0.80f, 0.45f, 1f);
        private static readonly Color CStockLow = new(0.95f, 0.72f, 0.25f, 1f);
        private static readonly Color CStockNo = new(0.90f, 0.35f, 0.35f, 1f);
        private static readonly Color CBadgeOn = new(0.22f, 0.56f, 0.34f, 0.85f);
        private static readonly Color CBadgeOff = new(0.35f, 0.37f, 0.40f, 0.85f);
        private static readonly Color CBadgeWarn = new(0.75f, 0.46f, 0.18f, 0.85f);
        private static readonly Color CSideSel = new(0.20f, 0.30f, 0.40f, 0.45f);
        private static readonly Color CSideHov = new(1f, 1f, 1f, 0.08f);

        private static Dictionary<ThingDef, GoodsItemData> clipboard;

        private readonly ThingComp_StorageSales sales;
        private readonly Dictionary<ThingDef, GoodsItemData> draft = new();
        private readonly HashSet<ThingDef> removedRules = new();
        private Dictionary<ThingDef, int> stock = new();
        private Dictionary<ThingDef, int> listed;
        private int listedTotal;
        private int totalStock;
        private int observedVersion = -1;
        private bool picking;
        private bool dirty;

        public override Vector2 InitialSize => new(Mathf.Min(1040f, UI.screenWidth - 48f), Mathf.Min(700f, UI.screenHeight - 48f));

        //从柜内实物和已保存规则初始化独立草稿，窗口沿用框架可调整大小与拖拽习惯。
        internal Dialog_StorageSales(ThingComp_StorageSales sales)
        {
            this.sales = sales;
            doCloseX = true;
            doCloseButton = false;
            forcePause = false;
            absorbInputAroundWindow = false;
            resizeable = true;
            draggable = true;
            closeOnAccept = false;
            RefreshStock();
        }

        //判断窗口是否对应同一宿主销售组件，避免重复弹出多个管理窗口。
        internal bool Matches(ThingComp_StorageSales other) => ReferenceEquals(sales, other);

        //重新统计宿主实物数量，保留未保存编辑和已删除规则状态。
        private void RefreshStock()
        {
            stock = sales.StockThings
                .Where(t => sales.SalesProps.AllowsListing(t.def))
                .GroupBy(t => t.def)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.stackCount));
            totalStock = stock.Values.Sum();
            if (sales.SalesProps.maxListedCount >= 0)
            {
                listed = sales.ListedStock()
                    .GroupBy(stack => stack.Thing.def)
                    .ToDictionary(group => group.Key, group => group.Sum(stack => stack.Count));
                listedTotal = listed.Values.Sum();
            }
            else
            {
                listed = null;
                listedTotal = 0;
            }
            foreach (ThingDef def in stock.Keys)
            {
                removedRules.Remove(def);
                EnsureDraft(def);
            }
            foreach (ThingDef def in sales.ConfiguredDefs)
                if (!removedRules.Contains(def)) EnsureDraft(def);
            observedVersion = sales.StoredCountVersion;
            RebuildManageRows();
            if (picking) RefreshPickerData();
        }

        //为品种建立默认或已有规则的独立草稿副本，规范化补货阈值显示。
        private GoodsItemData EnsureDraft(ThingDef def)
        {
            if (draft.TryGetValue(def, out GoodsItemData existing)) return existing;
            GoodsItemData copy = sales.Rule(def)?.Copy() ?? new GoodsItemData { enabled = true, count = 0, price = 0f, restockThreshold = -1 };
            NormalizeRuleBuffers(copy);
            draft[def] = copy;
            return copy;
        }

        //同步单条规则的输入缓冲和有效阈值，目标为 0 时保持阈值未启用。
        private static void NormalizeRuleBuffers(GoodsItemData rule)
        {
            rule.count = Math.Max(0, rule.count);
            rule.price = Math.Max(0f, rule.price);
            rule.priceBuffer = rule.price.ToString("0.##");
            rule.countBuffer = rule.count.ToString();
            if (rule.count <= 0)
            {
                rule.restockThreshold = -1;
                rule.restockThresholdBuffer = null;
            }
            else
            {
                rule.restockThreshold = GoodsItemData.NormalizeRestockThreshold(rule.restockThreshold, rule.count);
                rule.restockThresholdBuffer = rule.restockThreshold.ToString();
            }
        }

        //目标量变更后自动调整阈值，避免阈值悬空或超过新目标。
        private static void SyncThresholdAfterTargetChange(GoodsItemData rule, int previousCount)
        {
            if (rule.count <= 0)
            {
                rule.count = 0;
                rule.restockThreshold = -1;
                rule.restockThresholdBuffer = null;
                return;
            }
            if (previousCount <= 0 || rule.restockThreshold < 0 || rule.restockThreshold == previousCount)
                rule.restockThreshold = rule.count;
            else
                rule.restockThreshold = GoodsItemData.NormalizeRestockThreshold(rule.restockThreshold, rule.count);
            rule.restockThresholdBuffer = rule.restockThreshold.ToString();
        }

        //绘制框架风格的标题、摘要、提示、主工作区和底部保存栏，并完整隔离 GUI 状态。
        public override void DoWindowContents(Rect area)
        {
            if (!sales.Host.Spawned) { Close(); return; }
            if (observedVersion != sales.StoredCountVersion) RefreshStock();

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

                Zone_Shop shop = ShopDataUtility.FindShopZone(sales.Host.Map, sales.Host.Position);
                float y = area.y;
                y += DrawHeader(new Rect(area.x, y, area.width, 0f), shop);
                y += DrawSummaryAndNotice(area.x, y, area.width, shop);
                y += DrawModeBar(area.x, y, area.width);

                float controlH = ShopUiVisualUtility.ControlHeight();
                float footerH = controlH + 14f;
                Rect footerRect = new Rect(area.x, area.yMax - footerH, area.width, footerH);
                Rect bodyRect = new Rect(area.x, y, area.width, Mathf.Max(0f, footerRect.y - y - 8f));

                if (picking) DrawPickerBody(bodyRect);
                else DrawManageBody(bodyRect);

                DrawFooter(footerRect);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        //绘制窗口标题、所属商店说明及右上角销售开关，为原生关闭按钮预留安全空间。
        private float DrawHeader(Rect rect, Zone_Shop shop)
        {
            float titleH = ShopUiVisualUtility.LineHeight(GameFont.Medium);
            float subH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float headerH = titleH + subH + 4f;
            float closeReserve = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 6f;

            string toggleText = SimTranslation.T(sales.SalesEnabled ? "RSMF.StorageSales.DisableSales" : "RSMF.StorageSales.EnableSales");
            string statusText = SimTranslation.T(!sales.SalesEnabled
                ? "RSMF.StorageSales.Status.Off"
                : shop != null ? "RSMF.StorageSales.Status.Active" : "RSMF.StorageSales.Status.NoShop");
            Color badgeColor = !sales.SalesEnabled ? CBadgeOff : shop != null ? CBadgeOn : CBadgeWarn;

            Text.Font = GameFont.Small;
            float toggleW = Mathf.Max(88f, Text.CalcSize(toggleText).x + 22f);
            Text.Font = GameFont.Tiny;
            float badgeW = Mathf.Max(64f, Text.CalcSize(statusText).x + 18f);
            Text.Font = GameFont.Small;

            float controlH = ShopUiVisualUtility.ControlHeight();
            float badgeH = ShopUiVisualUtility.LineHeight(GameFont.Tiny) + 8f;
            float rightEdge = rect.xMax - closeReserve;
            Rect toggleRect = new Rect(rightEdge - toggleW, rect.y + (headerH - controlH) * 0.5f, toggleW, controlH);
            Rect badgeRect = new Rect(toggleRect.x - badgeW - 8f, rect.y + (headerH - badgeH) * 0.5f, badgeW, badgeH);

            float textW = Mathf.Max(80f, badgeRect.x - rect.x - 12f);
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x, rect.y, textW, titleH), sales.Host.LabelCap, Color.white, GameFont.Medium);
            string subtitle = shop != null
                ? SimTranslation.T("RSMF.StorageSales.SubtitleShop", shop.label.Named("shop"))
                : SimTranslation.T("RSMF.StorageSales.SubtitleNoShop");
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x, rect.y + titleH + 2f, textW, subH), subtitle,
                shop != null ? ShopUiVisualUtility.MutedText : ShopUiVisualUtility.WarningText);

            ShopUiVisualUtility.DrawStatusBadge(badgeRect, statusText, badgeColor);
            bool toggled = sales.SalesEnabled
                ? ShopUiVisualUtility.DrawSecondaryButton(toggleRect, toggleText)
                : ShopUiVisualUtility.DrawPrimaryButton(toggleRect, toggleText);
            TooltipHandler.TipRegion(toggleRect, SimTranslation.T("RSMF.StorageSales.ToggleDesc"));
            if (toggled)
            {
                sales.ToggleSales();
                RefreshStock();
            }

            return headerH + 8f;
        }

        //绘制四项紧凑指标与状态提示条，优先提醒未启售或未划入商店区域的问题。
        private float DrawSummaryAndNotice(float x, float y, float width, Zone_Shop shop)
        {
            int onSaleCount = 0;
            int totalTarget = 0;
            foreach (var entry in draft)
            {
                if (!entry.Value.enabled) continue;
                onSaleCount++;
                totalTarget += entry.Value.count;
            }
            MapComponent_RestockTaskQueue queue = sales.Host.Map?.GetComponent<MapComponent_RestockTaskQueue>();
            int totalPending = queue?.CountTotalPending(sales) ?? 0;

            string stockValue = listed != null
                ? SimTranslation.T("RSMF.StorageSales.Metric.StockWithCap",
                    listedTotal.Named("listed"), sales.SalesProps.maxListedCount.Named("capacity"), totalStock.Named("stock"))
                : totalStock.ToString();

            string[] labels =
            {
                SimTranslation.T("RSMF.StorageSales.Metric.ListedDefs"),
                SimTranslation.T("RSMF.StorageSales.Metric.Stock"),
                SimTranslation.T("RSMF.StorageSales.Metric.Target"),
                SimTranslation.T("RSMF.StorageSales.Metric.Pending")
            };
            string[] values =
            {
                $"{onSaleCount} / {draft.Count}",
                stockValue,
                totalTarget.ToString(),
                totalPending.ToString()
            };

            float metricsH = ShopUiVisualUtility.DrawMetrics(new Rect(x, y, width, 0f), labels, values, compact: true);
            float noticeY = y + metricsH + 8f;

            bool warn = !sales.SalesEnabled || shop == null;
            string notice = !sales.SalesEnabled
                ? SimTranslation.T("RSMF.StorageSales.WarningOff")
                : shop == null
                    ? SimTranslation.T("RSMF.StorageSales.WarningNoShop")
                    : SimTranslation.T("RSMF.StorageSales.HintFriendly");
            float noticeH = ShopUiVisualUtility.NoticeHeight(notice, width);
            ShopUiVisualUtility.DrawNotice(new Rect(x, noticeY, width, noticeH), notice, warn);

            return metricsH + 8f + noticeH + 8f;
        }

        //绘制清单与添加品种模式切换栏，并在清单模式下提供一键状态筛选。
        private float DrawModeBar(float x, float y, float width)
        {
            float h = ShopUiVisualUtility.ControlHeight();
            string manageLabel = SimTranslation.T("RSMF.StorageSales.Tab.Manage", draft.Count.Named("count"));
            string pickerLabel = SimTranslation.T("RSMF.StorageSales.Tab.Picker");
            float manageW = Mathf.Max(120f, Text.CalcSize(manageLabel).x + 26f);
            float pickerW = Mathf.Max(104f, Text.CalcSize(pickerLabel).x + 26f);

            if (ShopUiVisualUtility.DrawTabButton(new Rect(x, y, manageW, h), manageLabel, !picking, ShopUiVisualUtility.MutedText))
                SwitchMode(false);
            if (ShopUiVisualUtility.DrawTabButton(new Rect(x + manageW + 6f, y, pickerW, h), pickerLabel, picking, ShopUiVisualUtility.MutedText))
                SwitchMode(true);

            if (!picking)
            {
                float rightStart = x + manageW + pickerW + 18f;
                float avail = x + width - rightStart;
                DrawStockFilterChips(new Rect(rightStart, y, avail, h));
            }
            return h + 8f;
        }

        //在货柜清单与添加品种视图间切换，保留各自的搜索词与已编辑草稿。
        private void SwitchMode(bool toPicker)
        {
            if (picking == toPicker) return;
            picking = toPicker;
            GUI.FocusControl(null);
            if (picking) RefreshPickerData();
            else RebuildManageRows();
        }

        //绘制底部复制、粘贴、立即补货及取消/保存按钮。
        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), ShopUiVisualUtility.Divider);
            float h = ShopUiVisualUtility.ControlHeight();
            float by = rect.yMax - h;

            string copyLabel = SimTranslation.T("RSMF.GoodsManager.CopyConfig");
            string pasteLabel = SimTranslation.T("RSMF.GoodsManager.PasteConfig");
            string restockLabel = SimTranslation.T("RSMF.StorageSales.RestockNow");
            string cancelLabel = SimTranslation.T("RSMF.Common.Cancel");
            string saveLabel = SimTranslation.T("RSMF.StorageSales.Save");

            float copyW = Mathf.Max(88f, Text.CalcSize(copyLabel).x + 22f);
            float pasteW = Mathf.Max(88f, Text.CalcSize(pasteLabel).x + 22f);
            float restockW = Mathf.Max(88f, Text.CalcSize(restockLabel).x + 22f);
            float cancelW = Mathf.Max(88f, Text.CalcSize(cancelLabel).x + 24f);
            float saveW = Mathf.Max(110f, Text.CalcSize(saveLabel).x + 26f);

            float lx = rect.x;
            if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(lx, by, copyW, h), copyLabel, draft.Count > 0))
                CopyDraftToClipboard();
            lx += copyW + 6f;

            if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(lx, by, pasteW, h), pasteLabel, clipboard != null && clipboard.Count > 0))
                PasteClipboardToDraft();
            lx += pasteW + 6f;

            bool canRestock = sales.AllowsCustomerSelfPurchase && sales.RestockDefs.Any();
            Rect restockRect = new Rect(lx, by, restockW, h);
            if (ShopUiVisualUtility.DrawSecondaryButton(restockRect, restockLabel, canRestock))
                TriggerSavedRestock();
            TooltipHandler.TipRegion(restockRect, SimTranslation.T("RSMF.StorageSales.RestockNowTip"));
            lx += restockW + 10f;

            Rect saveRect = new Rect(rect.xMax - saveW, by, saveW, h);
            Rect cancelRect = new Rect(saveRect.x - cancelW - 8f, by, cancelW, h);

            if (dirty && cancelRect.x - lx > 90f)
                ShopUiVisualUtility.DrawCellLabel(new Rect(lx, by, cancelRect.x - lx - 8f, h),
                    SimTranslation.T("RSMF.StorageSales.UnsavedHint"), ShopUiVisualUtility.WarningText, GameFont.Tiny);

            if (ShopUiVisualUtility.DrawSecondaryButton(cancelRect, cancelLabel)) Close();
            if (ShopUiVisualUtility.DrawPrimaryButton(saveRect, saveLabel)) SaveAndClose();
        }
    }
}
