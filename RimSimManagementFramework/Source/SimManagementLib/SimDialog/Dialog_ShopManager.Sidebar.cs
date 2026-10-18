using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using SimManagementLib.Api;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        private void DrawSearchBar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.15f));
            float fieldH = 24f;
            float fieldY = rect.y + (rect.height - fieldH) / 2f;

            GUI.color = CTextDim;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 8f, fieldY, 18f, fieldH), "");
            GUI.color = Color.white;

            Rect inputRect = new Rect(rect.x + 28f, fieldY, 210f, fieldH);
            searchQuery = Widgets.TextField(inputRect, searchQuery);

            if (string.IsNullOrEmpty(searchQuery))
            {
                GUI.color = CTextDim;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(inputRect.x + 4f, inputRect.y + 2f, inputRect.width, inputRect.height), SimTranslation.T("RSMF.ShopManager.SearchPlaceholder"));
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                if (SimUiStyle.DrawSecondaryButton(new Rect(inputRect.xMax + 6f, fieldY, 48f, fieldH), SimTranslation.T("RSMF.ShopManager.ClearSearch"), true, GameFont.Tiny))
                    searchQuery = "";
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
        }

        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, CSideBg);

            Rect nameplate = new Rect(rect.x, rect.y, rect.width, 52f);
            Widgets.DrawBoxSolid(nameplate, new Color(0f, 0f, 0f, 0.3f));
            Widgets.DrawBoxSolid(new Rect(rect.x, nameplate.y, 3f, nameplate.height), CAccent);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, nameplate.y + 2f, rect.width - 14f, 26f), shopZone.label.Truncate(rect.width - 14f));

            int enabledCount = ShopDataUtility.GetAllSellableGoods(shopZone).Count;

            Text.Font = GameFont.Tiny;
            GUI.color = CAccent;
            int enabledServiceCount = 0;
            foreach (List<ServiceSlotData> slots in draftServiceData.Values)
            {
                if (slots == null) continue;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != null && slots[i].enabled) enabledServiceCount++;
                }
            }
            Widgets.Label(new Rect(rect.x + 10f, nameplate.y + 28f, rect.width - 14f, 20f), SimTranslation.T("RSMF.ShopManager.GoodsServiceCount",
                enabledCount.Named("goods"),
                enabledServiceCount.Named("services")));
            GUI.color = Color.white;
            Widgets.DrawLineHorizontal(rect.x, nameplate.yMax, rect.width);

            Rect outRect = new Rect(rect.x, nameplate.yMax + 4f, rect.width, rect.height - nameplate.height - 4f);
            float itemH = Mathf.Max(40f, Text.LineHeightOf(GameFont.Tiny) + 18f);
            List<ShopUiNavigationItem> navigationItems = BuildSidebarNavigationItems();
            Rect viewRect = new Rect(0f, 0f, rect.width - ScrW, navigationItems.Count * itemH + CountNavigationGroups(navigationItems) * 11f + 8f);

            Widgets.BeginScrollView(outRect, ref sideScroll, viewRect);
            float y = 4f;

            for (int i = 0; i < navigationItems.Count; i++)
            {
                ShopUiNavigationItem item = navigationItems[i];
                if (item == null) continue;
                if (item.startsNewGroup && y > 4f)
                {
                    y += 6f;
                    Widgets.DrawBoxSolid(new Rect(0f, y, viewRect.width, 1f), CDivider);
                    y += 4f;
                }

                ShopUiNavigationItem localItem = item;
                DrawSidebarItem(new Rect(0f, y, viewRect.width, itemH), localItem.label, localItem.IsSelected(uiContext), delegate { localItem.Activate(uiContext); });
                y += itemH;
            }

            Widgets.EndScrollView();
            ResetText();
        }

        /// <summary>
        /// 构建侧栏导航项，负责让页面 Worker 统一提供静态页面和运行时入口。
        /// </summary>
        private List<ShopUiNavigationItem> BuildSidebarNavigationItems()
        {
            List<ShopUiNavigationItem> items = new List<ShopUiNavigationItem>();
            for (int i = 0; i < uiPages.Count; i++)
            {
                ShopUiPageDef page = uiPages[i];
                if (page == null) continue;
                SimShopUiApi.SafeInvoke(page, uiContext, "BuildNavigationItems", worker =>
                {
                    IEnumerable<ShopUiNavigationItem> produced = worker?.BuildNavigationItems(uiContext);
                    if (produced == null) return;
                    foreach (ShopUiNavigationItem item in produced)
                    {
                        if (item == null) continue;
                        item.pageDef = item.pageDef ?? page;
                        if (string.IsNullOrEmpty(item.label))
                            item.label = BuildSidebarPageLabel(page);
                        items.Add(item);
                    }
                });
            }

            return items
                .OrderBy(item => item.order)
                .ThenBy(item => item.id)
                .ToList();
        }

        /// <summary>
        /// 统计导航分组数量，负责给滚动视图预留分隔线高度。
        /// </summary>
        private static int CountNavigationGroups(List<ShopUiNavigationItem> items)
        {
            if (items.NullOrEmpty()) return 0;
            int count = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i]?.startsNewGroup == true && i > 0)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 返回侧栏页面显示名称，负责给内置页面补回旧 UI 的翻译文本。
        /// </summary>
        private string BuildSidebarPageLabel(ShopUiPageDef page)
        {
            if (page == null) return "";
            if (page.defName == PageOverview) return SimTranslation.T("RSMF.ShopManager.Sidebar.Overview");
            if (page.defName == PageBusinessHours) return SimTranslation.T("RSMF.ShopManager.Sidebar.Schedule");
            if (page.defName == PageManageServices) return SimTranslation.T("RSMF.ShopManager.Sidebar.Services");
            if (page.defName == PageComboEdit) return page.DisplayLabel;
            return page.DisplayLabel;
        }

        private void DrawSidebarItem(Rect rect, string label, bool isCurrent, System.Action action)
        {
            if (isCurrent) Widgets.DrawBoxSolid(rect, CSideSel);
            else if (Mouse.IsOver(rect)) Widgets.DrawBoxSolid(rect, CSideHov);

            if (isCurrent) Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 5f, 3f, rect.height - 10f), CAccent);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = isCurrent ? Color.white : CTextMid;

            string display = label.Truncate(rect.width - 20f);
            Widgets.Label(new Rect(rect.x + 14f, rect.y, rect.width - 18f, rect.height), display);
            if (display != label)
                TooltipHandler.TipRegion(rect, label);

            if (!isCurrent && Widgets.ButtonInvisible(rect)) action();
            ResetText();
        }
    }
}
