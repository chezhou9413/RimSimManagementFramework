using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    public class JobDriver_BrowseAndPick : JobDriver
    {
        private const int MaxShelfReservations = 24;
        private Building_SimContainer TargetShelf => (Building_SimContainer)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetShelf, job, MaxShelfReservations, 0, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil browse = Toils_General.Wait(300);
            browse.initAction = () =>
            {
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseStart);
            };
            browse.WithProgressBarToilDelay(TargetIndex.A);
            yield return browse;

            Toil pickItem = new Toil();
            pickItem.initAction = delegate
            {
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null) return;

                GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();

                int pId = pawn.thingIDNumber;
                if (!lordJob.cartValues.ContainsKey(pId))
                    lordJob.cartValues[pId] = 0f;

                float alreadySpent = lordJob.cartValues[pId];
                float remainingBudget = lordJob.GetBudgetForPawn(pId) - alreadySpent;

                if (remainingBudget <= 0f)
                {
                    lordJob.MarkPawnReadyForCheckout(pId);
                    return;
                }

                Zone_Shop shopZone = ShopDataUtility.FindAssignedShopZone(
                    pawn.Map,
                    lordJob.targetShopZoneId,
                    lordJob.targetShopCell);
                if (shopZone != null)
                {
                    List<ComboData> combos = ShopDataUtility.GetAffordableInStockCombos(shopZone, remainingBudget);
                    if (!combos.NullOrEmpty())
                    {
                        ComboData targetCombo = combos
                            .OrderByDescending(c => GetComboPreferenceScore(lordJob, pId, c))
                            .ThenByDescending(c => c.totalPrice)
                            .FirstOrDefault();
                        if (ShopDataUtility.TryPurchaseCombo(shopZone, targetCombo, out float comboPrice))
                        {
                            float comboCost = 0f;
                            for (int i = 0; i < targetCombo.items.Count; i++)
                            {
                                ComboItem ci = targetCombo.items[i];
                                if (ci?.def == null || ci.count <= 0) continue;
                                comboCost += Mathf.Max(0f, ci.def.BaseMarketValue * ci.count);
                            }

                            lordJob.cartValues[pId] += comboPrice;
                            lordJob.AddCartItemsFromCombo(pId, targetCombo.items);
                            string comboName = string.IsNullOrEmpty(targetCombo.comboName) ? "未命名套餐" : targetCombo.comboName;
                            finance?.QueueComboSale(pawn, shopZone, comboName, comboPrice, comboCost);
                            CustomerExpressionUtility.TryShowExpression(
                                pawn,
                                CustomerExpressionEvents.PurchaseCombo,
                                new CustomerExpressionRequest().AddTag("combo"));
                            ShopBubbleUtility.ShowThingBubble(
                                pawn,
                                targetCombo.items.FirstOrDefault(item => item?.def != null)?.def,
                                $"选购套餐: {comboName}",
                                new Color(0.95f, 0.8f, 0.35f),
                                Color.white);
                            lordJob.MarkPawnReadyForCheckout(pId);
                            return;
                        }
                    }
                }

                List<(ThingDef def, float unitPrice)> candidates = new List<(ThingDef def, float unitPrice)>();
                ThingComp_GoodsData comp = TargetShelf.GetComp<ThingComp_GoodsData>();
                foreach (ThingDef def in TargetShelf.ActiveDefs)
                {
                    if (TargetShelf.CountStored(def) <= 0) continue;

                    float configuredPrice = comp?.itemData != null
                        && comp.itemData.TryGetValue(def.defName, out GoodsItemData data)
                        && data != null
                        && data.enabled
                        && data.price > 0f
                        ? data.price
                        : 0f;
                    float unitPrice = configuredPrice > 0f ? configuredPrice : Mathf.Max(1f, Mathf.CeilToInt(def.BaseMarketValue));
                    if (unitPrice <= remainingBudget)
                    {
                        candidates.Add((def, unitPrice));
                    }
                }

                if (candidates.NullOrEmpty())
                {
                    CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseNoMatch);
                    ShopBubbleUtility.ShowTextBubble(pawn, "没有合适的商品", new Color(0.88f, 0.88f, 0.88f));
                    lordJob.MarkPawnReadyForCheckout(pId);
                    return;
                }

                (ThingDef itemToBuy, float itemPrice) = candidates.RandomElementByWeight(c =>
                    Mathf.Max(0.1f, lordJob.GetPreferenceMultiplier(pId, c.def)));

                int maxByBudget = Mathf.FloorToInt(remainingBudget / itemPrice);
                int maxByStock = TargetShelf.CountStored(itemToBuy);
                int maxCount = Mathf.Min(maxByBudget, maxByStock);
                int buyCount = maxCount > 1 ? Rand.RangeInclusive(1, maxCount) : 1;

                Thing taken = TargetShelf.TryVirtualBuy(itemToBuy, buyCount, out _);
                if (taken != null)
                {
                    int actualCount = Mathf.Max(1, taken.stackCount);
                    float totalPrice = itemPrice * actualCount;
                    float totalCost = Mathf.Max(0f, itemToBuy.BaseMarketValue * actualCount);
                    lordJob.cartValues[pId] += totalPrice;
                    lordJob.AddCartItem(pId, itemToBuy, actualCount);
                    finance?.QueueProductSale(pawn, shopZone, itemToBuy, actualCount, totalPrice, totalCost);
                    taken.Destroy(DestroyMode.Vanish);
                    float preferenceMultiplier = lordJob.GetPreferenceMultiplier(pId, itemToBuy);
                    CustomerExpressionUtility.TryShowExpression(
                        pawn,
                        preferenceMultiplier >= 1.5f ? CustomerExpressionEvents.PurchasePreferredItem : CustomerExpressionEvents.PurchaseItem,
                        preferenceMultiplier >= 1.5f
                            ? new CustomerExpressionRequest().AddTag("preferred")
                            : null);
                    ShopBubbleUtility.ShowThingBubble(
                        pawn,
                        itemToBuy,
                        actualCount > 1 ? $"拿取 {itemToBuy.label} x{actualCount}" : $"拿取 {itemToBuy.label}",
                        null,
                        Color.white);
                }

                lordJob.MarkPawnReadyForCheckout(pId);
            };

            yield return pickItem;
        }

        private static float GetComboPreferenceScore(LordJob_CustomerVisit lordJob, int pawnId, ComboData combo)
        {
            if (combo == null || combo.items.NullOrEmpty()) return 0f;

            float score = 0f;
            for (int i = 0; i < combo.items.Count; i++)
            {
                ComboItem item = combo.items[i];
                if (item?.def == null || item.count <= 0) continue;
                score += lordJob.GetPreferenceMultiplier(pawnId, item.def) * item.count;
            }

            return score;
        }
    }
}
