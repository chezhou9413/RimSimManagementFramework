using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行付款后服务的通用使用流程，负责消耗服务资格并通知服务 Worker 完成状态。
    /// </summary>
    public class JobDriver_UsePaidService : JobDriver
    {
        private Thing Provider => job.GetTarget(TargetIndex.A).Thing;
        private IntVec3 UseCell => job.GetTarget(TargetIndex.B).Cell;
        private CustomerServiceOrder order;
        private ShopServiceDef serviceDef;
        private int durationTicks = 300;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing provider = Provider;
            if (provider == null) return false;
            return pawn.Reserve(provider, job, 24, 0, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            Toil init = new Toil();
            init.defaultCompleteMode = ToilCompleteMode.Instant;
            init.initAction = () =>
            {
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                order = lordJob?.GetServiceOrder(pawn.thingIDNumber, job.count);
                serviceDef = DefDatabase<ShopServiceDef>.GetNamedSilentFail(order?.serviceDefName);
                if (order == null || serviceDef == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                order.state = ServiceOrderState.InUse;
                order.startedTick = Find.TickManager.TicksGame;
                durationTicks = serviceDef.Worker.GetDurationTicks();
                serviceDef.Worker.NotifyServiceStarted(pawn, Provider, order);
            };
            yield return init;

            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil use = new Toil();
            use.defaultCompleteMode = ToilCompleteMode.Never;
            use.initAction = () =>
            {
                ticksLeftThisToil = Mathf.Max(60, durationTicks);
            };
            use.tickAction = () =>
            {
                float progress = 1f - ticksLeftThisToil / (float)Mathf.Max(1, durationTicks);
                ShopProgressBarUtility.Report(pawn, progress);
                serviceDef?.Worker.TickServiceUse(pawn, Provider, order);
                ticksLeftThisToil--;
                if (ticksLeftThisToil <= 0)
                    ReadyForNextToil();
            };
            use.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            yield return use;

            Toil finalize = new Toil();
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = () =>
            {
                if (order == null || serviceDef == null) return;
                order.completedTick = Find.TickManager.TicksGame;
                order.state = ServiceOrderState.Completed;
                serviceDef.Worker.NotifyServiceCompleted(pawn, Provider, order);
                ShopBubbleUtility.ShowTextBubble(pawn, SimTranslation.T("RSMF.Bubble.ServiceCompleted", serviceDef.DisplayLabel.Named("service")), new Color(0.55f, 0.85f, 1f));

                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                lordJob?.CheckAllCheckoutsDone();
            };
            yield return finalize;
        }
    }
}
