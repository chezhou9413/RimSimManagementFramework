using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行员工制作现做订单的通用流程，负责走到服务建筑、读条制作并推进订单状态。
    /// </summary>
    public class JobDriver_PrepareShopOrder : JobDriver
    {
        private Thing Provider => job.GetTarget(TargetIndex.A).Thing;
        private PreparedShopOrder order;
        private PreparedShopOrderWorker worker;
        private int durationTicks = 600;

        /// <summary>
        /// 预约服务建筑，避免多个员工同时使用同一制作目标。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing provider = Provider;
            return provider == null || pawn.Reserve(provider, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>
        /// 构建现做订单制作流程。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil init = new Toil();
            init.defaultCompleteMode = ToilCompleteMode.Instant;
            init.initAction = () =>
            {
                order = SimShopOrderApi.GetOrder(job.count);
                worker = SimShopOrderApi.GetPreparedOrderWorker(order?.serviceDefName);
                if (order == null || worker == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                PreparedShopOrderResult startResult = SimShopOrderApi.StartPreparation(pawn, order);
                if (!startResult.success)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                durationTicks = Mathf.Max(60, worker.GetPrepareDurationTicks(pawn, order));
            };
            yield return init;

            if (Provider != null)
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil prepare = new Toil();
            prepare.defaultCompleteMode = ToilCompleteMode.Never;
            prepare.initAction = () => ticksLeftThisToil = durationTicks;
            prepare.tickAction = () =>
            {
                if (order == null || worker == null)
                {
                    ReadyForNextToil();
                    return;
                }

                float progress = 1f - ticksLeftThisToil / (float)Mathf.Max(1, durationTicks);
                ShopProgressBarUtility.Report(pawn, progress);
                worker.TickPreparation(pawn, order);
                pawn.skills?.Learn(SkillDefOf.Cooking, 0.04f);
                ticksLeftThisToil--;
                if (ticksLeftThisToil <= 0)
                    ReadyForNextToil();
            };
            prepare.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            yield return prepare;

            Toil finalize = new Toil();
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = () =>
            {
                if (order == null) return;
                SimShopOrderApi.CompletePreparation(pawn, order);
            };
            yield return finalize;
        }
    }
}
