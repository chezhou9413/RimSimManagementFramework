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
                HashSet<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(shopZone);
                int totalTrimmed = 0;
                int trimmedStorageCount = 0;
                ApplyDraftGoodsSettingsToStorages(storages, ref totalTrimmed, ref trimmedStorageCount);

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

                if (totalTrimmed > 0)
                {
                    Messages.Message(SimTranslation.T("RSMF.ShopManager.AutoTrimNotice",
                        trimmedStorageCount.Named("storageCount"),
                        totalTrimmed.Named("trimmed")), MessageTypeDefOf.NeutralEvent, false);
                }

                Close();
            }
        }

        /// <summary>
        /// 将商店级货品草稿写回店内货柜，负责兼容已有分类货柜和未配置分类的空货柜。
        /// </summary>
        private void ApplyDraftGoodsSettingsToStorages(HashSet<Building_SimContainer> storages, ref int totalTrimmed, ref int trimmedStorageCount)
        {
            if (storages.NullOrEmpty()) return;

            foreach (Building_SimContainer storage in storages)
            {
                if (storage == null || storage.Destroyed) continue;
                ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                if (comp == null) continue;

                string categoryId = ResolveStorageCategoryForDraft(comp);
                if (string.IsNullOrEmpty(categoryId)) continue;

                Dictionary<string, GoodsItemData> settingsCopy = CloneDraftItemSettings();
                Dictionary<string, GoodsItemData> clamped = storage.ClampSettingsToCapacity(categoryId, settingsCopy, out int trimmed);
                if (trimmed > 0)
                {
                    totalTrimmed += trimmed;
                    trimmedStorageCount++;
                }

                comp.ApplySettings(categoryId, clamped);
            }
        }

        /// <summary>
        /// 为单个货柜选择商店级草稿应写入的商品分类，负责让已有货柜保持原分类并给空货柜选择目标量最高的可用分类。
        /// </summary>
        private string ResolveStorageCategoryForDraft(ThingComp_GoodsData comp)
        {
            if (comp == null) return "";
            if (!string.IsNullOrEmpty(comp.ActiveGoodsDefName) && comp.AllowsGoodsCategory(comp.ActiveGoodsDefName))
                return comp.ActiveGoodsDefName;

            return manageableGoodsCategoryIds
                .Where(comp.AllowsGoodsCategory)
                .Select(categoryId => new
                {
                    CategoryId = categoryId,
                    Target = GetDraftTargetTotalForCategory(categoryId)
                })
                .Where(entry => entry.Target > 0)
                .OrderByDescending(entry => entry.Target)
                .ThenBy(entry => GoodsCatalog.GetCategory(entry.CategoryId)?.label ?? entry.CategoryId)
                .Select(entry => entry.CategoryId)
                .FirstOrDefault() ?? "";
        }

        /// <summary>
        /// 复制商店级货品草稿，负责避免同一份 GoodsItemData 被多个货柜共享引用。
        /// </summary>
        private Dictionary<string, GoodsItemData> CloneDraftItemSettings()
        {
            return draftItemData.ToDictionary(
                kv => kv.Key,
                kv => new GoodsItemData
                {
                    enabled = kv.Value.enabled,
                    count = kv.Value.count,
                    price = kv.Value.price
                });
        }

        /// <summary>
        /// 统计指定分类在草稿中的目标数量，负责给未配置货柜自动选择写入分类。
        /// </summary>
        private int GetDraftTargetTotalForCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return 0;

            int total = 0;
            IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(categoryId);
            for (int i = 0; i < items.Count; i++)
            {
                ThingDef thingDef = items[i]?.thingDef;
                if (thingDef == null) continue;
                if (!draftItemData.TryGetValue(thingDef.defName, out GoodsItemData data) || data == null) continue;
                if (!data.enabled || data.count <= 0) continue;
                total += data.count;
            }

            return total;
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
