using RimWorld;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 执行店员从经营建筑现金库存取出银币并搬运到仓储位置的工作。
    /// </summary>
    public class JobDriver_CollectCashRegisterSilver : JobDriver
    {
        private const TargetIndex CashBuildingInd = TargetIndex.A;
        private const TargetIndex SilverThingInd = TargetIndex.B;
        private const TargetIndex StoreCellInd = TargetIndex.C;
        private const float CashHandlingWorkRequired = 22f;

        private Thing CashBuilding => job.GetTarget(CashBuildingInd).Thing;
        private ThingComp_CashStorage CashStorage => CashBuilding?.TryGetComp<ThingComp_CashStorage>();
        private int ReservedCount => job.count;
        private bool reservationCleared;
        private bool silverReserved;

        /// <summary>
        /// 取现任务使用现金组件的金额预约，不独占收银台建筑，避免和值班收银员互相阻塞。
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        /// <summary>
        /// 初始化工作运行状态。
        /// </summary>
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            reservationCleared = false;
            silverReserved = false;
        }

        /// <summary>
        /// 构建从收银台取款、搬运和存放银币的 Toil 序列。
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(CashBuildingInd);
            AddFinishAction(_ =>
            {
                if (reservationCleared || !silverReserved) return;
                CashStorage?.CancelWithdrawReservation(ReservedCount);
                reservationCleared = true;
                silverReserved = false;
            });

            yield return Toils_Goto.GotoThing(CashBuildingInd, PathEndMode.Touch)
                .FailOnDestroyedOrNull(CashBuildingInd);

            yield return MakeReserveSilverToil();
            yield return MakeWorkToil("PrepareWithdrawCashRegisterSilver", CashHandlingWorkRequired);
            yield return MakeWithdrawToil();

            yield return Toils_Goto.GotoThing(SilverThingInd, PathEndMode.ClosestTouch)
                .FailOnDestroyedOrNull(SilverThingInd);

            yield return Toils_Haul.StartCarryThing(SilverThingInd, false, false, false, false);

            yield return MakeFindStoreCellToil();
            yield return Toils_Goto.GotoCell(StoreCellInd, PathEndMode.ClosestTouch);
            yield return MakeWorkToil("PrepareDropCashRegisterSilver", CashHandlingWorkRequired);
            yield return MakeDropCarriedToil();
        }

        /// <summary>
        /// 创建收银台取放银币前的准备工作读条。
        /// </summary>
        private Toil MakeWorkToil(string debugName, float workRequired)
        {
            float workDone = 0f;
            Toil toil = ToilMaker.MakeToil(debugName);
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.tickAction = delegate
            {
                workDone += pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                ShopProgressBarUtility.Report(pawn, Mathf.Clamp01(workDone / workRequired), new Color(0.95f, 0.82f, 0.42f, 0.95f));
                if (workDone >= workRequired)
                    ReadyForNextToil();
            };
            toil.AddFinishAction(() => ShopProgressBarUtility.Clear(pawn));
            return toil;
        }

        /// <summary>
        /// 在实际到达经营建筑后预约可取白银，负责避免工作扫描阶段失败时锁住现金库存。
        /// </summary>
        private Toil MakeReserveSilverToil()
        {
            Toil toil = ToilMaker.MakeToil("ReserveCashRegisterSilver");
            toil.initAction = delegate
            {
                ThingComp_CashStorage cash = CashStorage;
                if (cash == null || CashBuilding == null || CashBuilding.Destroyed)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int desired = ReservedCount > 0 ? ReservedCount : cash.AutoWithdrawAmount;
                int reserved = cash.ReserveWithdrawSilver(desired);
                if (reserved <= 0)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                job.count = reserved;
                silverReserved = true;
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// 从收银台取出已预约数量的银币并放到店员脚下。
        /// </summary>
        private Toil MakeWithdrawToil()
        {
            Toil toil = ToilMaker.MakeToil("WithdrawCashRegisterSilver");
            toil.initAction = delegate
            {
                ThingComp_CashStorage cash = CashStorage;
                if (cash == null || CashBuilding == null || CashBuilding.Destroyed)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                int silverCount = cash.WithdrawReservedSilver(ReservedCount);
                reservationCleared = true;
                silverReserved = false;

                if (silverCount <= 0)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = silverCount;
                if (!GenPlace.TryPlaceThing(silver, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing placedSilver) || placedSilver == null)
                {
                    cash.DepositSilver(silverCount);
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                job.SetTarget(SilverThingInd, placedSilver);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// 为取出的银币寻找合适的仓储格。
        /// </summary>
        private Toil MakeFindStoreCellToil()
        {
            Toil toil = ToilMaker.MakeToil("FindStoreCellForRegisterSilver");
            toil.initAction = delegate
            {
                IntVec3 targetCell = pawn.Position;
                Thing carried = pawn.carryTracker?.CarriedThing;

                if (carried != null)
                {
                    if (!StoreUtility.TryFindBestBetterStoreCellFor(carried, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out targetCell))
                    {
                        if (!StoreUtility.TryFindStoreCellNearColonyDesperate(carried, pawn, out targetCell))
                            targetCell = pawn.Position;
                    }
                }

                job.SetTarget(StoreCellInd, targetCell);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// 将店员携带的银币放到目标仓储格。
        /// </summary>
        private Toil MakeDropCarriedToil()
        {
            Toil toil = ToilMaker.MakeToil("DropCashRegisterSilver");
            toil.initAction = delegate
            {
                if (pawn.carryTracker?.CarriedThing == null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                IntVec3 storeCell = job.GetTarget(StoreCellInd).Cell;
                if (!storeCell.IsValid)
                    storeCell = pawn.Position;

                if (!pawn.carryTracker.TryDropCarriedThing(storeCell, ThingPlaceMode.Near, out Thing _))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}
