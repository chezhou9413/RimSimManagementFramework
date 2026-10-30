using System.Collections.Generic;
using System.Linq;
using SimManagementLib.Api;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //将符合当前店铺条件的服务页收进同一个入口，业务回调仍使用原页面。
    public partial class Dialog_ShopManager
    {
        private readonly Dictionary<string, ShopUiPageDef> openedServicePages = new Dictionary<string, ShopUiPageDef>();
        private readonly Dictionary<string, ServicePageView> servicePageViews = new Dictionary<string, ServicePageView>();
        private string lastServicePage = "";
        private string lastVisibleServiceTab = "";
        private Vector2 serviceTabScroll;
        private Vector2 serviceTabLayout;
        private long nextPageVisibilityCheck;
        private bool closingAfterSave;
        private bool cancelNotified;

        //搜索和通用列表滚动按子页保留，业务草稿继续由上下文持有。
        private sealed class ServicePageView
        {
            public string search = "";
            public Vector2 listScroll;
            public Vector2 contextScroll;
        }

        //只列出可见且允许导航的服务页，沿用 Def 顺序。
        private List<ShopUiPageDef> GetServicePages() => uiPages.Where(p => p.isServicePage && p.showInNavigation).ToList();

        //服务总入口返回上次选择；对应设施移除时改选第一个可用服务。
        private void OpenServicePages()
        {
            var pages = GetServicePages();
            ShopUiPageDef page = pages.FirstOrDefault(p => p.defName == lastServicePage) ?? pages.FirstOrDefault();
            if (page != null) SwitchPage(page.defName);
        }

        //离开当前服务前记住搜索和滚动，切页不触碰任何配置。
        private void RememberServiceView()
        {
            ShopUiPageDef page = GetCurrentUiPage();
            if (page?.isServicePage != true) return;
            servicePageViews[page.defName] = new ServicePageView {
                search = searchQuery, listScroll = listScroll, contextScroll = uiContext.ScrollPosition
            };
        }

        //恢复该服务上次浏览的位置，新服务使用空搜索。
        private void RestoreServiceView(ShopUiPageDef page)
        {
            if (!servicePageViews.TryGetValue(page.defName, out ServicePageView view)) view = new ServicePageView();
            searchQuery = view.search;
            listScroll = view.listScroll;
            uiContext.ScrollPosition = view.contextScroll;
            lastServicePage = page.defName;
        }

        //已访问但暂时隐藏的服务仍参与保存和取消，设施变化不会丢弃窗口草稿。
        private IEnumerable<ShopUiPageDef> LifecyclePages() => uiPages.Concat(openedServicePages.Values)
            .GroupBy(p => p.defName).Select(group => group.First());

        //新建服务设施补入列表，只为尚无草稿的设施复制配置。
        private void RefreshServiceProviders()
        {
            serviceProviders = ShopServiceUtility.GetServiceProvidersInZone(shopZone).ToList();
            foreach (Thing provider in serviceProviders)
            {
                ThingComp_ServiceProvider comp = ShopServiceUtility.GetProviderComp(provider);
                if (comp == null || draftServiceData.ContainsKey(provider.thingIDNumber)) continue;
                comp.EnsureDefaultSlots();
                draftServiceData[provider.thingIDNumber] = comp.serviceSlots.Where(s => s != null)
                    .Select(s => new ServiceSlotData {
                        serviceDefName = s.serviceDefName, enabled = s.enabled,
                        priceOverrideEnabled = s.priceOverrideEnabled, priceOverride = s.priceOverride,
                        maxSimultaneousUsers = s.maxSimultaneousUsers
                    }).ToList();
            }
        }

        //子页签最多占两行，更多服务在独立导航滚动区选择。
        private Rect DrawServiceTabs(Rect rect)
        {
            var pages = GetServicePages();
            if (pages.Count == 0 || rect.width <= 0f || rect.height <= 0f) return rect;
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
                float line = ShopUiVisualUtility.ControlHeight();
                float width = Mathf.Max(1f, rect.width - 18f);
                if (serviceTabLayout.x != width || serviceTabLayout.y != line)
                {
                    serviceTabLayout = new Vector2(width, line);
                    lastVisibleServiceTab = "";
                }
                var tabs = new List<Rect>();
                float x = 0f, y = 0f;
                foreach (ShopUiPageDef page in pages)
                {
                    float tabWidth = Mathf.Min(width, Mathf.Clamp(Text.CalcSize(page.DisplayLabel).x + 28f, 100f, 220f));
                    if (x > 0f && x + tabWidth > width) { x = 0f; y += line + 6f; }
                    tabs.Add(new Rect(x, y, tabWidth, line));
                    x += tabWidth + 6f;
                }
                float contentHeight = y + line;
                float height = Mathf.Min(contentHeight, line * 2f + 6f, Mathf.Max(line, rect.height * 0.35f), rect.height);
                int selected = pages.FindIndex(p => p.defName == curPageDefName);
                if (selected >= 0 && lastVisibleServiceTab != curPageDefName)
                {
                    Rect tab = tabs[selected];
                    if (tab.y < serviceTabScroll.y) serviceTabScroll.y = tab.y;
                    else if (tab.yMax > serviceTabScroll.y + height) serviceTabScroll.y = tab.yMax - height;
                    lastVisibleServiceTab = curPageDefName;
                }
                serviceTabScroll.y = Mathf.Clamp(serviceTabScroll.y, 0f, Mathf.Max(0f, contentHeight - height));
                Widgets.BeginScrollView(new Rect(rect.x, rect.y, rect.width, height), ref serviceTabScroll,
                    new Rect(0f, 0f, width, contentHeight));
                try
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        ShopUiPageDef page = pages[i];
                        if (ShopUiVisualUtility.DrawTabButton(tabs[i], page.DisplayLabel, page.defName == curPageDefName, CTextMid))
                            SwitchPage(page.defName);
                        TooltipHandler.TipRegion(tabs[i], page.DisplayLabel +
                            (page.DisplayDescription.NullOrEmpty() ? "" : "\n" + page.DisplayDescription));
                    }
                }
                finally { Widgets.EndScrollView(); }
                float bodyY = rect.y + height + 12f;
                Widgets.DrawBoxSolid(new Rect(rect.x, bodyY - 6f, rect.width, 1f), CDivider);
                return new Rect(rect.x, bodyY, rect.width, Mathf.Max(0f, rect.yMax - bodyY));
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; Text.WordWrap = oldWrap; GUI.color = oldColor; }
        }
    }
}
