using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责绘制和保存单个货柜的商品分类、目标库存、价格与库存状态。
    /// </summary>
    public class Dialog_GoodsManager : Window
    {
        // 尺寸与布局常量。
        private const float SidebarW = 160f;
        private const float DivW = 2f;
        private const float RowH = 46f;
        private const float IconSz = 28f;
        private const float CheckSz = 24f;
        private const float FieldW = 65f;
        private const float ThresholdFieldW = 76f;
        private const float StockW = 60f;
        private const float SliderW = 150f;
        private const float ColGap = 10f;
        private const float RowPad = 8f;
        private const float HeaderH = 30f;
        private const float BottomH = 74f;
        private const float SearchBarH = 30f;
        private const float ScrW = 16f;
        private const int PendingEvictPreviewLimit = 6;

        // 配色方案。
        private static readonly Color CAccent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color CSideBg = new Color(0.12f, 0.13f, 0.14f, 1f);
        private static readonly Color CSideSel = new Color(0.2f, 0.3f, 0.4f, 0.4f);
        private static readonly Color CSideHov = new Color(0.3f, 0.3f, 0.3f, 0.2f);
        private static readonly Color CDivider = new Color(0.3f, 0.3f, 0.3f, 0.4f);
        private static readonly Color CStockOk = new Color(0.4f, 0.8f, 0.4f, 1f);
        private static readonly Color CStockLow = new Color(0.9f, 0.7f, 0.3f, 1f);
        private static readonly Color CStockNo = new Color(0.9f, 0.4f, 0.4f, 1f);
        private static readonly Color CCheckedBg = new Color(0.25f, 0.65f, 0.85f, 0.1f);

        private readonly ThingComp_GoodsData comp;
        private readonly Building_SimContainer storage;
        private static GoodsClipboardData clipboardData;
        private List<Pojo.RuntimeGoodsCategory> allDefs;
        private Vector2 sideScroll;
        private Vector2 listScroll;

        private string draftActiveDefName;
        private Dictionary<string, GoodsItemData> draftItemData;

        private string searchQuery = "";
        private readonly List<ThingDef> filteredItemsCache = new List<ThingDef>();
        private string filteredCategoryCacheId = "";
        private string filteredSearchCache = "";
        private int filteredSourceCount = -1;
        private readonly Dictionary<ThingDef, int> storedCountCache = new Dictionary<ThingDef, int>();
        private int storedCountCacheVersion;
        private int observedStorageCountVersion = -1;
        private bool storedCountCacheDirty = true;
        private readonly List<ThingDef> pendingEvictCache = new List<ThingDef>();
        private bool pendingEvictDirty = true;
        private string pendingEvictCategoryCacheId = "";
        private int pendingEvictStockVersion = -1;
        private int pendingEvictResultVersion;
        private int pendingEvictPreviewVersion = -1;
        private int pendingEvictPreviewCount = -1;
        private string pendingEvictPreviewNames = "";
        private bool draftTargetTotalDirty = true;
        private string draftTargetCategoryCacheId = "";
        private int cachedDraftTargetTotal;

        public override Vector2 InitialSize => new Vector2(1080f, 680f);

        public Dialog_GoodsManager(ThingComp_GoodsData comp)
        {
            this.comp = comp;
            this.storage = comp.parent as Building_SimContainer;
            doCloseButton = false;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            resizeable = false;
            draggable = false;

            GoodsCatalog.EnsureInitialized();
            allDefs = (GoodsCatalog.Categories ?? Enumerable.Empty<Pojo.RuntimeGoodsCategory>())
                .Where(d => d != null && comp.AllowsGoodsCategory(d.categoryId))
                .Where(d => d != null && d.Items != null && d.Items.Count > 0)
                .OrderBy(d => d.label)
                .ToList();

            draftActiveDefName = comp.ActiveGoodsDefName;
            if (!comp.AllowsGoodsCategory(draftActiveDefName))
                draftActiveDefName = "";
            draftItemData = comp.CloneItemData();
        }

        /// <summary>
        /// 获取指定商品的草稿配置，负责在玩家真正编辑该商品时创建缺失记录。
        /// </summary>
        private GoodsItemData GetDraftItem(ThingDef td)
        {
            if (!draftItemData.TryGetValue(td.defName, out var d))
                draftItemData[td.defName] = d = new GoodsItemData();
            return d;
        }

        /// <summary>
        /// 尝试读取指定商品的草稿配置，负责避免单纯绘制列表时扩张配置字典。
        /// </summary>
        private bool TryGetDraftItem(ThingDef td, out GoodsItemData data)
        {
            data = null;
            return td != null && draftItemData != null && draftItemData.TryGetValue(td.defName, out data) && data != null;
        }

        /// <summary>
        /// 绘制窗口主体内容，负责组合警告条、分类侧栏、商品列表和底部操作区。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            RefreshStoredCountCacheIfNeeded();
            var pendingEvict = GetPendingEvict();
            float bannerH = pendingEvict.Count > 0 ? 28f : 0f;

            Rect topBannerR = new Rect(inRect.x, inRect.y, inRect.width, bannerH);
            Rect contentR = new Rect(inRect.x, inRect.y + bannerH, inRect.width, inRect.height - BottomH - bannerH);

            Rect sideR = new Rect(contentR.x, contentR.y, SidebarW, contentR.height);
            Rect divR = new Rect(sideR.xMax, contentR.y, DivW, contentR.height);
            Rect mainR = new Rect(divR.xMax + 4f, contentR.y, contentR.width - SidebarW - DivW - 4f, contentR.height);
            Rect botR = new Rect(inRect.x, inRect.yMax - BottomH, inRect.width, BottomH);

            if (bannerH > 0) DrawEvictBanner(topBannerR, pendingEvict);

            DrawSidebar(sideR);
            Widgets.DrawBoxSolid(divR, CDivider);
            DrawMainPanel(mainR);
            DrawBottomBar(botR);
        }

        /// <summary>
        /// 绘制左侧分类列表，负责在玩家切换分类时重置当前列表滚动和搜索。
        /// </summary>
        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, CSideBg);

            Rect titleRect = new Rect(rect.x, rect.y, rect.width, 36f);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Color.white;
            Widgets.Label(titleRect, comp.HasGoodsCategoryRestriction ? SimTranslation.T("RSMF.GoodsManager.AvailableCategories") : SimTranslation.T("RSMF.GoodsManager.CategoryNav"));
            if (comp.HasGoodsCategoryRestriction)
                TooltipHandler.TipRegion(titleRect, SimTranslation.T("RSMF.GoodsManager.RestrictionTip", comp.GetAllowedGoodsCategoryLabelSummary().Named("labels")));
            Widgets.DrawLineHorizontal(rect.x + 10f, titleRect.yMax, rect.width - 20f);

            Rect outR = new Rect(rect.x, titleRect.yMax + 4f, rect.width, rect.height - 40f);
            float itemH = 36f;
            Rect viewR = new Rect(0f, 0f, rect.width - ScrW, (allDefs.Count + 1) * itemH);

            Widgets.BeginScrollView(outR, ref sideScroll, viewR);
            for (int i = 0; i < allDefs.Count; i++)
            {
                Pojo.RuntimeGoodsCategory def = allDefs[i];
                Rect ir = new Rect(0f, i * itemH, viewR.width, itemH);
                bool isCur = draftActiveDefName == def.categoryId;

                if (isCur) Widgets.DrawBoxSolid(ir, CSideSel);
                else if (Mouse.IsOver(ir)) Widgets.DrawBoxSolid(ir, CSideHov);

                if (isCur) Widgets.DrawBoxSolid(new Rect(ir.x, ir.y + 4f, 4f, ir.height - 8f), CAccent);

                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = isCur ? GameFont.Small : GameFont.Tiny;
                GUI.color = isCur ? Color.white : new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(new Rect(ir.x + 12f, ir.y, ir.width - 12f, ir.height), def.label);

                if (!isCur && Widgets.ButtonInvisible(ir)) TrySwitchDef(def.categoryId);
            }

            Rect clearR = new Rect(0f, allDefs.Count * itemH, viewR.width, itemH);
            if (Mouse.IsOver(clearR) && !string.IsNullOrEmpty(draftActiveDefName))
                Widgets.DrawBoxSolid(clearR, CSideHov);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(clearR, SimTranslation.T("RSMF.GoodsManager.ClearSelection"));
            if (Widgets.ButtonInvisible(clearR) && !string.IsNullOrEmpty(draftActiveDefName))
                TrySwitchDef("");

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制当前分类的商品列表，负责搜索、容量摘要和可视范围内的行绘制。
        /// </summary>
        private void DrawMainPanel(Rect rect)
        {
            Pojo.RuntimeGoodsCategory def = GoodsCatalog.GetCategory(draftActiveDefName);
            if (def == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.5f, 0.5f, 0.5f);
                string text = allDefs.Count > 0 ? SimTranslation.T("RSMF.GoodsManager.SelectCategoryFirst") : SimTranslation.T("RSMF.GoodsManager.NoCategories");
                Widgets.Label(rect, text);
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                return;
            }

            Rect searchR = new Rect(rect.x, rect.y, rect.width, SearchBarH);
            searchQuery = Widgets.TextField(new Rect(searchR.x, searchR.y, 200f, 24f), searchQuery);
            if (string.IsNullOrEmpty(searchQuery))
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Widgets.Label(new Rect(searchR.x + 6f, searchR.y + 2f, 190f, 24f), SimTranslation.T("RSMF.GoodsManager.SearchPlaceholder"));
                GUI.color = Color.white;
            }

            List<ThingDef> filteredList = GetFilteredItems(def);

            if (storage != null)
            {
                int targetTotal = GetDraftTargetTotal(def);
                string capText = SimTranslation.T("RSMF.GoodsManager.TargetTotal",
                    targetTotal.Named("current"),
                    storage.MaxTotalCapacity.Named("max"));
                Text.Anchor = TextAnchor.MiddleRight;
                Text.Font = GameFont.Tiny;
                GUI.color = targetTotal <= storage.MaxTotalCapacity ? CStockOk : CStockNo;
                Widgets.Label(new Rect(searchR.x + 220f, searchR.y, searchR.width - 220f, 24f), capText);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            Rect hdrR = new Rect(rect.x, searchR.yMax, rect.width, HeaderH);
            DrawHeader(hdrR);

            Rect outR = new Rect(rect.x, hdrR.yMax, rect.width, rect.height - SearchBarH - HeaderH);
            float viewW = outR.width - ScrW;
            Rect viewR = new Rect(0f, 0f, viewW, filteredList.Count * RowH);

            Widgets.BeginScrollView(outR, ref listScroll, viewR);
            ClampListScroll(viewR.height, outR.height);
            int firstIndex = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / RowH) - 1);
            int lastIndex = Mathf.Min(filteredList.Count - 1, Mathf.CeilToInt((listScroll.y + outR.height) / RowH) + 1);
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                DrawItemRow(new Rect(0f, i * RowH, viewW, RowH), filteredList[i], i % 2 == 0, def);
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制商品列表表头，负责固定各列标题的对齐方式。
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = new Color(0.8f, 0.8f, 0.8f);

            float curX = rect.xMax - RowPad;

            curX -= FieldW; Widgets.Label(new Rect(curX, rect.y, FieldW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Price")); curX -= ColGap;
            curX -= ThresholdFieldW; Widgets.Label(new Rect(curX, rect.y, ThresholdFieldW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.RestockThreshold")); curX -= ColGap;
            curX -= FieldW; Widgets.Label(new Rect(curX, rect.y, FieldW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Target")); curX -= ColGap;
            curX -= SliderW; Widgets.Label(new Rect(curX, rect.y, SliderW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.TargetSlider")); curX -= ColGap;
            curX -= StockW; Widgets.Label(new Rect(curX, rect.y, StockW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Stock")); curX -= ColGap;

            float leftStart = rect.x + RowPad + CheckSz + ColGap + IconSz + ColGap;
            Widgets.Label(new Rect(leftStart, rect.y, curX - leftStart, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Name"));

            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制单个商品行，负责展示勾选状态、库存、目标数量、价格和名称。
        /// </summary>
        private void DrawItemRow(Rect row, ThingDef td, bool alt, Pojo.RuntimeGoodsCategory activeDef)
        {
            TryGetDraftItem(td, out GoodsItemData d);
            bool nowEnabled = d != null && d.enabled;

            if (nowEnabled) Widgets.DrawBoxSolid(row, CCheckedBg);
            else if (alt) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.02f));

            Widgets.DrawHighlightIfMouseover(row);
            string label = td.LabelCap;
            if (Mouse.IsOver(row) && !string.IsNullOrEmpty(td.description))
                TooltipHandler.TipRegion(row, label + "\n\n" + td.description);

            if (!nowEnabled) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);

            float midY = row.y + (RowH - IconSz) / 2f;
            float ctrlY = row.y + (RowH - 24f) / 2f;

            // 勾选框：只有玩家改变启用状态时才创建配置记录。
            float x = row.x + RowPad;
            bool previousEnabled = nowEnabled;
            Widgets.Checkbox(x, row.y + (RowH - CheckSz) / 2f, ref nowEnabled, CheckSz, paintable: true);
            if (nowEnabled != previousEnabled)
            {
                d = GetDraftItem(td);
                d.enabled = nowEnabled;
                if (d.enabled && d.count <= 0)
                {
                    d.count = 1;
                    d.countBuffer = "1";
                    d.restockThreshold = GoodsItemData.NormalizeRestockThreshold(d.restockThreshold, d.count);
                    d.restockThresholdBuffer = d.restockThreshold.ToString();
                }
                // 首次勾选商品时补齐默认价格，避免保存出 0 价商品。
                if (d.enabled && d.price <= 0f)
                {
                    d.price = td.BaseMarketValue > 0f ? td.BaseMarketValue : 1f;
                    d.priceBuffer = d.price.ToString("F0");
                }
                MarkDraftInventoryViewDirty();
            }
            x += CheckSz + ColGap;

            // 图标。
            Widgets.ThingIcon(new Rect(x, midY, IconSz, IconSz), td);
            x += IconSz + ColGap;

            float rightX = row.xMax - RowPad;

            // 价格输入框。
            rightX -= FieldW;
            if (nowEnabled)
            {
                d = d ?? GetDraftItem(td);
                // 初始化价格输入缓存时补齐默认价格，保证输入框和数据一致。
                if (d.priceBuffer == null)
                {
                    if (d.price <= 0f)
                        d.price = td.BaseMarketValue > 0f ? td.BaseMarketValue : 1f;
                    d.priceBuffer = d.price.ToString("F0");
                }
                Widgets.TextFieldNumeric(new Rect(rightX, ctrlY, FieldW, 24f), ref d.price, ref d.priceBuffer, 0f, 99999f);
                TooltipHandler.TipRegion(new Rect(rightX, ctrlY, FieldW, 24f), SimTranslation.T("RSMF.GoodsManager.PriceTip", td.BaseMarketValue.ToString("F1").Named("value")));
            }
            else { DrawDisabledDash(new Rect(rightX, row.y, FieldW, RowH)); }
            rightX -= ColGap;

            // 补货阈值输入框。
            rightX -= ThresholdFieldW;
            if (nowEnabled)
            {
                d = d ?? GetDraftItem(td);
                EnsureRestockThresholdBuffer(d);
                int prevThreshold = d.restockThreshold;
                Widgets.TextFieldNumeric(new Rect(rightX, ctrlY, ThresholdFieldW, 24f), ref d.restockThreshold, ref d.restockThresholdBuffer, 0, Mathf.Max(0, d.count));
                d.restockThreshold = GoodsItemData.NormalizeRestockThreshold(d.restockThreshold, d.count);
                d.restockThresholdBuffer = d.restockThreshold.ToString();
                if (d.restockThreshold != prevThreshold)
                    MarkDraftInventoryViewDirty();
                TooltipHandler.TipRegion(new Rect(rightX, ctrlY, ThresholdFieldW, 24f), SimTranslation.T("RSMF.GoodsManager.RestockThresholdTip"));
            }
            else { DrawDisabledDash(new Rect(rightX, row.y, ThresholdFieldW, RowH)); }
            rightX -= ColGap;

            // 数量输入框。
            rightX -= FieldW;
            if (nowEnabled)
            {
                d = d ?? GetDraftItem(td);
                if (d.countBuffer == null) d.countBuffer = d.count.ToString();
                int prevCount = d.count;
                Widgets.TextFieldNumeric(new Rect(rightX, ctrlY, FieldW, 24f), ref d.count, ref d.countBuffer, 0, GetSliderMaxCount());
                if (d.count != prevCount)
                {
                    d.restockThreshold = GoodsItemData.NormalizeRestockThreshold(d.restockThreshold, d.count);
                    d.restockThresholdBuffer = d.restockThreshold.ToString();
                    d.countBuffer = d.count.ToString();
                    MarkDraftInventoryViewDirty();
                }
            }
            else { DrawDisabledDash(new Rect(rightX, row.y, FieldW, RowH)); }
            rightX -= ColGap;

            // 数量滑条。
            rightX -= SliderW;
            if (nowEnabled)
            {
                d = d ?? GetDraftItem(td);
                int sliderMax = GetSliderMaxCount();
                int newCount = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(rightX, row.y + (RowH - 22f) / 2f, SliderW, 22f), d.count, 0f, sliderMax, true, null, "0", sliderMax.ToString(), 1f));
                if (newCount != d.count)
                {
                    d.count = newCount;
                    d.restockThreshold = GoodsItemData.NormalizeRestockThreshold(d.restockThreshold, d.count);
                    d.restockThresholdBuffer = d.restockThreshold.ToString();
                    d.countBuffer = newCount.ToString();
                    MarkDraftInventoryViewDirty();
                }
            }
            else { DrawDisabledDash(new Rect(rightX, row.y, SliderW, RowH)); }
            rightX -= ColGap;

            // 库存数量。
            rightX -= StockW;
            int stk = GetStored(td);
            int targetCount = nowEnabled && d != null ? d.count : 0;
            int restockThreshold = nowEnabled && d != null ? d.EffectiveRestockThreshold : 0;
            Color stockColor = stk == 0 ? CStockNo : (nowEnabled && stk <= restockThreshold && stk < targetCount) ? CStockLow : CStockOk;
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Small;
            GUI.color = nowEnabled ? stockColor : new Color(0.5f, 0.5f, 0.5f);
            Widgets.Label(new Rect(rightX, row.y, StockW, RowH), stk.ToString());
            rightX -= ColGap;

            // 物品名称。
            float nameW = rightX - x;
            Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Small;
            GUI.color = nowEnabled ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(x, row.y, nameW, RowH), label.Truncate(nameW));

            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        //返回目标量滑条上限，负责让滑条匹配货柜容量而不是固定在小范围内。
        private int GetSliderMaxCount()
        {
            return Mathf.Max(1, storage?.MaxTotalCapacity ?? 999);
        }

        //确保补货阈值输入缓存存在，负责让 UI 显示和保存值保持一致。
        private static void EnsureRestockThresholdBuffer(GoodsItemData data)
        {
            if (data == null) return;
            data.restockThreshold = GoodsItemData.NormalizeRestockThreshold(data.restockThreshold, data.count);
            if (data.restockThresholdBuffer == null)
                data.restockThresholdBuffer = data.restockThreshold.ToString();
        }

        /// <summary>
        /// 绘制禁用列的占位横线，负责让未启用商品保持列宽一致。
        /// </summary>
        private void DrawDisabledDash(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.3f, 0.3f, 0.3f);
            Widgets.Label(rect, "-");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// 绘制待清退提示条，负责提醒玩家当前库存会因配置降低而被移出。
        /// </summary>
        private void DrawEvictBanner(Rect rect, List<ThingDef> pendingEvict)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.8f, 0.4f, 0f, 0.2f));
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 0.8f, 0.4f);
            string names = GetPendingEvictPreviewNames(pendingEvict);
            Widgets.Label(rect, SimTranslation.T("RSMF.GoodsManager.PendingEvictWarning",
                pendingEvict.Count.Named("count"),
                names.Truncate(rect.width - 100f).Named("names")));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// 获取待清退物品的短预览文本，负责避免货柜内容很多时每帧拼接完整名称列表。
        /// </summary>
        private string GetPendingEvictPreviewNames(List<ThingDef> pendingEvict)
        {
            int count = pendingEvict?.Count ?? 0;
            if (pendingEvictPreviewVersion == pendingEvictResultVersion && pendingEvictPreviewCount == count)
                return pendingEvictPreviewNames;

            StringBuilder builder = new StringBuilder();
            int previewCount = Mathf.Min(PendingEvictPreviewLimit, count);
            for (int i = 0; i < previewCount; i++)
            {
                ThingDef thingDef = pendingEvict[i];
                if (thingDef == null) continue;

                if (builder.Length > 0)
                    builder.Append(", ");
                builder.Append(thingDef.LabelCap.ToString());
            }

            int hiddenCount = count - previewCount;
            if (hiddenCount > 0)
            {
                if (builder.Length > 0)
                    builder.Append(", ");
                builder.Append(SimTranslation.T("RSMF.GoodsManager.PendingEvictMore", hiddenCount.Named("count")));
            }

            pendingEvictPreviewVersion = pendingEvictResultVersion;
            pendingEvictPreviewCount = count;
            pendingEvictPreviewNames = builder.ToString();
            return pendingEvictPreviewNames;
        }

        /// <summary>
        /// 绘制底部操作栏，负责展示容量状态和保存、取消、复制、粘贴等操作。
        /// </summary>
        private void DrawBottomBar(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);

            float statusY = rect.y + 6f;
            float btnY = rect.yMax - 36f;
            Rect capacityRect = new Rect(rect.x + 166f, statusY, rect.width - 508f, 22f);

            Pojo.RuntimeGoodsCategory activeDef = GoodsCatalog.GetCategory(draftActiveDefName);
            DrawStockLegend(new Rect(rect.x + 10f, statusY, 150f, 22f));
            if (storage != null && activeDef != null)
            {
                int total = GetDraftTargetTotal(activeDef);
                string text = SimTranslation.T("RSMF.GoodsManager.CapacityText",
                    total.Named("current"),
                    storage.MaxTotalCapacity.Named("max"));
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = total <= storage.MaxTotalCapacity ? CStockOk : CStockNo;
                Widgets.Label(capacityRect, text);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(draftActiveDefName))
            {
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x + 10f, btnY, 100f, 30f), SimTranslation.T("RSMF.GoodsManager.SelectAllPage"), true, GameFont.Tiny))
                    ToggleAllCurrentDef(true);
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x + 118f, btnY, 100f, 30f), SimTranslation.T("RSMF.GoodsManager.ClearAllPage"), true, GameFont.Tiny))
                    ToggleAllCurrentDef(false);
            }

            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 440f, btnY, 100f, 30f), SimTranslation.T("RSMF.GoodsManager.CopyConfig"), true, GameFont.Tiny))
                CopyDraftConfig();

            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 330f, btnY, 100f, 30f), SimTranslation.T("RSMF.GoodsManager.PasteConfig"), clipboardData != null, GameFont.Tiny))
                PasteDraftConfig();

            if (SimUiStyle.DrawPrimaryButton(new Rect(rect.xMax - 110f, btnY, 100f, 30f), SimTranslation.T("RSMF.GoodsManager.Save"), true, GameFont.Tiny))
            {
                int trimmed = ClampDraftCapacity(activeDef);
                if (trimmed > 0)
                {
                    Messages.Message(SimTranslation.T("RSMF.GoodsManager.AutoTrimNotice", trimmed.Named("trimmed")), MessageTypeDefOf.NeutralEvent, false);
                }
                comp.ApplySettings(draftActiveDefName, draftItemData);
                Close();
            }
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 220f, btnY, 100f, 30f), SimTranslation.T("RSMF.GoodsManager.Cancel"), true, GameFont.Tiny))
                Close();
        }

        /// <summary>
        /// 复制当前货柜草稿配置，负责把分类、启用状态、目标数量和价格暂存到内存剪贴板。
        /// </summary>
        private void CopyDraftConfig()
        {
            clipboardData = new GoodsClipboardData
            {
                activeCategoryId = draftActiveDefName ?? "",
                itemData = CloneGoodsItemData(draftItemData)
            };
            Messages.Message(SimTranslation.T("RSMF.GoodsManager.CopyConfigDone"), MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>
        /// 粘贴已复制的货柜配置，负责按当前货柜分类限制应用并刷新输入缓存。
        /// </summary>
        private void PasteDraftConfig()
        {
            if (clipboardData == null)
                return;

            if (!string.IsNullOrEmpty(clipboardData.activeCategoryId) && !comp.AllowsGoodsCategory(clipboardData.activeCategoryId))
            {
                Messages.Message(SimTranslation.T("RSMF.GoodsManager.PasteConfigInvalidCategory"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            draftActiveDefName = clipboardData.activeCategoryId ?? "";
            draftItemData = CloneGoodsItemData(clipboardData.itemData);
            ResetDraftInputBuffers();
            searchQuery = "";
            listScroll = Vector2.zero;

            Pojo.RuntimeGoodsCategory activeDef = GoodsCatalog.GetCategory(draftActiveDefName);
            int trimmed = ClampDraftCapacity(activeDef);
            if (trimmed > 0)
            {
                Messages.Message(SimTranslation.T("RSMF.GoodsManager.AutoTrimNotice", trimmed.Named("trimmed")), MessageTypeDefOf.NeutralEvent, false);
            }

            Messages.Message(SimTranslation.T("RSMF.GoodsManager.PasteConfigDone"), MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>
        /// 清理货柜草稿输入缓存，负责让粘贴后的数量和价格输入框重新按新配置显示。
        /// </summary>
        private void ResetDraftInputBuffers()
        {
            if (draftItemData == null) return;
            foreach (GoodsItemData item in draftItemData.Values)
            {
                if (item == null) continue;
                item.countBuffer = null;
                item.priceBuffer = null;
                item.restockThresholdBuffer = null;
            }
        }

        /// <summary>
        /// 克隆货柜商品配置字典，负责避免复制粘贴时多个货柜共享同一份配置对象。
        /// </summary>
        private static Dictionary<string, GoodsItemData> CloneGoodsItemData(Dictionary<string, GoodsItemData> source)
        {
            Dictionary<string, GoodsItemData> result = new Dictionary<string, GoodsItemData>();
            if (source == null) return result;

            foreach (KeyValuePair<string, GoodsItemData> kvp in source)
            {
                GoodsItemData item = kvp.Value;
                result[kvp.Key] = new GoodsItemData
                {
                    enabled = item?.enabled ?? false,
                    count = Mathf.Max(0, item?.count ?? 0),
                    price = Mathf.Max(0f, item?.price ?? 0f),
                    restockThreshold = GoodsItemData.NormalizeRestockThreshold(item?.restockThreshold ?? -1, Mathf.Max(0, item?.count ?? 0))
                };
            }

            return result;
        }

        /// <summary>
        /// 保存货柜配置剪贴板内容，负责在不同货柜管理窗口之间传递配置草稿。
        /// </summary>
        private class GoodsClipboardData
        {
            public string activeCategoryId = "";
            public Dictionary<string, GoodsItemData> itemData = new Dictionary<string, GoodsItemData>();
        }

        /// <summary>
        /// 绘制库存颜色图例，固定占用底栏左上角空间，避免和操作按钮重叠。
        /// </summary>
        private void DrawStockLegend(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float lx = rect.x;
            GUI.color = CStockOk;
            Widgets.Label(new Rect(lx, rect.y, 48f, rect.height), SimTranslation.T("RSMF.GoodsManager.StockOk"));
            lx += 50f;
            GUI.color = CStockLow;
            Widgets.Label(new Rect(lx, rect.y, 48f, rect.height), SimTranslation.T("RSMF.GoodsManager.StockLow"));
            lx += 50f;
            GUI.color = CStockNo;
            Widgets.Label(new Rect(lx, rect.y, 48f, rect.height), SimTranslation.T("RSMF.GoodsManager.StockNo"));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// 批量启用或禁用当前分类商品，负责在玩家点击全选或清空时更新草稿配置。
        /// </summary>
        private void ToggleAllCurrentDef(bool enable)
        {
            Pojo.RuntimeGoodsCategory def = GoodsCatalog.GetCategory(draftActiveDefName);
            if (def == null) return;
            for (int i = 0; i < def.Items.Count; i++)
            {
                ThingDef td = def.Items[i]?.thingDef;
                if (td == null) continue;
                var d = GetDraftItem(td);
                d.enabled = enable;
                if (enable && d.count <= 0) { d.count = 1; d.countBuffer = "1"; }
                if (enable)
                {
                    d.restockThreshold = GoodsItemData.NormalizeRestockThreshold(d.restockThreshold, d.count);
                    d.restockThresholdBuffer = d.restockThreshold.ToString();
                }
                // 全选时同步补齐未设置价格的商品，避免后续保存出 0 价。
                if (enable && d.price <= 0f)
                {
                    d.price = td.BaseMarketValue > 0f ? td.BaseMarketValue : 1f;
                    d.priceBuffer = d.price.ToString("F0");
                }
            }
            MarkDraftInventoryViewDirty();

            int trimmed = ClampDraftCapacity(def);
            if (trimmed > 0)
            {
                Messages.Message(SimTranslation.T("RSMF.GoodsManager.AutoTrimNotice", trimmed.Named("trimmed")), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>
        /// 获取当前分类的目标库存总量，负责缓存容量摘要避免每帧重复遍历整类商品。
        /// </summary>
        private int GetDraftTargetTotal(Pojo.RuntimeGoodsCategory def)
        {
            if (def == null) return 0;
            string categoryId = def.categoryId ?? "";
            if (!draftTargetTotalDirty && draftTargetCategoryCacheId == categoryId)
                return cachedDraftTargetTotal;

            int total = 0;
            for (int i = 0; i < def.Items.Count; i++)
            {
                ThingDef td = def.Items[i]?.thingDef;
                if (td == null) continue;
                if (!draftItemData.TryGetValue(td.defName, out GoodsItemData d) || d == null) continue;
                if (!d.enabled || d.count <= 0) continue;
                total += d.count;
            }

            cachedDraftTargetTotal = total;
            draftTargetCategoryCacheId = categoryId;
            draftTargetTotalDirty = false;
            return cachedDraftTargetTotal;
        }

        /// <summary>
        /// 将草稿目标数量限制在货柜容量内，负责保存或批量操作前裁剪超额目标。
        /// </summary>
        private int ClampDraftCapacity(Pojo.RuntimeGoodsCategory def)
        {
            if (storage == null || def == null) return 0;

            int used = 0;
            int trimmed = 0;
            int max = storage.MaxTotalCapacity;

            for (int i = 0; i < def.Items.Count; i++)
            {
                ThingDef td = def.Items[i]?.thingDef;
                if (td == null) continue;
                GoodsItemData d = GetDraftItem(td);

                if (!d.enabled || d.count <= 0)
                {
                    d.enabled = false;
                    d.count = 0;
                    d.countBuffer = "0";
                    d.restockThreshold = 0;
                    d.restockThresholdBuffer = "0";
                    continue;
                }

                int allow = max - used;
                if (allow <= 0)
                {
                    trimmed += d.count;
                    d.enabled = false;
                    d.count = 0;
                    d.countBuffer = "0";
                    d.restockThreshold = 0;
                    d.restockThresholdBuffer = "0";
                    continue;
                }

                if (d.count > allow)
                {
                    trimmed += d.count - allow;
                    d.count = allow;
                }

                d.restockThreshold = GoodsItemData.NormalizeRestockThreshold(d.restockThreshold, d.count);
                d.restockThresholdBuffer = d.restockThreshold.ToString();
                d.countBuffer = d.count.ToString();
                used += d.count;
            }

            if (trimmed > 0)
                MarkDraftInventoryViewDirty();
            return trimmed;
        }

        /// <summary>
        /// 切换当前商品分类，负责重置搜索、滚动和列表缓存。
        /// </summary>
        private void TrySwitchDef(string newDefName)
        {
            if (!string.IsNullOrEmpty(newDefName) && !comp.AllowsGoodsCategory(newDefName))
            {
                Messages.Message(SimTranslation.T("RSMF.GoodsManager.InvalidCategory"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            draftActiveDefName = newDefName ?? "";
            searchQuery = "";
            listScroll = Vector2.zero;
            InvalidateFilteredItemsCache();
            MarkDraftInventoryViewDirty();
        }

        /// <summary>
        /// 获取会被当前草稿配置清退的商品列表，负责复用缓存避免每帧扫描货柜库存。
        /// </summary>
        private List<ThingDef> GetPendingEvict()
        {
            if (storage == null)
            {
                pendingEvictCache.Clear();
                return pendingEvictCache;
            }
            string categoryId = draftActiveDefName ?? "";
            if (!pendingEvictDirty && pendingEvictCategoryCacheId == categoryId && pendingEvictStockVersion == storedCountCacheVersion)
                return pendingEvictCache;

            Pojo.RuntimeGoodsCategory activeDef = GoodsCatalog.GetCategory(draftActiveDefName);
            pendingEvictCache.Clear();
            foreach (KeyValuePair<ThingDef, int> entry in storedCountCache)
            {
                ThingDef thingDef = entry.Key;
                if (thingDef == null) continue;

                int target = 0;
                if (activeDef != null && activeDef.Contains(thingDef))
                {
                    if (draftItemData.TryGetValue(thingDef.defName, out GoodsItemData data) && data.enabled)
                        target = data.count;
                }

                if (entry.Value > target)
                    pendingEvictCache.Add(thingDef);
            }

            pendingEvictCategoryCacheId = categoryId;
            pendingEvictStockVersion = storedCountCacheVersion;
            pendingEvictDirty = false;
            pendingEvictResultVersion++;
            return pendingEvictCache;
        }

        /// <summary>
        /// 获取指定商品的缓存库存数量，负责避免列表每行都重新扫描虚拟货柜。
        /// </summary>
        private int GetStored(ThingDef td)
        {
            if (td == null || storage == null) return 0;
            RefreshStoredCountCacheIfNeeded();
            return storedCountCache.TryGetValue(td, out int count) ? count : 0;
        }

        /// <summary>
        /// 获取搜索后的商品列表，负责在分类或搜索词变化时才重新过滤。
        /// </summary>
        private List<ThingDef> GetFilteredItems(Pojo.RuntimeGoodsCategory def)
        {
            string categoryId = def?.categoryId ?? "";
            string search = searchQuery ?? "";
            int sourceCount = def?.Items?.Count ?? 0;
            if (filteredCategoryCacheId == categoryId && filteredSearchCache == search && filteredSourceCount == sourceCount)
                return filteredItemsCache;

            filteredItemsCache.Clear();
            if (def?.Items != null)
            {
                for (int i = 0; i < def.Items.Count; i++)
                {
                    ThingDef thingDef = def.Items[i]?.thingDef;
                    if (thingDef == null) continue;
                    if (!string.IsNullOrEmpty(search) && (thingDef.label ?? "").IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    filteredItemsCache.Add(thingDef);
                }
            }

            filteredCategoryCacheId = categoryId;
            filteredSearchCache = search;
            filteredSourceCount = sourceCount;
            return filteredItemsCache;
        }

        /// <summary>
        /// 限制商品列表滚动位置，负责在搜索结果变短后避免滚动位置落在空白区域。
        /// </summary>
        private void ClampListScroll(float viewHeight, float outerHeight)
        {
            float maxY = Mathf.Max(0f, viewHeight - outerHeight);
            listScroll.y = Mathf.Clamp(listScroll.y, 0f, maxY);
            listScroll.x = 0f;
        }

        /// <summary>
        /// 刷新货柜库存缓存，负责把虚拟货柜内容聚合成按 ThingDef 查询的轻量字典。
        /// </summary>
        private void RefreshStoredCountCacheIfNeeded(bool force = false)
        {
            if (storage == null) return;
            int currentVersion = storage.StoredCountVersion;
            if (!force && !storedCountCacheDirty && observedStorageCountVersion == currentVersion)
                return;

            storage.CopyStoredCountsTo(storedCountCache);
            observedStorageCountVersion = currentVersion;
            storedCountCacheDirty = false;
            storedCountCacheVersion++;
            pendingEvictDirty = true;
        }

        /// <summary>
        /// 标记草稿库存视图需要刷新，负责在启用状态或目标数量变化后更新容量与清退提示。
        /// </summary>
        private void MarkDraftInventoryViewDirty()
        {
            draftTargetTotalDirty = true;
            pendingEvictDirty = true;
        }

        /// <summary>
        /// 标记筛选列表需要刷新，负责在切换分类或外部商品目录变化后重建列表。
        /// </summary>
        private void InvalidateFilteredItemsCache()
        {
            filteredCategoryCacheId = "";
            filteredSearchCache = "";
            filteredSourceCount = -1;
        }
    }
}

