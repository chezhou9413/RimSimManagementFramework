using SimManagementLib.Api;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //按商品名称和来源模组筛选已加载物品，支持多选后统一注册分类。
    public class Dialog_QuickGoodsRegistration : Window
    {
        private readonly List<ThingDef> candidates;
        private readonly List<IGrouping<ModContentPack, ThingDef>> sources;
        private readonly HashSet<ThingDef> selected = new HashSet<ThingDef>();
        private List<ThingDef> shown;
        private string search = "";
        private string sourceSearch = "";
        private int selectedSource = -1;
        private Vector2 itemScroll;
        private Vector2 sourceScroll;

        //从现有商品注册规则获取候选项，保持原有可售物品范围。
        public Dialog_QuickGoodsRegistration()
        {
            candidates = CustomGoodsDatabase.GetAllCandidateThings();
            sources = candidates.GroupBy(def => def.modContentPack).OrderBy(group => SourceName(group.Key)).ToList();
            RefreshItems();
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
        }

        //限制窗口在当前界面范围内。
        public override Vector2 InitialSize => new Vector2(Mathf.Min(980f, UI.screenWidth - 48f), Mathf.Min(720f, UI.screenHeight - 48f));

        //绘制搜索、来源筛选、商品列表和分类确认入口。
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
                float y = inRect.y + ShopUiVisualUtility.DrawPageHeading(inRect,
                    SimTranslation.T("RSMF.QuickGoodsRegister.Title"), SimTranslation.T("RSMF.QuickGoodsRegister.BrowserHint"), true);
                float control = ShopUiVisualUtility.ControlHeight();
                string nextSearch = ShopUiVisualUtility.DrawSearchField(new Rect(inRect.x, y, inRect.width, control),
                    search, SimTranslation.T("RSMF.QuickGoodsRegister.SearchItems"));
                if (nextSearch != search)
                {
                    search = nextSearch;
                    RefreshItems();
                }
                y += control + 10f;
                float footerY = inRect.yMax - control;
                float sourceWidth = Mathf.Min(250f, inRect.width * 0.32f);
                DrawSources(new Rect(inRect.x, y, sourceWidth, footerY - y - 10f));
                Rect goodsRect = new Rect(inRect.x + sourceWidth + 10f, y, inRect.width - sourceWidth - 10f, footerY - y - 10f);
                float line = ShopUiVisualUtility.LineHeight(GameFont.Small);
                ShopUiVisualUtility.DrawCellLabel(new Rect(goodsRect.x, y, goodsRect.width, line),
                    SimTranslation.T("RSMF.QuickGoodsRegister.BrowserCount", shown.Count.Named("count"), selected.Count.Named("selected")),
                    ShopUiVisualUtility.MutedText);
                goodsRect.yMin += line + 6f;
                DrawItems(goodsRect);
                if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.x, footerY, 100f, control), SimTranslation.T("RSMF.Common.Cancel"))) Close();
                string nextLabel = SimTranslation.T("RSMF.QuickGoodsRegister.ChooseCategory");
                float nextWidth = Mathf.Max(160f, Text.CalcSize(nextLabel).x + 24f);
                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - nextWidth, footerY, nextWidth, control), nextLabel, selected.Count > 0))
                {
                    List<ThingDef> items = candidates.Where(selected.Contains).ToList();
                    Close();
                    Find.WindowStack.Add(new Dialog_RegisterSelectedGoodsCategory(items));
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        //绘制独立滚动的来源列表，切换筛选时保留已选商品。
        private void DrawSources(Rect rect)
        {
            ShopUiVisualUtility.DrawSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float line = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float control = ShopUiVisualUtility.ControlHeight();
            ShopUiVisualUtility.DrawCellLabel(new Rect(inner.x, inner.y, inner.width, line), SimTranslation.T("RSMF.QuickGoodsRegister.SourceMods"));
            float y = inner.y + line + 6f;
            string nextSearch = ShopUiVisualUtility.DrawSearchField(new Rect(inner.x, y, inner.width, control),
                sourceSearch, SimTranslation.T("RSMF.QuickGoodsRegister.SearchMods"));
            if (nextSearch != sourceSearch)
            {
                sourceSearch = nextSearch;
                sourceScroll = Vector2.zero;
            }
            y += control + 6f;
            DrawSourceOption(new Rect(inner.x, y, inner.width, control), -1, SimTranslation.T("RSMF.QuickGoodsRegister.AllSources"), candidates.Count);
            y += control + 6f;
            string key = sourceSearch.Trim();
            List<int> indices = Enumerable.Range(0, sources.Count).Where(i => Contains(SourceName(sources[i].Key), key)
                || Contains(sources[i].Key?.PackageId, key)).ToList();
            Rect outRect = new Rect(inner.x, y, inner.width, inner.yMax - y);
            float stride = control + 4f;
            Rect view = new Rect(0f, 0f, outRect.width - 16f, indices.Count * stride);
            Widgets.BeginScrollView(outRect, ref sourceScroll, view);
            for (int i = 0; i < indices.Count; i++)
            {
                Rect row = new Rect(0f, i * stride, view.width, control);
                if (row.yMax < sourceScroll.y || row.y > sourceScroll.y + outRect.height) continue;
                int index = indices[i];
                DrawSourceOption(row, index, SourceName(sources[index].Key), sources[index].Count());
            }
            Widgets.EndScrollView();
        }

        //显示来源及候选数量，并刷新右侧列表。
        private void DrawSourceOption(Rect rect, int index, string name, int count)
        {
            string label = name + " (" + count + ")";
            TooltipHandler.TipRegion(rect, label);
            if (ShopUiVisualUtility.DrawTabButton(rect, label, selectedSource == index, ShopUiVisualUtility.MutedText))
            {
                selectedSource = index;
                RefreshItems();
            }
        }

        //只绘制可见商品行，显示图标、名称、来源和选择按钮。
        private void DrawItems(Rect rect)
        {
            ShopUiVisualUtility.DrawSection(rect);
            if (shown.Count == 0)
            {
                ShopUiVisualUtility.DrawCellLabel(rect.ContractedBy(12f), SimTranslation.T("RSMF.QuickGoodsRegister.NoMatch"));
                return;
            }
            float line = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float control = ShopUiVisualUtility.ControlHeight();
            float height = Mathf.Max(60f, line * 2f + 16f);
            float stride = height + 5f;
            Rect view = new Rect(0f, 0f, rect.width - 16f, shown.Count * stride);
            Widgets.BeginScrollView(rect, ref itemScroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                Rect row = new Rect(0f, i * stride, view.width, height);
                if (row.yMax < itemScroll.y || row.y > itemScroll.y + rect.height) continue;
                ThingDef def = shown[i];
                bool isSelected = selected.Contains(def);
                ShopUiVisualUtility.DrawTableRowBackground(row, i, isSelected);
                Widgets.ThingIcon(new Rect(row.x + 8f, row.center.y - 20f, 40f, 40f), def);
                ShopUiVisualUtility.DrawCellLabel(new Rect(row.x + 58f, row.y + 6f, row.width - 164f, line), def.LabelCap);
                ShopUiVisualUtility.DrawCellLabel(new Rect(row.x + 58f, row.y + line + 8f, row.width - 164f, line),
                    SourceName(def.modContentPack) + " · " + def.defName, ShopUiVisualUtility.MutedText);
                if (SimUiStyle.DrawPrimaryButton(new Rect(row.xMax - 96f, row.center.y - control / 2f, 88f, control),
                    SimTranslation.T(isSelected ? "RSMF.QuickGoodsRegister.Selected" : "RSMF.QuickGoodsRegister.Select")))
                {
                    if (isSelected) selected.Remove(def);
                    else selected.Add(def);
                }
            }
            Widgets.EndScrollView();
        }

        //重建当前筛选结果并回到列表顶部。
        private void RefreshItems()
        {
            string key = search.Trim();
            shown = candidates.Where(def => (selectedSource < 0 || def.modContentPack == sources[selectedSource].Key)
                && (Contains(def.label, key) || Contains(def.defName, key) || Contains(SourceName(def.modContentPack), key))).ToList();
            itemScroll = Vector2.zero;
        }

        //搜索时忽略大小写，空搜索词保留全部结果。
        private static bool Contains(string value, string key) => key.Length == 0 || (value?.IndexOf(key, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

        //显示物品所属模组的名称。
        private static string SourceName(ModContentPack source) => source?.Name ?? SimTranslation.T("RSMF.QuickGoodsRegister.UnknownSource");
    }
}
