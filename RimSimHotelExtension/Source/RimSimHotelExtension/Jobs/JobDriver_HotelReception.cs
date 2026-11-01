using System.Collections.Generic;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Jobs
{
    //在服务台另一侧排队，与值班接待员办理入住、续住或退房。
    public sealed class JobDriver_HotelReception : JobDriver
    {
        private int servedTicks;
        private int waitStartTick = -1;
        private int serviceTicksAtWaitStart;
        private Building_HotelDesk Desk => job.targetA.Thing as Building_HotelDesk;

        //顾客只预约服务格，避免与正在值班的员工争抢收银台建筑。
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);

        //保存已经得到的服务时间，读档后不会重置队列等待。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref servedTicks, "servedTicks");
            Scribe_Values.Look(ref waitStartTick, "waitStartTick", -1);
            Scribe_Values.Look(ref serviceTicksAtWaitStart, "serviceTicksAtWaitStart");
        }

        //先到前台，再仅在员工在岗时推进实际接待。
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            Toil prepare = ToilMaker.MakeToil();
            prepare.initAction = () =>
            {
                var manager = HotelMapComponent.For(pawn.Map);
                if (manager.FindStay(pawn) != null) return;
                var shop = SimShopServiceApi.FindShop(pawn.Map, Desk.Position);
                if (shop == null || !manager.TryCheckInExisting(pawn, shop)) EndJobWith(JobCondition.Incompletable);
            };
            yield return prepare;
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil service = ToilMaker.MakeToil();
            service.defaultCompleteMode = ToilCompleteMode.Never;
            service.initAction = () =>
            {
                waitStartTick = Find.TickManager.TicksGame;
                serviceTicksAtWaitStart = Desk.CustomerServiceTicks;
            };
            service.tickAction = () =>
            {
                var manager = HotelMapComponent.For(pawn.Map);
                var stay = manager.FindStay(pawn);
                if (stay == null) { EndJobWith(JobCondition.Succeeded); return; }
                pawn.rotationTracker.FaceTarget(Desk);
                if (Desk.GetUnservedWaitTicks(waitStartTick, serviceTicksAtWaitStart)
                    > HotelTuningDef.Current.receptionTimeout)
                {
                    if (stay.active && !stay.closing)
                    {
                        stay.renewalRequested = false;
                        stay.renewalChecked = true;
                    }
                    else manager.RequestCheckout(stay, "RSH.Text.ReceptionWaitTimedOut");
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (!Desk.IsManned) return;
                Desk.NotifyCustomerService();
                servedTicks++;
                int needed = stay.active || stay.closing ? HotelTuningDef.Current.returnReceptionTicks : HotelTuningDef.Current.receptionTicks;
                if (servedTicks < needed) return;
                manager.CompleteReception(pawn);
                ReadyForNextToil();
            };
            yield return service;
        }
    }
}
