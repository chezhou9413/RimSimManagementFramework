using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //单件商品批量定价部分，职责是按真实商品市价统一设置未被顾客预订的售价。
    public sealed partial class Dialog_UniqueGoodsManager
    {
        private string priceMultiplierBuffer = "1";
        private readonly Dictionary<int, int> priceBufferIdentities = new Dictionary<int, int>();

        //将输入缓冲绑定到槽位中的实物，职责是防止槽位复用时把上一件商品的价格写回。
        private string GetPriceBuffer(UniqueGoodsSlotData slot)
        {
            int identity = slot.storedThingId >= 0 ? slot.storedThingId : slot.pendingSourceThingId;
            if (!priceBufferIdentities.TryGetValue(slot.index, out int previous) || previous != identity)
            {
                priceBufferIdentities[slot.index] = identity;
                priceBuffers[slot.index] = slot.price.ToString("0.##");
            }
            return priceBuffers.TryGetValue(slot.index, out string buffer) ? buffer : slot.price.ToString("0.##");
        }

        //绘制定价倍率和执行按钮，职责是明确以已经包含品质、材质与耐久的市价为基准。
        private void DrawBulkPricingControls(Rect rect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            string label = SimTranslation.T("RSMF.UniqueGoods.Pricing.Multiplier");
            string apply = SimTranslation.T("RSMF.UniqueGoods.Pricing.Apply");
            float labelWidth = Text.CalcSize(label).x + 6f;
            float buttonWidth = Text.CalcSize(apply).x + 24f;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect buttonRect = new Rect(rect.xMax - buttonWidth, rect.y, buttonWidth, rect.height);
            Rect inputRect = new Rect(labelRect.xMax, rect.y, buttonRect.x - labelRect.xMax - 6f, rect.height);
            Widgets.Label(labelRect, label);
            priceMultiplierBuffer = Widgets.TextField(inputRect, priceMultiplierBuffer);
            bool valid = float.TryParse(priceMultiplierBuffer, out float multiplier)
                && multiplier > 0f && !float.IsNaN(multiplier) && !float.IsInfinity(multiplier);
            if (SimUiStyle.DrawSecondaryButton(buttonRect, apply, valid)) ApplyMarketPrices(multiplier);
            TooltipHandler.TipRegion(rect, SimTranslation.T("RSMF.UniqueGoods.Pricing.Help"));
        }

        //执行全柜批量定价，职责是跳过已被顾客接受的成交价及失去来源实物的槽位。
        private void ApplyMarketPrices(float multiplier)
        {
            var prices = new List<(int index, float price)>();
            foreach (UniqueGoodsSlotData slot in container.UniqueSlots)
            {
                if (slot == null || !slot.IsOccupied || slot.IsReservedByCustomer) continue;
                Thing thing = container.GetStoredThing(slot)
                    ?? UniqueGoodsUtility.FindSource(container.Map, slot.pendingSourceThingId, container);
                if (thing == null || thing.Destroyed) continue;
                float price = Mathf.Max(1f, thing.MarketValue * multiplier);
                if (float.IsNaN(price) || float.IsInfinity(price))
                {
                    Messages.Message(SimTranslation.T("RSMF.UniqueGoods.Pricing.Invalid"), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                prices.Add((slot.index, price));
            }
            foreach (var item in prices)
            {
                container.SetSlotPrice(item.index, item.price);
                priceBuffers[item.index] = item.price.ToString("0.##");
            }
            Messages.Message(SimTranslation.T("RSMF.UniqueGoods.Pricing.Applied", prices.Count.Named("count")),
                MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
