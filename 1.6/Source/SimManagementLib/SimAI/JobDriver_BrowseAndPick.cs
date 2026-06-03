using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI.CustomerVisit;
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
    /// <summary>
    /// 执行顾客到货柜浏览、选择商品、扣减虚拟库存并写入购物车的流程。
    /// </summary>
    public class JobDriver_BrowseAndPick : JobDriver
    {
        private Building_SimContainer TargetShelf => (Building_SimContainer)job.GetTarget(TargetIndex.A).Thing;

        /// <summary>
        /// 跳过目标货柜预约，负责允许任意数量顾客同时浏览同一货柜。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        /// <summary>
        /// 构建顾客浏览货柜并挑选商品或套餐的流程，负责在目标商品耗尽时及时结束本次购物。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil browse = Toils_General.Wait(300);
            browse.initAction = () =>
            {
                LordJob_CustomerVisit lordJob = pawn.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                CustomerVisitSession session = lordJob?.GetOrCreateSession(pawn);
                session?.NotifyBrowseStarted(lordJob, pawn);
                lordJob?.MarkCurrentShopBrowsed(pawn);
                lordJob?.RecordCurrentShopStorageVisit(pawn, TargetShelf);
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseStart);
            };
            browse.tickAction = () =>
            {
                ShopProgressBarUtility.Report(pawn, 1f - ticksLeftThisToil / 300f);
            };
            browse.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            yield return browse;

            Toil pickItem = new Toil();
            pickItem.initAction = delegate
            {
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null) return;

                GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();

                int pId = pawn.thingIDNumber;
                lordJob.EnsureCustomerBill(pId);

                Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
                float remainingBudget = lordJob.GetRemainingTripBudget(pawn, shopZone);
                bool noProgressRecorded = false;

                if (remainingBudget <= 0f)
                {
                    RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded);
                    FinishWithoutBillOrCheckout(lordJob, pId, "顾客剩余预算不足，离店");
                    return;
                }

                if (shopZone != null)
                {
                    List<ComboData> combos = CustomerShoppingMatchUtility.GetMatchingAffordableInStockCombos(shopZone, lordJob, pawn, remainingBudget);
                    List<CustomerCartItem> currentCart = lordJob.GetCartItems(pId);
                    if (!combos.NullOrEmpty())
                    {
                        ComboData targetCombo = combos
                            .OrderByDescending(c => GetComboPurchaseScore(lordJob, pId, c))
                            .ThenByDescending(c => GetComboEffectivePrice(c))
                            .FirstOrDefault();
                        if (targetCombo != null && ShopDataUtility.TryPurchaseCombo(shopZone, targetCombo, out float comboPrice))
                        {
                            float comboCost = 0f;
                            for (int i = 0; i < targetCombo.items.Count; i++)
                            {
                                ComboItem ci = targetCombo.items[i];
                                if (ci?.def == null || ci.count <= 0) continue;
                                comboCost += Mathf.Max(0f, ci.def.BaseMarketValue * ci.count);
                            }

                            lordJob.AddCustomerBill(pId, comboPrice);
                            lordJob.AddCartItemsFromCombo(pId, targetCombo.items);
                            lordJob.ClearCurrentShopNoProgressBrowse(pawn);
                            lordJob.ClearPriceRejectionReason(pId);
                            string comboName = string.IsNullOrEmpty(targetCombo.comboName) ? SimTranslation.T("RSMF.Common.UnnamedCombo") : targetCombo.comboName;
                    finance?.QueueComboSale(pawn, shopZone, comboName, comboPrice, comboCost);
                            CustomerExpressionUtility.TryShowExpression(
                                pawn,
                                CustomerExpressionEvents.PurchaseCombo,
                                new CustomerExpressionRequest().AddTag("combo"));
                            ShopBubbleUtility.ShowThingBubble(
                                pawn,
                                targetCombo.items.FirstOrDefault(item => item?.def != null)?.def,
                                SimTranslation.T("RSMF.Bubble.PickCombo", comboName.Named("comboName")),
                                new Color(0.95f, 0.8f, 0.35f),
                                Color.white);
                            lordJob.GetOrCreateSession(pawn)?.NotifyConsumptionCompleted(lordJob, pawn, "套餐购买完成");
                            return;
                        }

                        if (RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded))
                            return;
                    }
                    else if (ShopDataUtility.TryFindRejectedComboPriceReason(
                        shopZone,
                        lordJob.GetPriceSensitivity(pId),
                        combo => CustomerShoppingMatchUtility.ComboMatchesCustomer(lordJob.RuntimeCustomerKind, lordJob.customerKind, combo),
                        out string comboRejectedReason))
                    {
                        lordJob.RecordPriceRejection(pId, comboRejectedReason);
                    }
                }

                List<(ThingDef def, float unitPrice, CustomerPriceEvaluation price)> candidates = new List<(ThingDef def, float unitPrice, CustomerPriceEvaluation price)>();
                string rejectedReason = "";
                foreach (ThingDef def in TargetShelf.ActiveDefs)
                {
                    if (TargetShelf.CountStored(def) <= 0) continue;
                    if (!CustomerShoppingMatchUtility.ThingMatchesCustomer(lordJob, def)) continue;

                    float unitPrice = ShopPricingUtility.GetUnitPrice(TargetShelf, def);
                    CustomerPriceEvaluation price = CustomerPriceUtility.Evaluate(def, unitPrice, lordJob.GetPriceSensitivity(pId));
                    if (price.rejected)
                    {
                        rejectedReason = BuildPriceRejectionReason(def, price);
                        continue;
                    }
                    if (unitPrice <= remainingBudget)
                    {
                        candidates.Add((def, unitPrice, price));
                    }
                }

                if (candidates.NullOrEmpty())
                {
                    if (ShouldLeaveBecauseShopHasNoMatch(lordJob, shopZone, remainingBudget))
                    {
                        RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded);
                        FinishWithoutBillOrCheckout(lordJob, pId, "货柜没有顾客想要的商品，离店");
                        return;
                    }

                    bool shouldCheckout = RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded);
                    if (!string.IsNullOrEmpty(rejectedReason))
                        lordJob.RecordPriceRejection(pId, rejectedReason);
                    CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseNoMatch);
                    ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.NoSuitableGoods"), new Color(0.88f, 0.88f, 0.88f));
                    if (shouldCheckout)
                        lordJob.MarkPawnReadyForCheckout(pId);
                    return;
                }

                (ThingDef itemToBuy, float itemPrice, CustomerPriceEvaluation itemPriceEvaluation) = candidates.RandomElementByWeight(c =>
                    Mathf.Max(0.001f, lordJob.GetPreferenceMultiplier(pId, c.def) * c.price.purchaseWeight));

                int maxByBudget = Mathf.FloorToInt(remainingBudget / itemPrice);
                int maxByStock = TargetShelf.CountStored(itemToBuy);
                int maxCount = Mathf.Min(maxByBudget, maxByStock);
                if (maxCount <= 0)
                {
                    if (RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded))
                        FinishWithoutBillOrCheckout(lordJob, pId, "顾客无法购买目标商品，离店");
                    return;
                }
                int buyCount = PickPurchaseCount(maxCount, itemPriceEvaluation);

                Thing taken = TargetShelf.TryVirtualBuy(itemToBuy, buyCount, out _);
                if (taken != null)
                {
                    int actualCount = Mathf.Max(1, taken.stackCount);
                    float totalPrice = itemPrice * actualCount;
                    float totalCost = Mathf.Max(0f, itemToBuy.BaseMarketValue * actualCount);
                    lordJob.AddCustomerBill(pId, totalPrice);
                    lordJob.AddCartItem(pId, itemToBuy, actualCount);
                    lordJob.ClearCurrentShopNoProgressBrowse(pawn);
                    lordJob.ClearPriceRejectionReason(pId);
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
                        actualCount > 1
                            ? SimTranslation.T("RSMF.Bubble.TakeItemCount", itemToBuy.label.Named("item"), actualCount.Named("count"))
                            : SimTranslation.T("RSMF.Bubble.TakeItem", itemToBuy.label.Named("item")),
                        null,
                        Color.white);
                }
                else
                {
                    if (RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded))
                        FinishWithoutBillOrCheckout(lordJob, pId, "顾客拿取商品失败，离店");
                    return;
                }

                lordJob.GetOrCreateSession(pawn)?.NotifyConsumptionCompleted(lordJob, pawn, "商品购买完成");
            };

            yield return pickItem;
        }

        /// <summary>
        /// 计算套餐对指定顾客的偏好得分，负责让更符合偏好的套餐优先被购买。
        /// </summary>
        private static float GetComboPurchaseScore(LordJob_CustomerVisit lordJob, int pawnId, ComboData combo)
        {
            if (combo == null || combo.items.NullOrEmpty()) return 0f;

            float score = 0f;
            for (int i = 0; i < combo.items.Count; i++)
            {
                ComboItem item = combo.items[i];
                if (item?.def == null || item.count <= 0) continue;
                score += lordJob.GetPreferenceMultiplier(pawnId, item.def) * item.count;
            }

            CustomerPriceEvaluation price = CustomerPriceUtility.EvaluateCombo(GetComboEffectivePrice(combo), ShopDataUtility.GetComboReferenceValue(combo), lordJob.GetPriceSensitivity(pawnId));
            return score * Mathf.Max(0.001f, price.purchaseWeight);
        }

        /// <summary>
        /// 选择单次购买数量，负责让顾客稳定购买但避免一次搬空整柜库存。
        /// </summary>
        private static int PickPurchaseCount(int maxCount, CustomerPriceEvaluation price)
        {
            if (maxCount <= 1) return 1;

            if (price.ratio <= 0.9f)
                return Rand.Value < 0.45f ? 1 : Mathf.Min(Rand.RangeInclusive(2, 3), maxCount);
            if (price.ratio > 1.5f)
                return 1;
            return Rand.Value < 0.7f ? 1 : Mathf.Min(2, maxCount);
        }

        /// <summary>
        /// 构建价格拒买原因，负责让评价和调试说明顾客为什么没有购买目标商品。
        /// </summary>
        private static string BuildPriceRejectionReason(ThingDef def, CustomerPriceEvaluation price)
        {
            string label = def?.label ?? def?.defName ?? "未知商品";
            return $"商品 {label} 售价约为市价 {price.ratio:F1} 倍，顾客认为价格远高于市价而拒绝购买";
        }

        /// <summary>
        /// 返回套餐实际售价，负责给排序和价格评估使用。
        /// </summary>
        private static float GetComboEffectivePrice(ComboData combo)
        {
            if (combo == null) return 0f;
            if (combo.totalPrice > 0f) return combo.totalPrice;
            return ShopDataUtility.GetComboReferenceValue(combo);
        }

        /// <summary>
        /// 判断当前商店是否已经没有顾客可购买的目标内容，负责让单人顾客快速结束无效浏览。
        /// </summary>
        private bool ShouldLeaveBecauseShopHasNoMatch(LordJob_CustomerVisit lordJob, Zone_Shop shopZone, float remainingBudget)
        {
            return lordJob != null
                && pawn != null
                && !CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoodsOrServices(pawn, shopZone, lordJob, remainingBudget);
        }

        /// <summary>
        /// 根据账单状态结束顾客流程，负责让零账单顾客离店、有账单顾客进入结账。
        /// </summary>
        private void FinishWithoutBillOrCheckout(LordJob_CustomerVisit lordJob, int pawnId, string reason)
        {
            if (lordJob == null || pawn == null) return;
            lordJob.EnsureCustomerBill(pawnId);
            if (!lordJob.HasAnyBill(pawnId))
                lordJob.FinishZeroBillCustomerAndLeave(pawn, reason);
            else
                lordJob.MarkPawnReadyForCheckout(pawnId);
        }

        /// <summary>
        /// 记录一次无进展货柜浏览，负责在连续空逛达到阈值后推进顾客结账。
        /// </summary>
        private bool RegisterNoProgressAndCheckoutIfNeeded(LordJob_CustomerVisit lordJob, ref bool noProgressRecorded)
        {
            if (lordJob == null || pawn == null) return false;
            if (!noProgressRecorded)
            {
                lordJob.RegisterCurrentShopNoProgressBrowse(pawn);
                noProgressRecorded = true;
            }
            if (!lordJob.HasCompletedCurrentShopMinimumBrowse(pawn)) return false;
            if (!lordJob.HasReachedCurrentShopBrowseLimit(pawn) && !lordJob.HasReachedCurrentShopNoProgressLimit(pawn)) return false;

            int pawnId = pawn.thingIDNumber;
            lordJob.EnsureCustomerBill(pawnId);
            FinishWithoutBillOrCheckout(lordJob, pawnId, "顾客连续浏览无进展，离店");
            return true;
        }
    }
}
