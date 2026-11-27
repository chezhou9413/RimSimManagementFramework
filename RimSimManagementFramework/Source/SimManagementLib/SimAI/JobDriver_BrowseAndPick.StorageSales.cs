using System.Collections.Generic;
using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimAI
{
    //按当前实物的品质、市价和未预约数量挑选宿主仓储商品。
    public partial class JobDriver_BrowseAndPick
    {
        //在取货成功后把同一件商品和确定的金额一起记入购物车。
        private bool TryPickHostGoods(ThingComp_StorageSales sales, LordJob_CustomerVisit visit, Zone_Shop shop,
            float budget, GameComponent_ShopFinanceManager finance)
        {
            int id = pawn.thingIDNumber;
            bool foodVisit = CustomerShoppingMatchUtility.IsFoodVisit(pawn);
            var choices = new List<(Thing thing, float price, CustomerPriceEvaluation evaluation)>();
            foreach (Thing thing in sales.SaleThings())
            {
                if (!CustomerShoppingMatchUtility.ThingMatchesCustomer(visit, thing.def)) continue;
                float price = sales.Price(thing);
                if (price <= 0f) continue;
                var evaluation = CustomerPriceUtility.EvaluateMarketValue(thing.MarketValue, price, visit.GetPriceSensitivity(id));
                if (evaluation.rejected)
                {
                    visit.RecordPriceRejection(id, BuildPriceRejectionReason(thing.def, evaluation));
                    continue;
                }
                if (price <= budget && (!foodVisit || CustomerPurchaseDeliveryUtility.CanCarryMore(pawn, visit.GetCartItems(id), thing.def, 1)))
                    choices.Add((thing, price, evaluation));
            }
            if (choices.Count == 0) return false;
            var chosen = choices.RandomElementByWeight(c => Mathf.Max(0.001f,
                visit.GetPreferenceMultiplier(id, c.thing.def) * c.evaluation.purchaseWeight));
            int max = Mathf.Min(sales.Available(chosen.thing), Mathf.FloorToInt(budget / chosen.price));
            if (foodVisit) max = Mathf.Min(max, CustomerPurchaseDeliveryUtility.MaxAdditionalCountByMass(pawn, visit.GetCartItems(id), chosen.thing.def));
            if (max <= 0) return false;
            var item = ShopDataUtility.TakeCartStack(sales, chosen.thing, PickPurchaseCount(max, chosen.evaluation), chosen.price);
            if (item == null) return false;
            if (!visit.AddExactCartItem(id, item))
            {
                ShopDataUtility.ReturnPhysicalThing(item, item.TakeExactThing());
                return false;
            }
            float total = item.unitPrice * item.count;
            visit.AddCustomerBill(id, total);
            visit.ClearCurrentShopNoProgressBrowse(pawn);
            visit.ClearPriceRejectionReason(id);
            finance?.QueueProductSale(pawn, shop, item.def, item.count, total, item.marketValue * item.count);
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.PurchaseItem);
            ShopBubbleUtility.ShowThingBubble(pawn, item.def, SimTranslation.T("RSMF.Bubble.TakeItemCount",
                item.def.label.Named("item"), item.count.Named("count")), null, Color.white);
            visit.GetOrCreateSession(pawn)?.NotifyConsumptionCompleted(visit, pawn, "取走宿主仓储实物");
            return true;
        }
    }
}
