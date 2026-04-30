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
                DrawHdrLabel(new Rect(cx, headerRect.y, statusW, headerRect.height), "状态", TextAnchor.MiddleCenter);
                cx -= ColGap;
                cx -= StockW;
                DrawHdrLabel(new Rect(cx, headerRect.y, StockW, headerRect.height), "当前 / 目标");
                cx -= ColGap;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), "单价");
                cx -= ColGap;
                float lx = headerRect.x + RowPad + IconSz + ColGap;
                DrawHdrLabel(new Rect(lx, headerRect.y, cx - lx - ColGap, headerRect.height), "商品名称");
            });

            Rect outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - HeaderH);
            float viewWidth = outRect.width - ScrW;
            Widgets.BeginScrollView(outRect, ref listScroll, new Rect(0f, 0f, viewWidth, items.Count * RowH));

            for (int i = 0; i < items.Count; i++)
            {
                ShopItemStatus item = items[i];
                Rect row = new Rect(0f, i * RowH, viewWidth, RowH);
                DrawRowBg(row, i, false);
                Widgets.DrawHighlightIfMouseover(row);
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
                    Widgets.Label(statusRect, "缺货");
                }
                else if (item.CurrentStock < item.Config.count)
                {
                    Widgets.DrawBoxSolid(statusRect, new Color(0.95f, 0.72f, 0.25f, 0.15f));
                    GUI.color = CStockLow;
                    Widgets.Label(statusRect, "不足");
                }
                else
                {
                    Widgets.DrawBoxSolid(statusRect, new Color(0.35f, 0.80f, 0.45f, 0.15f));
                    GUI.color = CStockOk;
                    Widgets.Label(statusRect, "充足");
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
            }

            Widgets.EndScrollView();
        }

        private void DrawManagePanel(Rect rect)
        {
            List<ThingDef> list = availableGoodsDefs.Where(t => MatchSearch(t.label)).ToList();

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderH);
            DrawTableHeader(headerRect, delegate
            {
                float cx = headerRect.xMax - RowPad;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), "单价");
                cx -= ColGap;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), "目标量");
                cx -= ColGap;
                cx -= SliderW;
                DrawHdrLabel(new Rect(cx, headerRect.y, SliderW, headerRect.height), "快速调节");
                cx -= ColGap;
                float lx = headerRect.x + RowPad + CheckSz + ColGap + IconSz + ColGap;
                DrawHdrLabel(new Rect(lx, headerRect.y, cx - lx - ColGap, headerRect.height), "商品名称  （勾选即上架）");
            });

            Rect outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - HeaderH);
            float viewWidth = outRect.width - ScrW;
            Widgets.BeginScrollView(outRect, ref listScroll, new Rect(0f, 0f, viewWidth, list.Count * RowH));

            for (int i = 0; i < list.Count; i++)
            {
                ThingDef thingDef = list[i];
                Rect row = new Rect(0f, i * RowH, viewWidth, RowH);
                GoodsItemData draft = GetDraftItem(thingDef);
                bool enabled = draft.enabled;

                DrawRowBg(row, i, enabled);
                Widgets.DrawHighlightIfMouseover(row);

                float midY = row.y + (RowH - IconSz) / 2f;
                float ctrlY = row.y + (RowH - 24f) / 2f;
                float x = row.x + RowPad;

                Widgets.Checkbox(x, row.y + (RowH - CheckSz) / 2f, ref enabled, CheckSz, paintable: true);
                if (enabled != draft.enabled)
                {
                    draft.enabled = enabled;
                    if (draft.enabled && draft.count <= 0) draft.count = 1;
                }
                x += CheckSz + ColGap;

                Widgets.ThingIcon(new Rect(x, midY, IconSz, IconSz), thingDef);
                x += IconSz + ColGap;

                float rx = row.xMax - RowPad;
                rx -= FieldW;
                if (enabled)
                {
                    if (!priceBuffers.ContainsKey(thingDef.defName)) priceBuffers[thingDef.defName] = draft.price.ToString("F0");
                    string priceBuffer = priceBuffers[thingDef.defName];
                    Widgets.TextFieldNumeric(new Rect(rx, ctrlY, FieldW, 24f), ref draft.price, ref priceBuffer, 0f, 99999f);
                    priceBuffers[thingDef.defName] = priceBuffer;
                }
                else
                {
                    DrawDash(new Rect(rx, row.y, FieldW, RowH));
                }

                rx -= ColGap;
                rx -= FieldW;
                if (enabled)
                {
                    if (!countBuffers.ContainsKey(thingDef.defName)) countBuffers[thingDef.defName] = draft.count.ToString();
                    string countBuffer = countBuffers[thingDef.defName];
                    int previous = draft.count;
                    Widgets.TextFieldNumeric(new Rect(rx, ctrlY, FieldW, 24f), ref draft.count, ref countBuffer, 0, 999999);
                    if (draft.count != previous) countBuffer = draft.count.ToString();
                    countBuffers[thingDef.defName] = countBuffer;
                }
                else
                {
                    DrawDash(new Rect(rx, row.y, FieldW, RowH));
                }

                rx -= ColGap;
                rx -= SliderW;
                if (enabled)
                {
                    int newValue = (int)Widgets.HorizontalSlider(new Rect(rx, row.y + (RowH - 16f) / 2f, SliderW, 16f), draft.count, 0f, 999f, true);
                    if (newValue != draft.count)
                    {
                        draft.count = newValue;
                        countBuffers[thingDef.defName] = newValue.ToString();
                    }
                }
                else
                {
                    DrawDash(new Rect(rx, row.y, SliderW, RowH));
                }

                rx -= ColGap;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = enabled ? Color.white : CTextDim;
                Widgets.Label(new Rect(x, row.y, rx - x, RowH), thingDef.LabelCap.Truncate(rx - x));
                ResetText();
            }

            Widgets.EndScrollView();
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
                Widgets.Label(new Rect(posterRect.x, posterRect.yMax + 2f, posterRect.width, 16f), $"{kindCount} 种 · {totalItems} 件");
                ResetText();
            }

            float editX = posterRect.xMax + 18f;
            float editW = topRect.xMax - editX - 14f;
            float fieldH = 28f;
            float labelH = 20f;
            float y = topRect.y + 14f;

            DrawFieldLabel(new Rect(editX, y, editW, labelH), "套餐名称");
            y += labelH + 2f;
            curCombo.comboName = Widgets.TextField(new Rect(editX, y, editW, fieldH), curCombo.comboName);
            y += fieldH + 4f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(editX, y, 120f, 26f), "🎲 随机取名", true, GameFont.Tiny))
                curCombo.comboName = ComboNameGenerator.GenerateName(curCombo);
            y += 34f;

            DrawFieldLabel(new Rect(editX, y, editW, labelH), "套餐售价");
            y += labelH + 2f;

            if (SimUiStyle.DrawSecondaryButton(new Rect(editX + 108f, y, 120f, fieldH), "📊 按单品估算", true, GameFont.Tiny))
            {
                float cost = curCombo.items.Sum(ci => GetDraftItem(ci.def).price * ci.count);
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
            Widgets.Label(new Rect(editX, y, editW, 18f), "按单品原价总和 × 0.9 自动估算");
            ResetText();

            Rect deleteRect = new Rect(topRect.xMax - 100f, topRect.y + 10f, 88f, 26f);
            if (SimUiStyle.DrawDangerButton(deleteRect, "删除套餐", true, GameFont.Tiny))
            {
                zoneCombos.Remove(curCombo);
                curCombo = null;
                curMenu = MenuType.Overview;
                ResetText();
                return;
            }

            List<ThingDef> sellable = availableGoodsDefs
                .Where(t => GetDraftItem(t).enabled && MatchSearch(t.label))
                .ToList();

            Rect headerRect = new Rect(rect.x, topRect.yMax + 8f, rect.width, HeaderH);
            DrawTableHeader(headerRect, delegate
            {
                float cx = headerRect.xMax - RowPad;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), "数量");
                cx -= ColGap;
                cx -= SliderW;
                DrawHdrLabel(new Rect(cx, headerRect.y, SliderW, headerRect.height), "调节滑块");
                cx -= ColGap;
                float lx = headerRect.x + RowPad + CheckSz + ColGap + IconSz + ColGap;
                DrawHdrLabel(new Rect(lx, headerRect.y, cx - lx - ColGap, headerRect.height), "在售货品  （勾选加入套餐）");
            });

            Rect outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - topH - HeaderH - 8f);
            float viewWidth = outRect.width - ScrW;
            Widgets.BeginScrollView(outRect, ref listScroll, new Rect(0f, 0f, viewWidth, sellable.Count * RowH));
            for (int i = 0; i < sellable.Count; i++)
            {
                DrawComboItemRow(new Rect(0f, i * RowH, viewWidth, RowH), sellable[i], i % 2 == 0);
            }
            Widgets.EndScrollView();
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
