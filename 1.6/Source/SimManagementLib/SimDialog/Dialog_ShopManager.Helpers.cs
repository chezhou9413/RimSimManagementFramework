using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
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

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextDim;
            Widgets.Label(new Rect(rect.x + 12f, btnY, 420f, 30f), SimTranslation.T("RSMF.ShopManager.SaveTip"));
            ResetText();

            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 230f, btnY, 100f, 30f), SimTranslation.T("RSMF.ShopManager.Cancel"), true, GameFont.Tiny))
            {
                Close();
            }

            Rect saveRect = new Rect(rect.xMax - 120f, btnY, 108f, 30f);
            if (SimUiStyle.DrawPrimaryButton(saveRect, SimTranslation.T("RSMF.ShopManager.Save"), true, GameFont.Tiny))
            {
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

                Close();
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
    }
}
