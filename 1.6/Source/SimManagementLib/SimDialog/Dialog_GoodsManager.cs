using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace SimManagementLib.SimDialog
{
    public class Dialog_GoodsManager : Window
    {
        // 尺寸与布局常量。
        private const float SidebarW = 160f;
        private const float DivW = 2f;
        private const float RowH = 46f;
        private const float IconSz = 28f;
        private const float CheckSz = 24f;
        private const float FieldW = 65f;
        private const float StockW = 60f;
        private const float SliderW = 110f;
        private const float ColGap = 10f;
        private const float RowPad = 8f;
        private const float HeaderH = 30f;
        private const float BottomH = 74f;
        private const float SearchBarH = 30f;
        private const float ScrW = 16f;

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

        public override Vector2 InitialSize => new Vector2(800f, 620f);

        public Dialog_GoodsManager(ThingComp_GoodsData comp)
        {
            this.comp = comp;
            this.storage = comp.parent as Building_SimContainer;
            doCloseButton = false;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            resizeable = true;
            draggable = true;

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

        private GoodsItemData GetDraftItem(ThingDef td)
        {
            if (!draftItemData.TryGetValue(td.defName, out var d))
                draftItemData[td.defName] = d = new GoodsItemData();
            return d;
        }

        public override void DoWindowContents(Rect inRect)
        {
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

            var filteredList = def.Items.Select(i => i.thingDef).Where(t => t != null).Where(t =>
                string.IsNullOrEmpty(searchQuery) ||
                t.label.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

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
            for (int i = 0; i < filteredList.Count; i++)
            {
                DrawItemRow(new Rect(0f, i * RowH, viewW, RowH), filteredList[i], i % 2 == 0, def);
            }
            Widgets.EndScrollView();
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = new Color(0.8f, 0.8f, 0.8f);

            float curX = rect.xMax - RowPad;

            curX -= FieldW; Widgets.Label(new Rect(curX, rect.y, FieldW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Price")); curX -= ColGap;
            curX -= FieldW; Widgets.Label(new Rect(curX, rect.y, FieldW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Target")); curX -= ColGap;
            curX -= SliderW; Widgets.Label(new Rect(curX, rect.y, SliderW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Slider")); curX -= ColGap;
            curX -= StockW; Widgets.Label(new Rect(curX, rect.y, StockW, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Stock")); curX -= ColGap;

            float leftStart = rect.x + RowPad + CheckSz + ColGap + IconSz + ColGap;
            Widgets.Label(new Rect(leftStart, rect.y, curX - leftStart, rect.height), SimTranslation.T("RSMF.GoodsManager.Header.Name"));

            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        private void DrawItemRow(Rect row, ThingDef td, bool alt, Pojo.RuntimeGoodsCategory activeDef)
        {
            GoodsItemData d = GetDraftItem(td);
            bool nowEnabled = d.enabled;

            if (nowEnabled) Widgets.DrawBoxSolid(row, CCheckedBg);
            else if (alt) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.02f));

            Widgets.DrawHighlightIfMouseover(row);
            if (!string.IsNullOrEmpty(td.description))
                TooltipHandler.TipRegion(row, td.LabelCap + "\n\n" + td.description);

            if (!nowEnabled) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);

            float midY = row.y + (RowH - IconSz) / 2f;
            float ctrlY = row.y + (RowH - 24f) / 2f;

            // 1. Checkbox
            float x = row.x + RowPad;
            Widgets.Checkbox(x, row.y + (RowH - CheckSz) / 2f, ref nowEnabled, CheckSz, paintable: true);
            if (nowEnabled != d.enabled)
            {
                d.enabled = nowEnabled;
                if (d.enabled && d.count <= 0)
                {
                    d.count = 1;
                    d.countBuffer = "1";
                }
                // 首次勾选商品时补齐默认价格，避免保存出 0 价商品。
                if (d.enabled && d.price <= 0f)
                {
                    d.price = td.BaseMarketValue > 0f ? td.BaseMarketValue : 1f;
                    d.priceBuffer = d.price.ToString("F0");
                }
            }
            x += CheckSz + ColGap;

            // 2. Icon
            Widgets.ThingIcon(new Rect(x, midY, IconSz, IconSz), td);
            x += IconSz + ColGap;

            float rightX = row.xMax - RowPad;

            // 6. 价格输入框
            rightX -= FieldW;
            if (nowEnabled)
            {
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

            // 5. 数量输入框
            rightX -= FieldW;
            if (nowEnabled)
            {
                if (d.countBuffer == null) d.countBuffer = d.count.ToString();
                int prevCount = d.count;
                Widgets.TextFieldNumeric(new Rect(rightX, ctrlY, FieldW, 24f), ref d.count, ref d.countBuffer, 0, 999999);
                if (d.count != prevCount)
                {
                    d.countBuffer = d.count.ToString();
                }
            }
            else { DrawDisabledDash(new Rect(rightX, row.y, FieldW, RowH)); }
            rightX -= ColGap;

            // 4. 数量滑条
            rightX -= SliderW;
            if (nowEnabled)
            {
                int newCount = (int)Widgets.HorizontalSlider(new Rect(rightX, row.y + (RowH - 16f) / 2f, SliderW, 16f), d.count, 0f, 999f, true);
                if (newCount != d.count)
                {
                    d.count = newCount;
                    d.countBuffer = newCount.ToString();
                }
            }
            else { DrawDisabledDash(new Rect(rightX, row.y, SliderW, RowH)); }
            rightX -= ColGap;

            // 3. 库存数量
            rightX -= StockW;
            int stk = GetStored(td);
            Color stockColor = stk == 0 ? CStockNo : (nowEnabled && stk < d.count) ? CStockLow : CStockOk;
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Small;
            GUI.color = nowEnabled ? stockColor : new Color(0.5f, 0.5f, 0.5f);
            Widgets.Label(new Rect(rightX, row.y, StockW, RowH), stk.ToString());
            rightX -= ColGap;

            // 7. 物品名称
            float nameW = rightX - x;
            Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Small;
            GUI.color = nowEnabled ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(x, row.y, nameW, RowH), td.LabelCap.Truncate(nameW));

            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        private void DrawDisabledDash(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.3f, 0.3f, 0.3f);
            Widgets.Label(rect, "-");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawEvictBanner(Rect rect, List<ThingDef> pendingEvict)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.8f, 0.4f, 0f, 0.2f));
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 0.8f, 0.4f);
            string names = string.Join(", ", pendingEvict.Select(t => t.LabelCap));
            Widgets.Label(rect, SimTranslation.T("RSMF.GoodsManager.PendingEvictWarning", names.Truncate(rect.width - 100f).Named("names")));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
        }

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
                    price = Mathf.Max(0f, item?.price ?? 0f)
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
                // 全选时同步补齐未设置价格的商品，避免后续保存出 0 价。
                if (enable && d.price <= 0f)
                {
                    d.price = td.BaseMarketValue > 0f ? td.BaseMarketValue : 1f;
                    d.priceBuffer = d.price.ToString("F0");
                }
            }

            int trimmed = ClampDraftCapacity(def);
            if (trimmed > 0)
            {
                Messages.Message(SimTranslation.T("RSMF.GoodsManager.AutoTrimNotice", trimmed.Named("trimmed")), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private int GetDraftTargetTotal(Pojo.RuntimeGoodsCategory def)
        {
            if (def == null) return 0;

            int total = 0;
            for (int i = 0; i < def.Items.Count; i++)
            {
                ThingDef td = def.Items[i]?.thingDef;
                if (td == null) continue;
                if (!draftItemData.TryGetValue(td.defName, out GoodsItemData d) || d == null) continue;
                if (!d.enabled || d.count <= 0) continue;
                total += d.count;
            }

            return total;
        }

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
                    continue;
                }

                int allow = max - used;
                if (allow <= 0)
                {
                    trimmed += d.count;
                    d.enabled = false;
                    d.count = 0;
                    d.countBuffer = "0";
                    continue;
                }

                if (d.count > allow)
                {
                    trimmed += d.count - allow;
                    d.count = allow;
                }

                d.countBuffer = d.count.ToString();
                used += d.count;
            }

            return trimmed;
        }

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
        }

        private List<ThingDef> GetPendingEvict()
        {
            List<ThingDef> pending = new List<ThingDef>();
            if (storage == null) return pending;

            Pojo.RuntimeGoodsCategory activeDef = GoodsCatalog.GetCategory(draftActiveDefName);

            HashSet<ThingDef> checkedDefs = new HashSet<ThingDef>();

            foreach (Thing t in storage.GetDirectlyHeldThings())
            {
                if (checkedDefs.Contains(t.def)) continue;
                checkedDefs.Add(t.def);

                int target = 0;
                if (activeDef != null && activeDef.Contains(t.def))
                {
                    if (draftItemData.TryGetValue(t.def.defName, out GoodsItemData data) && data.enabled)
                        target = data.count;
                }

                if (storage.CountStored(t.def) > target)
                    pending.Add(t.def);
            }
            return pending;
        }

        private int GetStored(ThingDef td) => storage != null ? storage.CountStored(td) : 0;
    }
}

