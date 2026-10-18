using SimManagementLib.SimDef;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //执行顾客未购买前的橱窗浏览，职责是让无合适商品的顾客完成店内体验。
    public class JobDriver_WindowShop : JobDriver
    {
        private const int DefaultBrowseTicks = 300;

        //预约橱窗浏览目标，职责是复核站位并在目标被占用时选择其他店内空格。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            LocalTargetInfo target = job.GetTarget(TargetIndex.A);
            if (!target.IsValid) return false;
            if (target.HasThing)
                return true;
            if (!CustomerWindowShopTargetUtility.CanUseCell(pawn, target.Cell))
            {
                var lordJob = pawn.GetLord()?.LordJob as LordJob_CustomerVisit;
                if (!CustomerWindowShopTargetUtility.TryFindCell(pawn, lordJob?.GetCurrentShop(pawn), out IntVec3 cell))
                    return false;
                job.SetTarget(TargetIndex.A, cell);
                target = cell;
            }
            return pawn.ReserveSittableOrSpot(target.Cell, job, errorOnFailed);
        }

        //创建橱窗浏览流程，职责是移动、等待、展示进度并提交离店前反馈。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !job.GetTarget(TargetIndex.A).IsValid);

            if (job.GetTarget(TargetIndex.A).HasThing)
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            else
                yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            int browseTicks = GetBrowseTicks();
            Toil browse = Toils_General.Wait(browseTicks);
            browse.initAction = () =>
            {
                LordJob_CustomerVisit lordJob = pawn.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                lordJob?.GetOrCreateSession(pawn)?.NotifyBrowseStarted(lordJob, pawn);
                lordJob?.RecordCurrentShopStorageVisit(pawn, job.GetTarget(TargetIndex.A).Thing as Building_SimContainer);
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseStart);
            };
            browse.tickAction = () =>
            {
                ShopProgressBarUtility.Report(pawn, 1f - ticksLeftThisToil / (float)browseTicks);
            };
            browse.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            yield return browse;

            Toil finish = new Toil();
            finish.initAction = () =>
            {
                LordJob_CustomerVisit lordJob = pawn.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                int pawnId = pawn.thingIDNumber;
                lordJob?.MarkCurrentShopBrowsed(pawn);
                lordJob?.GetOrCreateSession(pawn)?.NotifyNoProgressBrowse(lordJob, pawn, "橱窗浏览没有合适商品");
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseNoMatch);
                ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.NoSuitableGoods"), new Color(0.88f, 0.88f, 0.88f));

                if (lordJob != null)
                {
                    lordJob.EnsureCustomerBill(pawnId);
                    if (!lordJob.HasAnyBill(pawnId))
                        lordJob.FinishZeroBillCustomerAndLeave(pawn, "橱窗浏览后没有合适商品，顾客离店");
                    else
                        lordJob.MarkPawnReadyForCheckout(pawnId);
                }
            };
            yield return finish;
        }

        //返回本次橱窗浏览时长，职责是复用顾客类型中的浏览时间配置。
        private int GetBrowseTicks()
        {
            if (job != null && job.count > 0)
                return Mathf.Clamp(job.count, 60, 2500);

            LordJob_CustomerVisit lordJob = pawn.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            ShoppingBehaviorProps behavior = lordJob?.GetShoppingBehavior();
            if (behavior == null) return DefaultBrowseTicks;
            int ticks = behavior.browseTimeRange.RandomInRange;
            return Mathf.Max(60, ticks);
        }
    }
}
