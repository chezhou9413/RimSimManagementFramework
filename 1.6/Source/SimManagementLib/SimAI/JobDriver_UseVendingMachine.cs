using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行顾客在自动售货机前选择商品、扣库存、付款和离开的流程。
    /// </summary>
    public class JobDriver_UseVendingMachine : JobDriver
    {
        private const TargetIndex MachineInd = TargetIndex.A;
        private const int BrowseTicks = 240;
        private const int PayTicks = 90;

        private Building_SimContainer Machine => job.GetTarget(MachineInd).Thing as Building_SimContainer;

        /// <summary>
        /// 顾客不独占机器，机器并发由刷客容量控制。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        /// <summary>
        /// 构建前往机器、浏览、付款和完成访问的 Toil 序列。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MachineInd);

            yield return Toils_Goto.GotoThing(MachineInd, PathEndMode.Touch);
            yield return MakeTimedToil("UseVendingMachineBrowse", BrowseTicks, new Color(0.55f, 0.82f, 1f, 0.95f));
            yield return MakePurchaseToil();
            yield return MakeTimedToil("UseVendingMachinePay", PayTicks, new Color(0.95f, 0.78f, 0.34f, 0.95f));
            yield return MakeFinishToil();
        }

        /// <summary>
        /// 创建带模拟经营读条的等待 Toil。
        /// </summary>
        private Toil MakeTimedToil(string debugName, int ticks, Color color)
        {
            Toil toil = ToilMaker.MakeToil(debugName);
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.initAction = () => ticksLeftThisToil = ticks;
            toil.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Machine);
                ticksLeftThisToil--;
                ShopProgressBarUtility.Report(pawn, 1f - ticksLeftThisToil / (float)Mathf.Max(1, ticks), color);
                if (ticksLeftThisToil <= 0)
                    ReadyForNextToil();
            };
            toil.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            return toil;
        }

        /// <summary>
        /// 从机器库存中选择顾客买得起且有偏好的商品，并立即写入机器收入。
        /// </summary>
        private Toil MakePurchaseToil()
        {
            Toil toil = ToilMaker.MakeToil("UseVendingMachinePurchase");
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = delegate
            {
                LordJob_VendingMachineVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_VendingMachineVisit;
                Building_SimContainer machine = Machine;
                if (lordJob == null || machine == null || !VendingMachineUtility.IsUsableVendingMachine(machine))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (!VendingMachineUtility.TryPurchaseBestItem(pawn, lordJob, machine, out ThingDef boughtDef, out int count, out float paid, out float cost))
                {
                    CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseNoMatch);
                    ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.VendingNoSuitableGoods"), new Color(0.88f, 0.88f, 0.88f));
                    return;
                }

                int silver = Mathf.CeilToInt(paid);
                machine.GetComp<ThingComp_CashStorage>()?.DepositSilver(silver);
                GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
                finance?.CommitVendingMachineSale(pawn, machine, boughtDef, count, silver, cost);
                CustomerPurchaseDeliveryUtility.DeliverPurchasedItems(pawn, new List<CustomerCartItem>
                {
                    new CustomerCartItem { def = boughtDef, count = count }
                });
                lordJob.RecordDeliveredItem(pawn.thingIDNumber, boughtDef, count);
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.PurchaseItem);
                ShopBubbleUtility.ShowThingBubble(
                    pawn,
                    boughtDef,
                    count > 1
                        ? SimTranslation.T("RSMF.Bubble.VendingAutoBuyCount", boughtDef.label.Named("item"), count.Named("count"))
                        : SimTranslation.T("RSMF.Bubble.VendingAutoBuy", boughtDef.label.Named("item")),
                    null,
                    Color.white);
                ShopBubbleUtility.ShowSilverPayment(pawn, silver);
            };
            return toil;
        }

        /// <summary>
        /// 通知自动售货机访问 Lord 结束当前顾客流程。
        /// </summary>
        private Toil MakeFinishToil()
        {
            Toil toil = ToilMaker.MakeToil("UseVendingMachineFinish");
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = delegate
            {
                LordJob_VendingMachineVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_VendingMachineVisit;
                lordJob?.NotifyDone();
            };
            return toil;
        }
    }
}
