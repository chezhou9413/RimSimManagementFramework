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
        // ── 尺寸与布局常量 (优化了间距和比例) ──
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
        private const float BottomH = 50f;
        private const float SearchBarH = 30f;
        private const float ScrW = 16f;

        // ── 配色方案 ──
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
            allDefs = GoodsCatalog.Manager?.Categories
                .Where(d => d != null && d.Items != null && d.Items.Count > 0)
                .OrderBy(d => d.label)
                .ToList() ?? new List<Pojo.RuntimeGoodsCategory>();

            draftActiveDefName = comp.ActiveGoodsDefName;
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
            Widgets.Label(titleRect, "分类导航");
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
            Widgets.Label(clearR, "— 清除选择 —");
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
                Widgets.Label(rect, "请在左侧选择需要管理的货品分类");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                return;
            }

            Rect searchR = new Rect(rect.x, rect.y, rect.width, SearchBarH);
            searchQuery = Widgets.TextField(new Rect(searchR.x, searchR.y, 200f, 24f), searchQuery);
            if (string.IsNullOrEmpty(searchQuery))
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Widgets.Label(new Rect(searchR.x + 6f, searchR.y + 2f, 190f, 24f), "搜索物品...");
                GUI.color = Color.white;
            }

            var filteredList = def.Items.Select(i => i.thingDef).Where(t => t != null).Where(t =>
                string.IsNullOrEmpty(searchQuery) ||
                t.label.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (storage != null)
            {
                int targetTotal = GetDraftTargetTotal(def);
                string capText = $"目标总量: {targetTotal}/{storage.MaxTotalCapacity}";
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

            curX -= FieldW; Widgets.Label(new Rect(curX, rect.y, FieldW, rect.height), "单价(银)"); curX -= ColGap;
            curX -= FieldW; Widgets.Label(new Rect(curX, rect.y, FieldW, rect.height), "目标量"); curX -= ColGap;
            curX -= SliderW; Widgets.Label(new Rect(curX, rect.y, SliderW, rect.height), "快速调节"); curX -= ColGap;
            curX -= StockW; Widgets.Label(new Rect(curX, rect.y, StockW, rect.height), "当前库存"); curX -= ColGap;

            float leftStart = rect.x + RowPad + CheckSz + ColGap + IconSz + ColGap;
            Widgets.Label(new Rect(leftStart, rect.y, curX - leftStart, rect.height), "货品名称");

            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        private void DrawItemRow(Rect row, ThingDef td, bool alt, Pojo.RuntimeGoodsCategory activeDef)
        {
            GoodsItemData d = GetDraftItem(td);
            bool nowEnabled = d.enabled;
            bool changed = false;

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
                changed = true;
                if (d.enabled && d.count <= 0)
                {
                    d.count = 1;
                    d.countBuffer = "1";
                }
                // ── 【修改】首次勾选时，若尚未设置价格，自动填入市场价 ──
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
                // ── 【修改】初始化 priceBuffer 时，若价格为 0 则先填入市场价 ──
                if (d.priceBuffer == null)
                {
                    if (d.price <= 0f)
                        d.price = td.BaseMarketValue > 0f ? td.BaseMarketValue : 1f;
                    d.priceBuffer = d.price.ToString("F0");
                }
                Widgets.TextFieldNumeric(new Rect(rightX, ctrlY, FieldW, 24f), ref d.price, ref d.priceBuffer, 0f, 99999f);
                TooltipHandler.TipRegion(new Rect(rightX, ctrlY, FieldW, 24f), "参考市价: " + td.BaseMarketValue.ToString("F1") + " 银");
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
                    changed = true;
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
                    changed = true;
                }
            }
            else { DrawDisabledDash(new Rect(rightX, row.y, SliderW, RowH)); }
            rightX -= ColGap;

            if (changed)
            {
                ClampDraftCapacity(activeDef);
            }

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
            Widgets.Label(rect, $"⚠️ 注意：保存后以下物品将被移出仓库：{names.Truncate(rect.width - 100f)}");
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawBottomBar(Rect rect)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);

            float btnY = rect.y + (rect.height - 30f) / 2f;
            Rect capacityRect = new Rect(rect.x + 170f, rect.y + 2f, rect.width - 400f, 16f);

            Pojo.RuntimeGoodsCategory activeDef = GoodsCatalog.GetCategory(draftActiveDefName);
            if (storage != null && activeDef != null)
            {
                int total = GetDraftTargetTotal(activeDef);
                string text = $"容量: {total}/{storage.MaxTotalCapacity}";
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = total <= storage.MaxTotalCapacity ? CStockOk : CStockNo;
                Widgets.Label(capacityRect, text);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft;
            float lx = rect.x + 10f;
            GUI.color = CStockOk; Widgets.Label(new Rect(lx, rect.y, 48f, rect.height), "■ 充足"); lx += 50f;
            GUI.color = CStockLow; Widgets.Label(new Rect(lx, rect.y, 48f, rect.height), "■ 不足"); lx += 50f;
            GUI.color = CStockNo; Widgets.Label(new Rect(lx, rect.y, 48f, rect.height), "■ 缺货");
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;

            if (!string.IsNullOrEmpty(draftActiveDefName))
            {
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.center.x - 110f, btnY, 100f, 30f), "全选当前页", true, GameFont.Tiny))
                    ToggleAllCurrentDef(true);
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.center.x + 10f, btnY, 100f, 30f), "清空当前页", true, GameFont.Tiny))
                    ToggleAllCurrentDef(false);
            }

            if (SimUiStyle.DrawPrimaryButton(new Rect(rect.xMax - 110f, btnY, 100f, 30f), "确认保存", true, GameFont.Tiny))
            {
                int trimmed = ClampDraftCapacity(activeDef);
                if (trimmed > 0)
                {
                    Messages.Message($"已按货柜容量上限自动调整目标量，超出部分 {trimmed} 件已移除。", MessageTypeDefOf.NeutralEvent, false);
                }
                comp.ApplySettings(draftActiveDefName, draftItemData);
                Close();
            }
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 220f, btnY, 100f, 30f), "取消", true, GameFont.Tiny))
                Close();
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
                // ── 【修改】全选时也自动填入市场价 ──
                if (enable && d.price <= 0f)
                {
                    d.price = td.BaseMarketValue > 0f ? td.BaseMarketValue : 1f;
                    d.priceBuffer = d.price.ToString("F0");
                }
            }

            int trimmed = ClampDraftCapacity(def);
            if (trimmed > 0)
            {
                Messages.Message($"已按货柜容量上限自动调整目标量，超出部分 {trimmed} 件已移除。", MessageTypeDefOf.NeutralEvent, false);
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

