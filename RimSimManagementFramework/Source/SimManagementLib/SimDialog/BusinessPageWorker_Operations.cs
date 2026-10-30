using System;
using System.Collections.Generic;
using System.Linq;
using SimManagementLib.Api;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //把店铺列表与扩展运营汇总放进同一入口，具体设置仍由单店窗口处理。
    public sealed class BusinessPageWorker_Operations : BusinessManagerPageWorker
    {
        //分类、搜索和刷新时钟属于窗口，避免共享 Worker 串用界面状态。
        private sealed class State
        {
            public List<ShopUiPageDef> pages = new List<ShopUiPageDef>();
            public readonly Dictionary<string, string> searches = new Dictionary<string, string>();
            public string selected = "";
            public string opened;
            public long nextRefresh;
        }

        //取得当前窗口的运营分类状态。
        private static State GetState(ShopUiContext context)
            => context.GetOrCreatePageState("RSMF.Operations", () => new State());

        //重新打开总览时立即核对设施，暂停游戏也能刷新分类。
        public override void OnOpen(ShopUiContext context)
        {
            State state = GetState(context);
            state.nextRefresh = 0;
            state.opened = null;
        }

        //显式刷新不清除当前分类和搜索词。
        public override void OnRefresh(ShopUiContext context) => GetState(context).nextRefresh = 0;

        //总览只读并提供跳转，不需要保存按钮。
        public override bool ShowSaveButton(ShopUiContext context) => false;

        //先留出分类和搜索栏，子页沿用原来的数据、明细与跳转操作。
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            State state = GetState(context);
            ShopUiPageDef parentPage = context.PageDef;
            string parentName = context.CurrentPageDefName;
            string parentSearch = context.SearchText;
            GameFont oldFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                RefreshCategories(context, state);
                context.PageDef = parentPage;
                Rect body = DrawNavigation(rect, state);
                context.SearchText = state.searches[state.selected];
                if (body.height <= 0f) return;
                ShopUiPageDef page = state.pages.FirstOrDefault(p => p.defName == state.selected);
                if (page == null)
                {
                    context.Window.ApiDrawShopManagementPage(body);
                    return;
                }

                context.CurrentPageDefName = page.defName;
                if (state.opened != page.defName)
                {
                    SimShopUiApi.SafeInvoke(page, context, "OnOpen", worker => worker.OnOpen(context));
                    state.opened = page.defName;
                }
                SimShopUiApi.SafeInvoke(page, context, "DrawPage", worker => worker.DrawPage(body, context));
            }
            finally
            {
                context.PageDef = parentPage;
                context.CurrentPageDefName = parentName;
                context.SearchText = parentSearch;
                Text.Font = oldFont;
            }
        }

        //按实际业务条件发现分类，设施消失时回到全部店铺，搜索不参与可见性判断。
        private static void RefreshCategories(BusinessManagerUiContext context, State state)
        {
            long now = Environment.TickCount64;
            if (now < state.nextRefresh && !context.RefreshRequested) return;
            state.nextRefresh = now + 1000;
            state.pages = SimShopUiApi.GetPages(ShopUiPageScope.BusinessManager, context)
                .Where(page => page.isOperationsPage && page.showInNavigation).ToList();
            if (state.selected.Length > 0 && !state.pages.Any(page => page.defName == state.selected))
            {
                state.selected = "";
                state.opened = null;
            }
            context.ClearRefreshRequest();
        }

        //宽窗口让搜索与分类同行，窄窗口按实际文字宽度换行。
        private static Rect DrawNavigation(Rect rect, State state)
        {
            float line = ShopUiVisualUtility.ControlHeight();
            var labels = new List<string> { SimTranslation.T("RSMF.Business.Operations.All") };
            labels.AddRange(state.pages.Select(page => page.DisplayLabel));
            float[] widths = labels.Select(label => Mathf.Min(rect.width, Mathf.Clamp(Text.CalcSize(label).x + 24f, 80f, 220f))).ToArray();
            float total = widths.Sum() + (labels.Count - 1) * 6f;
            bool inlineSearch = total + 296f <= rect.width;
            float searchWidth = inlineSearch ? Mathf.Min(420f, rect.width - total - 16f) : rect.width;
            float navWidth = inlineSearch ? rect.width - searchWidth - 16f : rect.width;
            float x = rect.x, y = rect.y;
            for (int i = 0; i < labels.Count; i++)
            {
                if (x > rect.x && x + widths[i] > rect.x + navWidth)
                { x = rect.x; y += line + 6f; }
                string key = i == 0 ? "" : state.pages[i - 1].defName;
                Rect tab = new Rect(x, y, widths[i], line);
                if (ShopUiVisualUtility.DrawTabButton(tab, labels[i], state.selected == key, ShopUiVisualUtility.MutedText)
                    && state.selected != key)
                {
                    state.selected = key;
                    state.opened = null;
                }
                TooltipHandler.TipRegion(tab, labels[i]);
                x += widths[i] + 6f;
            }

            Rect search = inlineSearch ? new Rect(rect.xMax - searchWidth, rect.y, searchWidth, line)
                : new Rect(rect.x, y + line + 6f, rect.width, line);
            state.searches.TryGetValue(state.selected, out string text);
            state.searches[state.selected] = ShopUiVisualUtility.DrawSearchField(search, text,
                SimTranslation.T("RSMF.Business.Operations.SearchHint"));
            float bodyY = Mathf.Max(y + line, search.yMax) + 12f;
            Widgets.DrawBoxSolid(new Rect(rect.x, bodyY - 6f, rect.width, 1f), ShopUiVisualUtility.Divider);
            return new Rect(rect.x, bodyY, rect.width, Mathf.Max(0f, rect.yMax - bodyY));
        }
    }
}
