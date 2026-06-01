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
    /// <summary>
    /// 执行顾客到货柜浏览、选择商品、扣减虚拟库存并写入购物车的流程。
    /// </summary>
    public class JobDriver_BrowseAndPick : JobDriver
    {
        private const int MaxShelfReservations = 24;
        private Building_SimContainer TargetShelf => (Building_SimContainer)job.GetTarget(TargetIndex.A).Thing;

        /// <summary>
        /// 预约目标货柜，负责允许多名顾客同时浏览同一货柜但仍通过原版预约系统控制上限。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetShelf, job, MaxShelfReservations, 0, null, errorOnFailed);
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
                lordJob?.MarkCurrentShopBrowsed(pawn);
                lordJob?.RegisterCurrentShopBrowseAttempt(pawn);
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
                if (!lordJob.cartValues.ContainsKey(pId))
                    lordJob.cartValues[pId] = 0f;

                Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
                float remainingBudget = lordJob.GetRemainingTripBudget(pawn, shopZone);
                bool noProgressRecorded = false;

                if (remainingBudget <= 0f)
                {
                    RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded);
                    lordJob.MarkPawnReadyForCheckout(pId);
                    return;
                }

                if (shopZone != null)
                {
                    List<ComboData> combos = CustomerShoppingMatchUtility.GetMatchingAffordableInStockCombos(shopZone, lordJob, remainingBudget);
                    List<CustomerCartItem> currentCart = lordJob.GetCartItems(pId);
                    if (!combos.NullOrEmpty())
                    {
                        ComboData targetCombo = combos
                            .OrderByDescending(c => GetComboPreferenceScore(lordJob, pId, c))
                            .ThenByDescending(c => c.totalPrice)
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

                            lordJob.cartValues[pId] += comboPrice;
                            lordJob.AddCartItemsFromCombo(pId, targetCombo.items);
                            lordJob.ClearCurrentShopNoProgressBrowse(pawn);
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
                            if (lordJob.RegisterConsumptionActionAndShouldCheckout(pId) || lordJob.ShouldCheckoutFromCurrentShop(pawn, shopZone, "套餐购买完成"))
                                lordJob.MarkPawnReadyForCheckout(pId);
                            return;
                        }

                        if (RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded))
                            return;
                    }
                }

                List<(ThingDef def, float unitPrice)> candidates = new List<(ThingDef def, float unitPrice)>();
                foreach (ThingDef def in TargetShelf.ActiveDefs)
                {
                    if (TargetShelf.CountStored(def) <= 0) continue;
                    if (!CustomerShoppingMatchUtility.ThingMatchesCustomer(lordJob, def)) continue;

                    float unitPrice = ShopPricingUtility.GetUnitPrice(TargetShelf, def);
                    if (unitPrice <= remainingBudget)
                    {
                        candidates.Add((def, unitPrice));
                    }
                }

                if (candidates.NullOrEmpty())
                {
                    bool shouldCheckout = RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded);
                    CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseNoMatch);
                    ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.NoSuitableGoods"), new Color(0.88f, 0.88f, 0.88f));
                    if (shouldCheckout)
                        lordJob.MarkPawnReadyForCheckout(pId);
                    return;
                }

                (ThingDef itemToBuy, float itemPrice) = candidates.RandomElementByWeight(c =>
                    Mathf.Max(0.1f, lordJob.GetPreferenceMultiplier(pId, c.def)));

                int maxByBudget = Mathf.FloorToInt(remainingBudget / itemPrice);
                int maxByStock = TargetShelf.CountStored(itemToBuy);
                int maxCount = Mathf.Min(maxByBudget, maxByStock);
                if (maxCount <= 0)
                {
                    if (RegisterNoProgressAndCheckoutIfNeeded(lordJob, ref noProgressRecorded))
                        lordJob.MarkPawnReadyForCheckout(pId);
                    return;
                }
                int buyCount = PickPurchaseCount(maxCount);

                Thing taken = TargetShelf.TryVirtualBuy(itemToBuy, buyCount, out _);
                if (taken != null)
                {
                    int actualCount = Mathf.Max(1, taken.stackCount);
                    float totalPrice = itemPrice * actualCount;
                    float totalCost = Mathf.Max(0f, itemToBuy.BaseMarketValue * actualCount);
                    lordJob.cartValues[pId] += totalPrice;
                    lordJob.AddCartItem(pId, itemToBuy, actualCount);
                    lordJob.ClearCurrentShopNoProgressBrowse(pawn);
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
                        lordJob.MarkPawnReadyForCheckout(pId);
                    return;
                }

                if (lordJob.RegisterConsumptionActionAndShouldCheckout(pId) || lordJob.ShouldCheckoutFromCurrentShop(pawn, shopZone, "商品购买完成"))
                    lordJob.MarkPawnReadyForCheckout(pId);
            };

            yield return pickItem;
        }

        /// <summary>
        /// 计算套餐对指定顾客的偏好得分，负责让更符合偏好的套餐优先被购买。
        /// </summary>
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

        /// <summary>
        /// 选择单次购买数量，负责让顾客稳定购买但避免一次搬空整柜库存。
        /// </summary>
        private static int PickPurchaseCount(int maxCount)
        {
            if (maxCount <= 1) return 1;

            int softMax = Mathf.Min(maxCount, 4);
            return Rand.RangeInclusive(1, softMax);
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
            if (!lordJob.cartValues.ContainsKey(pawnId))
                lordJob.cartValues[pawnId] = 0f;
            lordJob.MarkPawnReadyForCheckout(pawnId);
            return true;
        }
    }
}
