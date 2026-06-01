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
        private void DrawOverviewPanel(Rect rect)
        {
            List<ShopItemStatus> items = ShopDataUtility.GetAllSellableGoods(shopZone)
                .Where(t => MatchSearch(t.Def.label))
                .ToList();

            const float statusW = 52f;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderH);
            DrawTableHeader(headerRect, delegate
            {
                float cx = headerRect.xMax - RowPad;
                cx -= statusW;
                DrawHdrLabel(new Rect(cx, headerRect.y, statusW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Header.Status"), TextAnchor.MiddleCenter);
                cx -= ColGap;
                cx -= StockW;
                DrawHdrLabel(new Rect(cx, headerRect.y, StockW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Header.Stock"));
                cx -= ColGap;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Header.Price"));
                cx -= ColGap;
                float lx = headerRect.x + RowPad + IconSz + ColGap;
                DrawHdrLabel(new Rect(lx, headerRect.y, cx - lx - ColGap, headerRect.height), SimTranslation.T("RSMF.ShopManager.Header.Name"));
            });

            Rect outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - HeaderH);
            DrawVirtualizedRows(outRect, items.Count, delegate(int i, Rect row)
            {
                ShopItemStatus item = items[i];
                DrawRowBg(row, i, false);
                Widgets.DrawHighlightIfMouseover(row);
                if (Mouse.IsOver(row) && !string.IsNullOrEmpty(item.Def.description))
                    TooltipHandler.TipRegion(row, item.Def.description);

                float midY = row.y + (RowH - IconSz) / 2f;
                float x = row.x + RowPad;
                Widgets.ThingIcon(new Rect(x, midY, IconSz, IconSz), item.Def);
                x += IconSz + ColGap;

                float rx = row.xMax - RowPad;
                rx -= statusW;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Rect statusRect = new Rect(rx, row.y + (RowH - 18f) / 2f, statusW, 18f);
                if (item.CurrentStock <= 0)
                {
                    Widgets.DrawBoxSolid(statusRect, new Color(0.9f, 0.35f, 0.35f, 0.2f));
                    GUI.color = CStockNo;
                    Widgets.Label(statusRect, SimTranslation.T("RSMF.ShopManager.Status.OutOfStock"));
                }
                else if (item.CurrentStock < item.Config.count)
                {
                    Widgets.DrawBoxSolid(statusRect, new Color(0.95f, 0.72f, 0.25f, 0.15f));
                    GUI.color = CStockLow;
                    Widgets.Label(statusRect, SimTranslation.T("RSMF.ShopManager.Status.Low"));
                }
                else
                {
                    Widgets.DrawBoxSolid(statusRect, new Color(0.35f, 0.80f, 0.45f, 0.15f));
                    GUI.color = CStockOk;
                    Widgets.Label(statusRect, SimTranslation.T("RSMF.ShopManager.Status.Enough"));
                }

                rx -= ColGap;
                rx -= StockW;
                Color stockColor = item.CurrentStock <= 0 ? CStockNo : item.CurrentStock < item.Config.count ? CStockLow : CStockOk;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                GUI.color = stockColor;
                Widgets.Label(new Rect(rx, row.y, 35f, RowH), item.CurrentStock.ToString());
                GUI.color = CTextDim;
                Widgets.Label(new Rect(rx + 35f, row.y, 10f, RowH), "/");
                GUI.color = Color.white;
                Widgets.Label(new Rect(rx + 45f, row.y, StockW - 45f, RowH), item.Config.count.ToString());

                rx -= ColGap;
                rx -= FieldW;
                GUI.color = CGold;
                Widgets.Label(new Rect(rx, row.y, FieldW, RowH), $"¥{item.Config.price:F0}");
                rx -= ColGap;

                GUI.color = Color.white;
                Widgets.Label(new Rect(x, row.y, rx - x, RowH), item.Def.LabelCap.Truncate(rx - x));
                ResetText();
            });
        }

        private void DrawManagePanel(Rect rect)
        {
            Building_SimContainer storage = GetSelectedStorage();
            ThingComp_GoodsData comp = storage?.GetComp<ThingComp_GoodsData>();
            if (storage == null || comp == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = CTextDim;
                Widgets.Label(rect, SimTranslation.T("RSMF.ShopManager.NoStorageSelected"));
                ResetText();
                return;
            }

            string activeCategoryId = !string.IsNullOrEmpty(comp.ActiveGoodsDefName) && comp.AllowsGoodsCategory(comp.ActiveGoodsDefName)
                ? comp.ActiveGoodsDefName
                : GetManageableCategoryIds(comp).FirstOrDefault();
            RuntimeGoodsCategory activeCategory = GoodsCatalog.GetCategory(activeCategoryId);
            List<ThingDef> list = (activeCategory?.Items ?? Enumerable.Empty<RuntimeGoodsItem>())
                .Select(item => item?.thingDef)
                .Where(def => def != null && MatchSearch(def.label))
                .ToList();

            string categoryText = activeCategory != null
                ? activeCategory.label
                : SimTranslation.TOrFallback("RSMF.Common.None", "无");
            string summaryLine = SimTranslation.T("RSMF.ShopManager.StorageSummaryLine",
                categoryText.Named("category"),
                storage.CountTotalStored().Named("stored"),
                storage.MaxTotalCapacity.Named("capacity"));
            string hintLine = SimTranslation.T("RSMF.ShopManager.StorageHintLine");
            GameFont oldMeasureFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float tinyLine = Text.LineHeightOf(GameFont.Tiny) + 2f;
            float hintHeight = Mathf.Max(tinyLine, Text.CalcHeight(hintLine, Mathf.Max(120f, rect.width - 160f)));
            Text.Font = oldMeasureFont;
            float infoHeight = Mathf.Max(72f, 12f + Text.LineHeightOf(GameFont.Small) + tinyLine + hintHeight + 8f);
            Rect infoRect = new Rect(rect.x, rect.y, rect.width, infoHeight);
            Widgets.DrawBoxSolid(infoRect, new Color(0f, 0f, 0f, 0.14f));
            DrawBorderRect(infoRect, CDivider, 1f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(infoRect.x + 10f, infoRect.y + 4f, infoRect.width - 160f, 24f), storage.StorageDisplayLabel);

            Text.Font = GameFont.Tiny;
            GUI.color = CTextDim;
            Widgets.Label(new Rect(infoRect.x + 10f, infoRect.y + 30f, infoRect.width - 160f, tinyLine), summaryLine);
            Widgets.Label(new Rect(infoRect.x + 10f, infoRect.y + 30f + tinyLine, infoRect.width - 160f, hintHeight), hintLine);

            Rect openButtonRect = new Rect(infoRect.xMax - 132f, infoRect.y + 17f, 120f, 30f);
            if (SimUiStyle.DrawPrimaryButton(openButtonRect, SimTranslation.T("RSMF.Gizmo.ContainerManagement.Label"), true, GameFont.Tiny))
                Find.WindowStack.Add(new Dialog_GoodsManager(comp));

            ResetText();

            Rect headerRect = new Rect(rect.x, infoRect.yMax + 6f, rect.width, HeaderH);
            DrawTableHeader(headerRect, delegate
            {
                float cx = headerRect.xMax - RowPad;
                cx -= StockW;
                DrawHdrLabel(new Rect(cx, headerRect.y, StockW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Header.Stock"));
                cx -= ColGap;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), SimTranslation.T("RSMF.ShopManager.TargetPriceHeader"));
                cx -= ColGap;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), SimTranslation.T("RSMF.ShopManager.TargetCountHeader"));
                cx -= ColGap;
                float lx = headerRect.x + RowPad + IconSz + ColGap;
                DrawHdrLabel(new Rect(lx, headerRect.y, cx - lx - ColGap, headerRect.height), SimTranslation.T("RSMF.ShopManager.TargetGoodsHeader"));
            });

            Rect outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - infoRect.height - 6f - HeaderH);
            DrawVirtualizedRows(outRect, list.Count, delegate(int i, Rect row)
            {
                ThingDef thingDef = list[i];
                GoodsItemData config = comp.FindItemData(thingDef);
                if (config == null && comp.itemData.TryGetValue(thingDef.defName, out GoodsItemData rawData))
                    config = rawData;
                bool enabled = config != null && config.enabled;

                DrawRowBg(row, i, enabled);
                Widgets.DrawHighlightIfMouseover(row);
                if (Mouse.IsOver(row) && !string.IsNullOrEmpty(thingDef.description))
                    TooltipHandler.TipRegion(row, thingDef.description);

                float midY = row.y + (RowH - IconSz) / 2f;
                float x = row.x + RowPad;

                Widgets.ThingIcon(new Rect(x, midY, IconSz, IconSz), thingDef);
                x += IconSz + ColGap;

                float rx = row.xMax - RowPad;
                rx -= StockW;
                int currentStock = storage.CountStored(thingDef);
                int targetCount = enabled ? Mathf.Max(0, config?.count ?? 0) : 0;
                Color stockColor = currentStock <= 0 ? CStockNo : currentStock < targetCount ? CStockLow : CStockOk;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                GUI.color = stockColor;
                Widgets.Label(new Rect(rx, row.y, 35f, RowH), currentStock.ToString());
                GUI.color = CTextDim;
                Widgets.Label(new Rect(rx + 35f, row.y, 10f, RowH), "/");
                GUI.color = Color.white;
                Widgets.Label(new Rect(rx + 45f, row.y, StockW - 45f, RowH), targetCount.ToString());

                rx -= ColGap;
                rx -= FieldW;
                GUI.color = enabled ? CGold : CTextDim;
                Widgets.Label(new Rect(rx, row.y, FieldW, RowH), enabled ? $"¥{Mathf.Max(0f, config?.price ?? 0f):F0}" : "—");

                rx -= ColGap;
                rx -= FieldW;
                GUI.color = enabled ? Color.white : CTextDim;
                Widgets.Label(new Rect(rx, row.y, FieldW, RowH), enabled ? targetCount.ToString() : "—");

                rx -= ColGap;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = enabled ? Color.white : CTextDim;
                Widgets.Label(new Rect(x, row.y, rx - x, RowH), thingDef.LabelCap.Truncate(rx - x));
                ResetText();
            });
        }

        private void DrawComboPanel(Rect rect)
        {
            if (curCombo == null) return;

            float topH = 200f;
            float posterW = 140f;
            float posterH = 182f;
            Rect topRect = new Rect(rect.x, rect.y, rect.width, topH);
            Widgets.DrawBoxSolid(topRect, new Color(0f, 0f, 0f, 0.12f));
            DrawBorderRect(topRect, CDivider, 1f);

            Rect posterRect = new Rect(topRect.x + 14f, topRect.y + (topH - posterH) / 2f, posterW, posterH);
            ComboPosterRenderer.DrawComboPoster(posterRect, curCombo);

            int totalItems = curCombo.items.Sum(ci => ci.count);
            int kindCount = curCombo.items.Count;
            if (kindCount > 0)
            {
                GUI.color = CAccent;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(posterRect.x, posterRect.yMax + 2f, posterRect.width, 16f), SimTranslation.T("RSMF.ShopManager.ComboItemCount",
                    kindCount.Named("kindCount"),
                    totalItems.Named("totalItems")));
                ResetText();
            }

            float editX = posterRect.xMax + 18f;
            float editW = topRect.xMax - editX - 14f;
            float fieldH = 28f;
            float labelH = 20f;
            float y = topRect.y + 14f;

            DrawFieldLabel(new Rect(editX, y, editW, labelH), SimTranslation.T("RSMF.ShopManager.ComboName"));
            y += labelH + 2f;
            curCombo.comboName = Widgets.TextField(new Rect(editX, y, editW, fieldH), curCombo.comboName);
            y += fieldH + 4f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(editX, y, 120f, 26f), SimTranslation.T("RSMF.ShopManager.RandomName"), true, GameFont.Tiny))
                curCombo.comboName = ComboNameGenerator.GenerateName(curCombo);
            y += 34f;

            DrawFieldLabel(new Rect(editX, y, editW, labelH), SimTranslation.T("RSMF.ShopManager.ComboPrice"));
            y += labelH + 2f;

            if (SimUiStyle.DrawSecondaryButton(new Rect(editX + 108f, y, 120f, fieldH), SimTranslation.T("RSMF.ShopManager.EstimateByItem"), true, GameFont.Tiny))
            {
                float cost = curCombo.items.Sum(ci => GetReferencePriceForCombo(ci.def) * ci.count);
                curCombo.totalPrice = (float)System.Math.Round(cost * 0.9f, 1);
                comboPriceBuf = curCombo.totalPrice.ToString("F0");
                priceJustCalculated = true;
            }

            if (!priceJustCalculated)
            {
                Widgets.TextFieldNumeric(new Rect(editX, y, 100f, fieldH), ref curCombo.totalPrice, ref comboPriceBuf, 0f, 99999f);
            }
            else
            {
                Widgets.DrawBoxSolid(new Rect(editX, y, 100f, fieldH), new Color(0f, 0f, 0f, 0.3f));
                GUI.color = CGold;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(editX, y, 100f, fieldH), comboPriceBuf);
                ResetText();
            }

            GUI.color = CGold;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(editX - 18f, y, 18f, fieldH), "¥");
            ResetText();

            y += fieldH + 4f;
            GUI.color = CTextDim;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(editX, y, editW, 18f), SimTranslation.T("RSMF.ShopManager.AutoEstimateTip"));
            ResetText();

            Rect saveNewRect = new Rect(topRect.xMax - 224f, topRect.y + 10f, 116f, 26f);
            if (SimUiStyle.DrawSecondaryButton(saveNewRect, SimTranslation.T("RSMF.ShopManager.NewCombo"), true, GameFont.Tiny))
                SaveCurrentComboAndCreateNext();

            Rect deleteRect = new Rect(topRect.xMax - 100f, topRect.y + 10f, 88f, 26f);
            if (SimUiStyle.DrawDangerButton(deleteRect, SimTranslation.T("RSMF.ShopManager.DeleteCombo"), true, GameFont.Tiny))
            {
                zoneCombos.Remove(curCombo);
                curCombo = null;
                curPageDefName = PageOverview;
                ResetText();
                return;
            }

            List<ThingDef> sellable = availableGoodsDefs
                .Where(t => HasAnyStorageSellingThing(t) && MatchSearch(t.label))
                .ToList();

            Rect headerRect = new Rect(rect.x, topRect.yMax + 8f, rect.width, HeaderH);
            DrawTableHeader(headerRect, delegate
            {
                float cx = headerRect.xMax - RowPad;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Quantity"));
                cx -= ColGap;
                cx -= SliderW;
                DrawHdrLabel(new Rect(cx, headerRect.y, SliderW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Slider"));
                cx -= ColGap;
                float lx = headerRect.x + RowPad + CheckSz + ColGap + IconSz + ColGap;
                DrawHdrLabel(new Rect(lx, headerRect.y, cx - lx - ColGap, headerRect.height), SimTranslation.T("RSMF.ShopManager.ComboGoodsHeader"));
            });

            Rect outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - topH - HeaderH - 8f);
            DrawVirtualizedRows(outRect, sellable.Count, delegate(int i, Rect row)
            {
                DrawComboItemRow(row, sellable[i], i % 2 == 0);
            });
        }

        private void DrawComboItemRow(Rect row, ThingDef thingDef, bool alt)
        {
            ComboItem comboItem = curCombo.items.FirstOrDefault(ci => ci.def == thingDef);
            bool inCombo = comboItem != null;

            DrawRowBg(row, alt ? 1 : 0, inCombo);
            Widgets.DrawHighlightIfMouseover(row);

            float midY = row.y + (RowH - IconSz) / 2f;
            float ctrlY = row.y + (RowH - 24f) / 2f;
            float x = row.x + RowPad;

            bool check = inCombo;
            Widgets.Checkbox(x, row.y + (RowH - CheckSz) / 2f, ref check, CheckSz, paintable: true);
            if (check && !inCombo) curCombo.items.Add(new ComboItem { def = thingDef, count = 1 });
            else if (!check && inCombo) curCombo.items.Remove(comboItem);
            x += CheckSz + ColGap;

            Widgets.ThingIcon(new Rect(x, midY, IconSz, IconSz), thingDef);
            x += IconSz + ColGap;

            float rx = row.xMax - RowPad;
            float nameW = rx - SliderW - FieldW - ColGap * 2 - x;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = inCombo ? Color.white : CTextDim;
            string displayName = thingDef.LabelCap.Truncate(nameW);
            Widgets.Label(new Rect(x, row.y, nameW, RowH), displayName);
            if (displayName != thingDef.LabelCap.RawText) TooltipHandler.TipRegion(new Rect(x, row.y, nameW, RowH), thingDef.LabelCap);

            rx -= FieldW;
            if (inCombo)
            {
                string buffer = comboItem.count.ToString();
                Widgets.TextFieldNumeric(new Rect(rx, ctrlY, FieldW, 24f), ref comboItem.count, ref buffer, 1, 999);
                rx -= ColGap;
                rx -= SliderW;
                comboItem.count = (int)Widgets.HorizontalSlider(new Rect(rx, row.y + (RowH - 16f) / 2f, SliderW, 16f), comboItem.count, 1f, 50f, true);
            }

            ResetText();
        }
    }
}
