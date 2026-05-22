using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
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
            Widgets.Label(new Rect(rect.x + 8f, fieldY, 18f, fieldH), "🔍");
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
            float itemH = 36f;
            Rect viewRect = new Rect(0f, 0f, rect.width - ScrW, (6 + storages.Count + zoneCombos.Count) * itemH + 30f);

            Widgets.BeginScrollView(outRect, ref sideScroll, viewRect);
            float y = 4f;

            DrawSidebarItem(new Rect(0f, y, viewRect.width, itemH), SimTranslation.T("RSMF.ShopManager.Sidebar.Overview"), curMenu == MenuType.Overview, delegate { SwitchMenu(MenuType.Overview); });
            y += itemH;

            DrawSidebarItem(new Rect(0f, y, viewRect.width, itemH), SimTranslation.T("RSMF.ShopManager.Sidebar.Schedule"), curMenu == MenuType.BusinessHours, delegate { SwitchMenu(MenuType.BusinessHours); });
            y += itemH;

            DrawSidebarItem(new Rect(0f, y, viewRect.width, itemH), SimTranslation.T("RSMF.ShopManager.Sidebar.Goods"), curMenu == MenuType.ManageGoods, delegate { SwitchMenu(MenuType.ManageGoods); });
            y += itemH;

            DrawSidebarItem(new Rect(0f, y, viewRect.width, itemH), SimTranslation.T("RSMF.ShopManager.Sidebar.Services"), curMenu == MenuType.ManageServices, delegate { SwitchMenu(MenuType.ManageServices); });
            y += itemH + 6f;

            Widgets.DrawBoxSolid(new Rect(0f, y, viewRect.width, 1f), CDivider);
            y += 4f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextDim;
            Widgets.Label(new Rect(10f, y, viewRect.width - 10f, 18f), SimTranslation.TOrFallback("RSMF.ShopManager.StorageHeader", $"货柜 ({storages.Count})"));
            y += 20f;

            for (int i = 0; i < storages.Count; i++)
            {
                Building_SimContainer storage = storages[i];
                if (storage == null) continue;

                bool isSelected = storage.thingIDNumber == selectedStorageThingId;
                string label = storage.StorageDisplayLabel;
                DrawSidebarItem(new Rect(0f, y, viewRect.width, itemH), "📦  " + label, isSelected, delegate
                {
                    selectedStorageThingId = storage.thingIDNumber;
                    if (curMenu != MenuType.ManageGoods)
                    {
                        curMenu = MenuType.ManageGoods;
                        curCombo = null;
                    }
                    listScroll = Vector2.zero;
                });
                y += itemH;
            }

            Widgets.DrawBoxSolid(new Rect(0f, y, viewRect.width, 1f), CDivider);
            y += 4f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextDim;
            Widgets.Label(new Rect(10f, y, viewRect.width - 10f, 18f), SimTranslation.T("RSMF.ShopManager.CombosHeader", zoneCombos.Count.Named("count")));
            y += 20f;

            foreach (ComboData combo in zoneCombos)
            {
                bool isCurrent = curMenu == MenuType.ComboEdit && curCombo == combo;
                ComboData localCombo = combo;
                DrawSidebarItem(new Rect(0f, y, viewRect.width, itemH), "🍔  " + localCombo.comboName, isCurrent, delegate
                {
                    curMenu = MenuType.ComboEdit;
                    curCombo = localCombo;
                    listScroll = Vector2.zero;
                    comboPriceBuf = localCombo.totalPrice.ToString("F0");
                });
                y += itemH;
            }

            y += 4f;
            Rect newRect = new Rect(8f, y, viewRect.width - 16f, itemH - 4f);
            if (SimUiStyle.DrawPrimaryButton(newRect, SimTranslation.T("RSMF.ShopManager.NewCombo"), true, GameFont.Tiny))
            {
                ComboData newCombo = new ComboData();
                zoneCombos.Add(newCombo);
                curCombo = newCombo;
                curMenu = MenuType.ComboEdit;
                listScroll = Vector2.zero;
                comboPriceBuf = "0";
            }

            Widgets.EndScrollView();
            ResetText();
        }

        private void SwitchMenu(MenuType menu)
        {
            curMenu = menu;
            curCombo = null;
            listScroll = Vector2.zero;
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
