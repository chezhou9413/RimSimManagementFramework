using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //组织商店管理面板，职责是展示配置内容与当前选择摘要。
    public partial class Dialog_ShopManager
    {
        //绘制单店总览，职责是展示当前经营状态和配置摘要。
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

        //绘制套餐编辑面板，职责是维护套餐商品与售价草稿。
        private void DrawComboPanel(Rect rect)
        {
            if (curCombo == null) return;
            EnsureComboUiCache();

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
            float aiNameButtonW = 82f;
            float aiNameGap = 8f;
            float nameFieldW = Mathf.Max(120f, editW - aiNameButtonW - aiNameGap);
            curCombo.comboName = Widgets.TextField(new Rect(editX, y, nameFieldW, fieldH), curCombo.comboName);
            DrawComboAiNameButton(new Rect(editX + nameFieldW + aiNameGap, y, aiNameButtonW, fieldH));
            y += fieldH + 4f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(editX, y, 120f, 26f), SimTranslation.T("RSMF.ShopManager.RandomName"), true, GameFont.Tiny))
                curCombo.comboName = ComboNameGenerator.GenerateName(curCombo);
            y += 34f;

            DrawFieldLabel(new Rect(editX, y, editW, labelH), SimTranslation.T("RSMF.ShopManager.ComboPrice"));
            y += labelH + 2f;

            if (SimUiStyle.DrawSecondaryButton(new Rect(editX + 108f, y, 120f, fieldH), SimTranslation.T("RSMF.ShopManager.EstimateByItem"), true, GameFont.Tiny))
            {
                float cost = curCombo.items.Sum(ci => GetCachedReferencePriceForCombo(ci.def) * ci.count);
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
                InvalidateComboUiCache();
                curCombo = null;
                curPageDefName = PageOverview;
                ResetText();
                return;
            }

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
            DrawVirtualizedRows(outRect, comboSellableCache.Count, delegate(int i, Rect row)
            {
                DrawComboItemRow(row, comboSellableCache[i], i % 2 == 0);
            });
        }

        //绘制套餐商品行，职责是展示选项并响应数量配置。
        private void DrawComboItemRow(Rect row, ThingDef thingDef, bool alt)
        {
            ComboItem comboItem = GetCachedComboItem(thingDef);
            bool inCombo = comboItem != null;

            DrawRowBg(row, alt ? 1 : 0, inCombo);
            Widgets.DrawHighlightIfMouseover(row);

            float midY = row.y + (RowH - IconSz) / 2f;
            float ctrlY = row.y + (RowH - 24f) / 2f;
            float x = row.x + RowPad;

            bool check = inCombo;
            Widgets.Checkbox(x, row.y + (RowH - CheckSz) / 2f, ref check, CheckSz, paintable: true);
            if (check && !inCombo)
            {
                comboItem = new ComboItem { def = thingDef, count = 1 };
                curCombo.items.Add(comboItem);
                comboItemByDefCache[thingDef] = comboItem;
                InvalidateComboItemCache();
                inCombo = true;
            }
            else if (!check && inCombo)
            {
                curCombo.items.Remove(comboItem);
                comboItemByDefCache.Remove(thingDef);
                comboItemCountBuffers.Remove(thingDef.defName);
                InvalidateComboItemCache();
                inCombo = false;
            }
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
                int oldCount = comboItem.count;
                string buffer = GetComboCountBuffer(thingDef, comboItem.count);
                Widgets.TextFieldNumeric(new Rect(rx, ctrlY, FieldW, 24f), ref comboItem.count, ref buffer, 1, 999);
                SetComboCountBuffer(thingDef, buffer);
                rx -= ColGap;
                rx -= SliderW;
                comboItem.count = (int)Widgets.HorizontalSlider(new Rect(rx, row.y + (RowH - 16f) / 2f, SliderW, 16f), comboItem.count, 1f, 50f, true);
                if (comboItem.count != oldCount)
                {
                    SetComboCountBuffer(thingDef, comboItem.count.ToString());
                    InvalidateComboItemCache();
                }
            }

            ResetText();
        }
    }
}
