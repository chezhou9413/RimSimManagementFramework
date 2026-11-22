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

        //幂等认领订单并预约服务建筑，确保强制指令和启动阶段重复预约都能通过且中断时归还订单。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            PreparedShopOrder targetOrder = SimShopOrderApi.GetOrder(job.count);
            if (targetOrder == null)
                return false;

            bool assignedNow = false;
            if (targetOrder.state == PreparedShopOrderState.PaidWaitingPreparation)
            {
                if (!SimShopOrderApi.TryAssignOrder(pawn, targetOrder).success)
                    return false;
                assignedNow = true;
            }
            else if (targetOrder.state != PreparedShopOrderState.Assigned || targetOrder.staffThingId != pawn.thingIDNumber)
            {
                return false;
            }

            Thing provider = Provider;
            if (provider != null && !pawn.Reserve(provider, job, 1, -1, null, errorOnFailed))
            {
                if (assignedNow)
                {
                    targetOrder.state = PreparedShopOrderState.PaidWaitingPreparation;
                    targetOrder.staffThingId = -1;
                }
                return false;
            }

            order = targetOrder;
            return true;
        }

        //构建现做订单制作流程，并在中断未完成时把订单退回等待制作状态。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(_ => ReleaseOrderIfIncomplete());
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

        //制作中断时归还仍由当前员工认领的订单，避免订单永久卡在已分配状态。
        private void ReleaseOrderIfIncomplete()
        {
            PreparedShopOrder current = order ?? SimShopOrderApi.GetOrder(job.count);
            if (current == null || current.staffThingId != pawn.thingIDNumber)
                return;
            if (current.state == PreparedShopOrderState.Assigned || current.state == PreparedShopOrderState.Preparing)
            {
                current.state = PreparedShopOrderState.PaidWaitingPreparation;
                current.staffThingId = -1;
            }
        }
    }
}
