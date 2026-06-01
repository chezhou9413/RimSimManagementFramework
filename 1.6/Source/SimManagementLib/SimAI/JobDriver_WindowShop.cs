using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行顾客未购买前的橱窗浏览，负责让无合适商品的顾客也先进入店内体验。
    /// </summary>
    public class JobDriver_WindowShop : JobDriver
    {
        private const int DefaultBrowseTicks = 300;

        /// <summary>
        /// 预约橱窗浏览目标，负责避免多人同时挤占同一个可站立点。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            LocalTargetInfo target = job.GetTarget(TargetIndex.A);
            if (!target.IsValid) return true;
            if (target.HasThing)
                return pawn.Reserve(target.Thing, job, 24, 0, null, errorOnFailed);
            return pawn.Reserve(target.Cell, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>
        /// 创建橱窗浏览流程，负责移动、等待、进度展示和离店前反馈。
        /// </summary>
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
                lordJob?.RegisterCurrentShopBrowseAttempt(pawn);
                lordJob?.RegisterCurrentShopNoProgressBrowse(pawn);
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseNoMatch);
                ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.NoSuitableGoods"), new Color(0.88f, 0.88f, 0.88f));

                if (lordJob != null)
                {
                    if (!lordJob.cartValues.ContainsKey(pawnId))
                        lordJob.cartValues[pawnId] = 0f;
                    lordJob.MarkPawnReadyForCheckout(pawnId);
                }
            };
            yield return finish;
        }

        /// <summary>
        /// 返回本次橱窗浏览时长，负责复用顾客类型中的浏览时间配置。
        /// </summary>
        private int GetBrowseTicks()
        {
            LordJob_CustomerVisit lordJob = pawn.Map?.lordManager?.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            ShoppingBehaviorProps behavior = lordJob?.GetShoppingBehavior();
            if (behavior == null) return DefaultBrowseTicks;
            int ticks = behavior.browseTimeRange.RandomInRange;
            return Mathf.Max(60, ticks);
        }
    }
}
