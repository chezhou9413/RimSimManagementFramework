using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //组件货柜的添加品种视图，提供殖民地现有物品筛选、分类侧栏与连续多选添加。
    internal sealed partial class Dialog_StorageSales
    {
        private const int BulkAddLimit = 50;
        private const float PickerStockW = 86f;
        private const float PickerValueW = 86f;
        private const float PickerBtnW = 92f;

        private string pickerSearch = "";
        private bool pickerColonyOnly = true;
        private bool pickerScopeInitialized;
        private ThingCategoryDef selectedCategory;
        private bool filterOtherCategory;
        private readonly List<ThingDef> allCandidates = new();
        private readonly Dictionary<ThingDef, int> mapStock = new();
        private readonly List<(ThingCategoryDef category, int count)> pickerCategories = new();
        private readonly List<ThingDef> pickerRows = new();
        private readonly List<Thing> tmpMapThings = new();
        private int colonyCandidateCount;
        private int otherCategoryCount;
        private Vector2 pickerCategoryScroll;
        private Vector2 pickerListScroll;

        //收集宿主允许的全部品种及地图现存数量，首次打开时在地图有货时默认聚焦殖民地现有物品。
        private void RefreshPickerData()
        {
            allCandidates.Clear();
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                if (sales.AllowsDef(def)) allCandidates.Add(def);
            }

            mapStock.Clear();
            Map map = sales.Host.Map;
            if (map != null)
            {
                tmpMapThings.Clear();
                map.listerThings.GetAllThings(tmpMapThings, ThingRequestGroup.HaulableEver, null, lookInHaulSources: true);
                for (int i = 0; i < tmpMapThings.Count; i++)
                {
                    Thing thing = tmpMapThings[i];
                    if (thing == null || thing.Destroyed || !sales.AllowsDef(thing.def)) continue;
                    mapStock[thing.def] = mapStock.GetValueOrDefault(thing.def) + thing.stackCount;
                }
                tmpMapThings.Clear();
            }

            colonyCandidateCount = allCandidates.Count(d => mapStock.GetValueOrDefault(d) > 0);
            if (!pickerScopeInitialized)
            {
                pickerColonyOnly = colonyCandidateCount > 0;
                pickerScopeInitialized = true;
            }
            RebuildPickerCategories();
            RebuildPickerRows();
        }

        //按当前范围重建顶层分类计数，保持左侧导航简洁清晰。
        private void RebuildPickerCategories()
        {
            pickerCategories.Clear();
            otherCategoryCount = 0;
            var counts = new Dictionary<ThingCategoryDef, int>();
            for (int i = 0; i < allCandidates.Count; i++)
            {
                ThingDef def = allCandidates[i];
                if (pickerColonyOnly && mapStock.GetValueOrDefault(def) <= 0) continue;
                ThingCategoryDef top = TopCategory(def);
                if (top == null) otherCategoryCount++;
                else counts[top] = counts.GetValueOrDefault(top) + 1;
            }
            foreach (var pair in counts.OrderBy(p => p.Key.label))
                pickerCategories.Add((pair.Key, pair.Value));

            if (selectedCategory != null && !counts.ContainsKey(selectedCategory))
                selectedCategory = null;
            if (filterOtherCategory && otherCategoryCount == 0)
                filterOtherCategory = false;
        }

        //按范围、分类和搜索词刷新候选物品行，优先展示地图现存较多的物品。
        private void RebuildPickerRows()
        {
            pickerRows.Clear();
            for (int i = 0; i < allCandidates.Count; i++)
            {
                ThingDef def = allCandidates[i];
                if (pickerColonyOnly && mapStock.GetValueOrDefault(def) <= 0) continue;
                if (filterOtherCategory)
                {
                    if (TopCategory(def) != null) continue;
                }
                else if (selectedCategory != null && TopCategory(def) != selectedCategory)
                {
                    continue;
                }
                if (!pickerSearch.NullOrEmpty() && !MatchesSearch(def, pickerSearch)) continue;
                pickerRows.Add(def);
            }
            pickerRows.Sort((a, b) =>
            {
                int stockCmp = mapStock.GetValueOrDefault(b).CompareTo(mapStock.GetValueOrDefault(a));
                return stockCmp != 0 ? stockCmp : string.Compare(a.label, b.label, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        //绘制左侧范围与分类栏及右侧可连续添加的候选品种列表。
        private void DrawPickerBody(Rect rect)
        {
            float sideW = Mathf.Clamp(rect.width * 0.24f, 190f, 240f);
            Rect sideRect = new Rect(rect.x, rect.y, sideW, rect.height);
            Rect mainRect = new Rect(sideRect.xMax + 10f, rect.y, Mathf.Max(200f, rect.width - sideW - 10f), rect.height);

            DrawPickerSidebar(sideRect);
            DrawPickerListPanel(mainRect);
        }

        //绘制左侧殖民地现有/全部范围切换及顶层物品分类导航。
        private void DrawPickerSidebar(Rect rect)
        {
            ShopUiVisualUtility.DrawSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float h = ShopUiVisualUtility.ControlHeight();

            string colonyLabel = SimTranslation.T("RSMF.StorageSales.Picker.ScopeColony", colonyCandidateCount.Named("count"));
            string allLabel = SimTranslation.T("RSMF.StorageSales.Picker.ScopeAll", allCandidates.Count.Named("count"));

            if (ShopUiVisualUtility.DrawTabButton(new Rect(inner.x, inner.y, inner.width, h), colonyLabel, pickerColonyOnly, ShopUiVisualUtility.MutedText))
                SwitchPickerScope(true);
            if (ShopUiVisualUtility.DrawTabButton(new Rect(inner.x, inner.y + h + 4f, inner.width, h), allLabel, !pickerColonyOnly, ShopUiVisualUtility.MutedText))
                SwitchPickerScope(false);

            float divY = inner.y + h * 2f + 10f;
            Widgets.DrawBoxSolid(new Rect(inner.x, divY, inner.width, 1f), ShopUiVisualUtility.Divider);

            Rect outRect = new Rect(inner.x, divY + 6f, inner.width, Mathf.Max(0f, inner.yMax - divY - 6f));
            float itemH = Mathf.Max(30f, ShopUiVisualUtility.LineHeight(GameFont.Small) + 8f);
            int totalEntries = 1 + pickerCategories.Count + (otherCategoryCount > 0 ? 1 : 0);
            float viewW = Mathf.Max(60f, outRect.width - ScrollbarW);
            Rect viewRect = new Rect(0f, 0f, viewW, Mathf.Max(outRect.height, totalEntries * itemH));

            Widgets.BeginScrollView(outRect, ref pickerCategoryScroll, viewRect);
            try
            {
                int rowIndex = 0;
                int scopeTotal = pickerColonyOnly ? colonyCandidateCount : allCandidates.Count;
                if (DrawCategoryEntry(new Rect(0f, rowIndex++ * itemH, viewW, itemH),
                    SimTranslation.T("RSMF.StorageSales.Picker.AllCategories"), scopeTotal, selectedCategory == null && !filterOtherCategory))
                {
                    selectedCategory = null;
                    filterOtherCategory = false;
                    pickerListScroll = Vector2.zero;
                    RebuildPickerRows();
                }

                for (int i = 0; i < pickerCategories.Count; i++)
                {
                    var entry = pickerCategories[i];
                    if (DrawCategoryEntry(new Rect(0f, rowIndex++ * itemH, viewW, itemH),
                        entry.category.LabelCap, entry.count, !filterOtherCategory && selectedCategory == entry.category))
                    {
                        selectedCategory = entry.category;
                        filterOtherCategory = false;
                        pickerListScroll = Vector2.zero;
                        RebuildPickerRows();
                    }
                }

                if (otherCategoryCount > 0 && DrawCategoryEntry(new Rect(0f, rowIndex * itemH, viewW, itemH),
                    SimTranslation.T("RSMF.StorageSales.Picker.OtherCategory"), otherCategoryCount, filterOtherCategory))
                {
                    selectedCategory = null;
                    filterOtherCategory = true;
                    pickerListScroll = Vector2.zero;
                    RebuildPickerRows();
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制单条分类导航项，选中项带框架青色竖条标识。
        private static bool DrawCategoryEntry(Rect rect, string label, int count, bool selected)
        {
            if (selected)
            {
                Widgets.DrawBoxSolid(rect, CSideSel);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, 3f, rect.height - 8f), ShopUiVisualUtility.Accent);
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, CSideHov);
            }

            string countText = count.ToString();
            float countW = Mathf.Max(28f, Text.CalcSize(countText).x + 8f);
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 10f, rect.y, rect.width - countW - 14f, rect.height),
                label, selected ? Color.white : ShopUiVisualUtility.MutedText, GameFont.Small);
            DrawCenteredLabel(new Rect(rect.xMax - countW - 4f, rect.y, countW, rect.height),
                countText, selected ? Color.white : ShopUiVisualUtility.MutedText, GameFont.Tiny);
            return !selected && Widgets.ButtonInvisible(rect);
        }

        //切换殖民地现有与全部可储存范围并重置滚动位置。
        private void SwitchPickerScope(bool colonyOnly)
        {
            pickerColonyOnly = colonyOnly;
            pickerCategoryScroll = Vector2.zero;
            pickerListScroll = Vector2.zero;
            RebuildPickerCategories();
            RebuildPickerRows();
        }

        //绘制右侧搜索栏、批量添加按钮与候选品种表格。
        private void DrawPickerListPanel(Rect rect)
        {
            float controlH = ShopUiVisualUtility.ControlHeight();
            int unaddedCount = pickerRows.Count(d => !draft.ContainsKey(d));
            string addAllLabel = SimTranslation.T("RSMF.StorageSales.Picker.AddFiltered", unaddedCount.Named("count"));
            float addAllW = Mathf.Max(118f, Text.CalcSize(addAllLabel).x + 22f);

            Rect addAllRect = new Rect(rect.xMax - addAllW, rect.y, addAllW, controlH);
            Rect searchRect = new Rect(rect.x, rect.y, Mathf.Max(120f, addAllRect.x - rect.x - 8f), controlH);

            string nextSearch = ShopUiVisualUtility.DrawSearchField(searchRect, pickerSearch);
            if (nextSearch != pickerSearch)
            {
                pickerSearch = nextSearch;
                pickerListScroll = Vector2.zero;
                RebuildPickerRows();
            }

            bool canBulkAdd = unaddedCount > 0 && unaddedCount <= BulkAddLimit;
            if (ShopUiVisualUtility.DrawSecondaryButton(addAllRect, addAllLabel, canBulkAdd))
                AddFilteredCandidates();
            TooltipHandler.TipRegion(addAllRect, SimTranslation.T("RSMF.StorageSales.Picker.AddFilteredTip", BulkAddLimit.Named("limit")));

            Rect tableFrame = new Rect(rect.x, rect.y + controlH + 8f, rect.width, Mathf.Max(0f, rect.height - controlH - 8f));
            ShopUiVisualUtility.DrawSection(tableFrame);
            if (tableFrame.height <= controlH + 4f) return;

            float headerH = ShopUiVisualUtility.LineHeight(GameFont.Small) + 10f;
            Rect headerRect = new Rect(tableFrame.x + 1f, tableFrame.y + 1f, tableFrame.width - 2f, headerH);
            ShopUiVisualUtility.DrawTableHeaderBackground(headerRect);
            DrawPickerHeader(new Rect(headerRect.x, headerRect.y, headerRect.width - ScrollbarW, headerRect.height));

            Rect listRect = new Rect(tableFrame.x + 1f, headerRect.yMax, tableFrame.width - 2f, Mathf.Max(0f, tableFrame.yMax - headerRect.yMax - 1f));
            if (pickerRows.Count == 0)
            {
                ShopUiLayoutUtility.DrawEmptyState(listRect, SimTranslation.T("RSMF.StorageSales.Picker.Empty"));
                return;
            }

            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float rowH = Mathf.Max(44f, smallH + tinyH + 10f, controlH + 6f);
            float viewW = Mathf.Max(100f, listRect.width - ScrollbarW);
            Rect viewRect = new Rect(0f, 0f, viewW, Mathf.Max(listRect.height, pickerRows.Count * rowH));
            pickerListScroll.y = Mathf.Clamp(pickerListScroll.y, 0f, Mathf.Max(0f, viewRect.height - listRect.height));

            Widgets.BeginScrollView(listRect, ref pickerListScroll, viewRect);
            try
            {
                int first = Mathf.Max(0, Mathf.FloorToInt(pickerListScroll.y / rowH) - 1);
                int end = Mathf.Min(pickerRows.Count, Mathf.CeilToInt((pickerListScroll.y + listRect.height) / rowH) + 1);
                for (int i = first; i < end; i++)
                    DrawPickerRow(new Rect(0f, i * rowH, viewW, rowH), pickerRows[i], i);
            }
            finally { Widgets.EndScrollView(); }
        }

        //绘制候选品种表头。
        private static void DrawPickerHeader(Rect rect)
        {
            float rx = rect.xMax - RowPad;
            rx -= PickerBtnW;
            DrawCenteredLabel(new Rect(rx, rect.y, PickerBtnW, rect.height), SimTranslation.T("RSMF.StorageSales.Action"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            rx -= ColGap + PickerValueW;
            DrawCenteredLabel(new Rect(rx, rect.y, PickerValueW, rect.height), SimTranslation.T("RSMF.StorageSales.Picker.BaseValue"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            rx -= ColGap + PickerStockW;
            DrawCenteredLabel(new Rect(rx, rect.y, PickerStockW, rect.height), SimTranslation.T("RSMF.StorageSales.Picker.MapStock"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
            rx -= ColGap;
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + RowPad, rect.y, Mathf.Max(40f, rx - rect.x - RowPad), rect.height),
                SimTranslation.T("RSMF.StorageSales.Item"), ShopUiVisualUtility.MutedText, GameFont.Tiny);
        }

        //绘制单条候选品种行，点击添加或移出后保留在当前列表以便连续选择。
        private void DrawPickerRow(Rect row, ThingDef def, int index)
        {
            bool added = draft.ContainsKey(def);
            bool onShelf = stock.GetValueOrDefault(def) > 0;
            ShopUiVisualUtility.DrawTableRowBackground(row, index, added);

            float lx = row.x + RowPad;
            Widgets.ThingIcon(new Rect(lx, row.center.y - 15f, 30f, 30f), def);
            lx += 30f + ColGap;

            float rx = row.xMax - RowPad;
            float btnH = Mathf.Clamp(ShopUiVisualUtility.ControlHeight() - 4f, ShopUiVisualUtility.LineHeight(GameFont.Small) + 4f, row.height - 8f);
            rx -= PickerBtnW;
            Rect btnRect = new Rect(rx, row.center.y - btnH * 0.5f, PickerBtnW, btnH);
            rx -= ColGap + PickerValueW;
            Rect valueRect = new Rect(rx, row.y, PickerValueW, row.height);
            rx -= ColGap + PickerStockW;
            Rect stockRect = new Rect(rx, row.y, PickerStockW, row.height);
            rx -= ColGap;

            Rect nameRect = new Rect(lx, row.y, Mathf.Max(40f, rx - lx), row.height);
            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float topY = nameRect.center.y - (smallH + tinyH) * 0.5f;
            ShopUiVisualUtility.DrawCellLabel(new Rect(nameRect.x, topY, nameRect.width, smallH), def.LabelCap, Color.white, GameFont.Small);
            string categoryLabel = def.FirstThingCategory?.LabelCap ?? SimTranslation.T("RSMF.StorageSales.Picker.OtherCategory");
            ShopUiVisualUtility.DrawCellLabel(new Rect(nameRect.x, topY + smallH, nameRect.width, tinyH), categoryLabel, ShopUiVisualUtility.MutedText, GameFont.Tiny);
            if (Mouse.IsOver(nameRect) && !def.description.NullOrEmpty())
                TooltipHandler.TipRegion(nameRect, def.LabelCap + "\n\n" + def.description);

            int colonyCount = mapStock.GetValueOrDefault(def);
            DrawCenteredLabel(stockRect, colonyCount.ToString(), colonyCount > 0 ? CStockOk : ShopUiVisualUtility.MutedText, GameFont.Small);
            DrawCenteredLabel(valueRect, def.BaseMarketValue.ToString("0.#"), ShopUiVisualUtility.MutedText, GameFont.Small);

            if (!added)
            {
                if (ShopUiVisualUtility.DrawPrimaryButton(btnRect, SimTranslation.T("RSMF.StorageSales.Picker.AddOne"), true, GameFont.Tiny))
                {
                    removedRules.Remove(def);
                    EnsureDraft(def);
                    dirty = true;
                }
            }
            else if (onShelf)
            {
                ShopUiVisualUtility.DrawSecondaryButton(btnRect, SimTranslation.T("RSMF.StorageSales.Picker.OnShelf"), false, GameFont.Tiny);
                TooltipHandler.TipRegion(btnRect, SimTranslation.T("RSMF.StorageSales.Picker.OnShelfTip"));
            }
            else
            {
                if (ShopUiVisualUtility.DrawSecondaryButton(btnRect, SimTranslation.T("RSMF.StorageSales.Picker.Added"), true, GameFont.Tiny))
                {
                    draft.Remove(def);
                    removedRules.Add(def);
                    dirty = true;
                }
                TooltipHandler.TipRegion(btnRect, SimTranslation.T("RSMF.StorageSales.Picker.AddedTip"));
            }
        }

        //将当前筛选结果中尚未加入清单的品种批量加入草稿。
        private void AddFilteredCandidates()
        {
            int added = 0;
            for (int i = 0; i < pickerRows.Count && added < BulkAddLimit; i++)
            {
                ThingDef def = pickerRows[i];
                if (draft.ContainsKey(def)) continue;
                removedRules.Remove(def);
                EnsureDraft(def);
                added++;
            }
            if (added > 0) dirty = true;
        }

        //沿分类树向上定位顶层物品分类，减少导航层级碎片。
        private static ThingCategoryDef TopCategory(ThingDef def)
        {
            ThingCategoryDef category = def.FirstThingCategory;
            while (category?.parent != null && category.parent != ThingCategoryDefOf.Root)
                category = category.parent;
            return category == ThingCategoryDefOf.Root ? null : category;
        }
    }
}
