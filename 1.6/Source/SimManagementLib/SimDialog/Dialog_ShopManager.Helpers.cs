using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        private void DrawBottomBar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
            float btnY = rect.y + (rect.height - 30f) / 2f;
            ShopUiPageDef currentPage = GetCurrentUiPage();
            bool showSave = CurrentPageShowsSaveButton(currentPage);
            string tip = GetCurrentPageSaveTip(currentPage);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextDim;
            float reservedRight = showSave ? 260f : 150f;
            Rect tipRect = new Rect(rect.x + 12f, btnY, Mathf.Max(80f, rect.width - reservedRight), 30f);
            Widgets.Label(tipRect, tip.Truncate(tipRect.width));
            ResetText();

            float cancelX = showSave ? rect.xMax - 230f : rect.xMax - 120f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(cancelX, btnY, 100f, 30f), SimTranslation.T("RSMF.ShopManager.Cancel"), true, GameFont.Tiny))
            {
                NotifyAllPagesCancel();
                Close();
            }

            if (!showSave)
                return;

            Rect saveRect = new Rect(rect.xMax - 120f, btnY, 108f, 30f);
            if (SimUiStyle.DrawPrimaryButton(saveRect, GetCurrentPageSaveLabel(currentPage), true, GameFont.Tiny))
            {
                ApiSaveDrafts(closeAfterSave: true);
            }
        }

        /// <summary>
        /// 判断当前页面是否显示底部保存按钮，负责把按钮可见性委托给页面 Worker。
        /// </summary>
        private bool CurrentPageShowsSaveButton(ShopUiPageDef page)
        {
            bool show = true;
            if (page == null) return false;
            SimManagementLib.Api.SimShopUiApi.SafeInvoke(page, uiContext, "ShowSaveButton", worker => show = worker?.ShowSaveButton(uiContext) != false);
            return show;
        }

        /// <summary>
        /// 返回当前页面保存按钮文本，负责按页面语义显示提交动作。
        /// </summary>
        private string GetCurrentPageSaveLabel(ShopUiPageDef page)
        {
            string label = SimTranslation.T("RSMF.ShopManager.Save");
            if (page == null) return label;
            SimManagementLib.Api.SimShopUiApi.SafeInvoke(page, uiContext, "GetSaveButtonLabel", worker =>
            {
                string workerLabel = worker?.GetSaveButtonLabel(uiContext);
                if (!string.IsNullOrEmpty(workerLabel))
                    label = workerLabel;
            });
            return label;
        }

        /// <summary>
        /// 返回当前页面底部提示，负责让无保存按钮页面说明修改是否即时生效。
        /// </summary>
        private string GetCurrentPageSaveTip(ShopUiPageDef page)
        {
            string tip = SimTranslation.T("RSMF.ShopManager.NoSaveNeededTip");
            if (page == null) return tip;
            SimManagementLib.Api.SimShopUiApi.SafeInvoke(page, uiContext, "GetSaveTip", worker =>
            {
                string workerTip = worker?.GetSaveTip(uiContext);
                if (!string.IsNullOrEmpty(workerTip))
                    tip = workerTip;
            });
            return tip;
        }

        /// <summary>
        /// 保存当前窗口所有草稿并关闭窗口，负责兼容外部代码需要提交后退出的旧入口。
        /// </summary>
        public void ApiSaveDraftsAndClose()
        {
            ApiSaveDrafts(closeAfterSave: true);
        }

        /// <summary>
        /// 保存当前窗口所有草稿，负责统一内置页面和外部页面保存流程。
        /// </summary>
        public void ApiSaveDrafts(bool closeAfterSave)
        {
            NotifyAllPagesSave();

            foreach (Thing provider in serviceProviders)
            {
                if (provider == null || provider.Destroyed) continue;
                ThingComp_ServiceProvider comp = ShopServiceUtility.GetProviderComp(provider);
                if (comp == null) continue;
                if (!draftServiceData.TryGetValue(provider.thingIDNumber, out List<ServiceSlotData> drafts)) continue;

                comp.serviceSlots = drafts
                    .Where(s => s != null)
                    .Select(s => new ServiceSlotData
                    {
                        serviceDefName = s.serviceDefName,
                        enabled = s.enabled,
                        priceOverride = Mathf.Max(0f, s.priceOverride),
                        maxSimultaneousUsers = Mathf.Max(1, s.maxSimultaneousUsers)
                    })
                    .ToList();
            }

            shopZone.ApplySchedule(draftSchedule);

            Messages.Message(SimTranslation.T("RSMF.ShopManager.SaveSuccess"), MessageTypeDefOf.PositiveEvent, false);

            if (closeAfterSave)
                Close();
        }

        /// <summary>
        /// 通知所有页面保存，负责让外部表单页参与保存按钮生命周期。
        /// </summary>
        private void NotifyAllPagesSave()
        {
            for (int i = 0; i < uiPages.Count; i++)
            {
                SimManagementLib.Api.SimShopUiApi.SafeInvoke(uiPages[i], uiContext, "OnSave", worker => worker?.OnSave(uiContext));
            }
        }

        /// <summary>
        /// 通知所有页面取消，负责让外部表单页清理未保存草稿。
        /// </summary>
        private void NotifyAllPagesCancel()
        {
            for (int i = 0; i < uiPages.Count; i++)
            {
                SimManagementLib.Api.SimShopUiApi.SafeInvoke(uiPages[i], uiContext, "OnCancel", worker => worker?.OnCancel(uiContext));
            }
        }

        /// <summary>
        /// 判断店内是否存在任意一个正在售卖指定商品的货柜，负责给套餐编辑页过滤可选商品。
        /// </summary>
        private bool HasAnyStorageSellingThing(ThingDef thingDef)
        {
            if (thingDef == null || storages.NullOrEmpty())
                return false;

            foreach (Building_SimContainer storage in storages)
            {
                if (storage == null || storage.Destroyed) continue;
                ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                if (comp == null) continue;
                GoodsItemData data = comp.FindItemData(thingDef);
                if (data != null && data.enabled && data.count > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 返回套餐估价时参考的单件售价，负责优先使用当前选中货柜的配置价，再回退到其他货柜或物品基础市价。
        /// </summary>
        private float GetReferencePriceForCombo(ThingDef thingDef)
        {
            if (thingDef == null) return 1f;

            Building_SimContainer selectedStorage = GetSelectedStorage();
            if (TryGetConfiguredPrice(selectedStorage, thingDef, out float selectedPrice))
                return selectedPrice;

            for (int i = 0; i < storages.Count; i++)
            {
                if (TryGetConfiguredPrice(storages[i], thingDef, out float price))
                    return price;
            }

            return Mathf.Max(1f, thingDef.BaseMarketValue);
        }

        /// <summary>
        /// 读取指定货柜对某个商品的配置售价，负责给套餐页和总管概览复用同一套判断。
        /// </summary>
        private static bool TryGetConfiguredPrice(Building_SimContainer storage, ThingDef thingDef, out float price)
        {
            price = 0f;
            if (storage == null || thingDef == null) return false;

            ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
            GoodsItemData data = comp?.FindItemData(thingDef);
            if (data == null || !data.enabled || data.price <= 0f)
                return false;

            price = Mathf.Max(1f, data.price);
            return true;
        }

        private void DrawTableHeader(Rect rect, System.Action drawColumns)
        {
            Widgets.DrawBoxSolid(rect, CHeaderBg);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), CDivider);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextMid;
            drawColumns();
            ResetText();
        }

        /// <summary>
        /// 绘制固定行高的虚拟滚动列表，负责只绘制当前视口附近的行来降低大列表开销。
        /// </summary>
        private void DrawVirtualizedRows(Rect outRect, int count, System.Action<int, Rect> drawRow)
        {
            float viewWidth = outRect.width - ScrW;
            float viewHeight = Mathf.Max(outRect.height, count * RowH);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(outRect, ref listScroll, viewRect);
            ClampSharedListScroll(viewHeight, outRect.height);

            int firstIndex = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / RowH) - 1);
            int lastIndex = Mathf.Min(count - 1, Mathf.CeilToInt((listScroll.y + outRect.height) / RowH) + 1);
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                drawRow(i, new Rect(0f, i * RowH, viewWidth, RowH));
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 限制共享列表滚动位置，负责在搜索或切页导致列表变短时避免停留在空白区域。
        /// </summary>
        private void ClampSharedListScroll(float viewHeight, float outerHeight)
        {
            float maxY = Mathf.Max(0f, viewHeight - outerHeight);
            listScroll.y = Mathf.Clamp(listScroll.y, 0f, maxY);
            listScroll.x = 0f;
        }

        private void DrawHdrLabel(Rect rect, string text, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
        }

        private void DrawRowBg(Rect row, int index, bool highlighted)
        {
            if (highlighted) Widgets.DrawBoxSolid(row, CCheckedBg);
            else if (index % 2 == 1) Widgets.DrawBoxSolid(row, CRowAlt);
        }

        private void DrawFieldLabel(Rect rect, string text)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextDim;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
        }

        private void DrawDash(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.25f, 0.25f, 0.25f);
            Widgets.Label(rect, "—");
            ResetText();
        }

        private void DrawBorderRect(Rect rect, Color color, float thickness)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private bool MatchSearch(string label)
        {
            return string.IsNullOrEmpty(searchQuery)
                || label.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 保存当前套餐并打开一个新套餐，负责支持连续录入多个套餐。
        /// </summary>
        private void SaveCurrentComboAndCreateNext()
        {
            if (curCombo == null) return;
            EnsureComboHasName(curCombo);
            Messages.Message(SimTranslation.T("RSMF.ShopManager.ComboSavedAndNew", curCombo.comboName.Named("comboName")), MessageTypeDefOf.TaskCompletion, false);
            ApiCreateCombo();
        }

        /// <summary>
        /// 确保套餐拥有可显示名称，负责在玩家留空时按当前商品内容自动生成随机名称。
        /// </summary>
        private static void EnsureComboHasName(ComboData combo)
        {
            if (combo == null) return;
            if (!string.IsNullOrWhiteSpace(combo.comboName)) return;
            combo.comboName = ComboNameGenerator.GenerateName(combo);
        }
    }
}
