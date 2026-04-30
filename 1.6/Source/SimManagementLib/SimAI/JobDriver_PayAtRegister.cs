using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
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
    public class JobDriver_PayAtRegister : JobDriver
    {
        private const int ServiceTicks = 300;
        private const int DefaultMaxQueueWaitTicks = 2500;

        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;
        private IntVec3 QueueCell => job.GetTarget(TargetIndex.B).Cell;
        private IntVec3 ServiceCell => job.GetTarget(TargetIndex.C).Cell;

        private bool abortedByTimeout;
        private int totalWaitTicks;
        private int maxQueueWaitTicks = DefaultMaxQueueWaitTicks;
        private int serviceTicksRequired = ServiceTicks;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Customers do not reserve the register building itself.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            yield return MakeEnsureQueueAndServiceCellsToil();
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil waitInQueue = new Toil();
            waitInQueue.defaultCompleteMode = ToilCompleteMode.Never;
            waitInQueue.initAction = () =>
            {
                abortedByTimeout = false;
                totalWaitTicks = 0;
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                maxQueueWaitTicks = lordJob?.GetQueuePatienceForPawn(pawn.thingIDNumber) ?? DefaultMaxQueueWaitTicks;
                if (maxQueueWaitTicks <= 0) maxQueueWaitTicks = DefaultMaxQueueWaitTicks;
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutQueueStart);
            };
            waitInQueue.tickAction = () =>
            {
                totalWaitTicks++;
                FaceCashierOrRegister();

                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (totalWaitTicks >= maxQueueWaitTicks)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (QueueCell.IsValid && pawn.Position != QueueCell && pawn.IsHashIntervalTick(90))
                {
                    if (pawn.CanReach(QueueCell, PathEndMode.OnCell, Danger.Deadly))
                    {
                        pawn.pather.StartPath(QueueCell, PathEndMode.OnCell);
                    }
                }

                if (!Register.IsManned) return;
                if (!IsMyTurn(lordJob)) return;
                if (!IsServiceCellUsable(pawn.Map, ServiceCell, pawn)) return;

                ReadyForNextToil();
            };
            waitInQueue.WithProgressBar(TargetIndex.A, () => Mathf.Min(1f, totalWaitTicks / (float)Mathf.Max(1, maxQueueWaitTicks)));
            yield return waitInQueue;

            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);

            Toil doService = new Toil();
            doService.defaultCompleteMode = ToilCompleteMode.Never;
            doService.initAction = () =>
            {
                float cashierSpeed = GetCashierServiceSpeed();
                serviceTicksRequired = Mathf.Max(60, Mathf.RoundToInt(ServiceTicks / Mathf.Max(0.2f, cashierSpeed)));
                ticksLeftThisToil = serviceTicksRequired;
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutServiceStart);
            };
            doService.tickAction = () =>
            {
                totalWaitTicks++;
                FaceCashierOrRegister();

                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (totalWaitTicks >= maxQueueWaitTicks)
                {
                    abortedByTimeout = true;
                    ReadyForNextToil();
                    return;
                }

                if (pawn.Position != ServiceCell) return;
                if (!Register.IsManned) return;
                if (!IsMyTurn(lordJob)) return;

                ticksLeftThisToil--;
                if (ticksLeftThisToil <= 0)
                {
                    ReadyForNextToil();
                }
            };
            doService.WithProgressBar(TargetIndex.A, () => 1f - (ticksLeftThisToil / (float)Mathf.Max(1, serviceTicksRequired)));
            yield return doService;

            Toil finalize = new Toil();
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            finalize.initAction = () =>
            {
                LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
                if (lordJob == null) return;

                GameComponent_ShopFinanceManager finance = Current.Game?.GetComponent<GameComponent_ShopFinanceManager>();
                GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();

                int pawnId = pawn.thingIDNumber;
                int budget = lordJob.GetBudgetForPawn(pawnId);
                Zone_Shop shopZone = ShopDataUtility.FindAssignedShopZone(
                    pawn.Map,
                    lordJob.targetShopZoneId,
                    lordJob.targetShopCell);
                List<CustomerCartItem> purchasedItems = SnapshotCartItems(lordJob, pawnId);

                if (abortedByTimeout)
                {
                    HandleCheckoutTimeout(lordJob, finance, pawnId, shopZone);
                    analytics?.RecordCheckoutResult(shopZone, totalWaitTicks, maxQueueWaitTicks, 0, budget, success: false, timeout: true);
                    lordJob.CheckAllCheckoutsDone();
                    return;
                }

                if (lordJob.cartValues.TryGetValue(pawnId, out float amountOwed) && amountOwed > 0f)
                {
                    int silverAmount = Mathf.CeilToInt(amountOwed);
                    Register.DepositSilver(silverAmount);
                    finance?.CommitCheckout(pawn, Register, silverAmount);
                    PurchaseOutcomeResolver.TryQueuePostPurchaseJobs(pawn, lordJob, pawnId, shopZone, purchasedItems);
                    CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutPaid);
                    ShopBubbleUtility.ShowSilverPayment(pawn, silverAmount);
                    lordJob.ClearCustomerCart(pawnId);
                    analytics?.RecordCheckoutResult(shopZone, totalWaitTicks, maxQueueWaitTicks, silverAmount, budget, success: true, timeout: false);
                }
                else
                {
                    finance?.ClearPendingBill(pawn);
                    PurchaseOutcomeResolver.TryQueuePostPurchaseJobs(pawn, lordJob, pawnId, shopZone, purchasedItems);
                    lordJob.ClearCustomerCart(pawnId);
                    analytics?.RecordCheckoutResult(shopZone, totalWaitTicks, maxQueueWaitTicks, 0, budget, success: true, timeout: false);
                }

                lordJob.CheckAllCheckoutsDone();
            };
            yield return finalize;
        }

        private Toil MakeEnsureQueueAndServiceCellsToil()
        {
            Toil toil = new Toil();
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = () =>
            {
                IntVec3 service = ServiceCell;
                if (!IsServiceCellUsable(pawn.Map, service, pawn))
                {
                    service = FindServiceCell(pawn.Map);
                    job.SetTarget(TargetIndex.C, service);
                }

                IntVec3 queue = QueueCell;
                if (!IsServiceCellUsable(pawn.Map, queue, pawn))
                {
                    queue = service;
                    job.SetTarget(TargetIndex.B, queue);
                }
            };
            return toil;
        }

        private IntVec3 FindServiceCell(Map map)
        {
            IntVec3 cashierCell = Register.InteractionCell;
            IntVec3 delta = cashierCell - Register.Position;
            IntVec3 mirrored = Register.Position - new IntVec3(Mathf.Clamp(delta.x, -1, 1), 0, Mathf.Clamp(delta.z, -1, 1));

            if (IsServiceCellUsable(map, mirrored, pawn))
                return mirrored;

            if (CellFinder.TryFindRandomCellNear(Register.Position, map, 3, c => IsServiceCellUsable(map, c, pawn), out IntVec3 found))
                return found;

            if (IsServiceCellUsable(map, Register.Position, pawn))
                return Register.Position;

            return pawn.Position;
        }

        private static bool IsServiceCellUsable(Map map, IntVec3 cell, Pawn pawn)
        {
            if (!cell.IsValid) return false;
            if (!cell.InBounds(map)) return false;
            if (!cell.Standable(map)) return false;
            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return false;

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && other != pawn) return false;
            }
            return true;
        }

        private bool IsMyTurn(LordJob_CustomerVisit lordJob)
        {
            int myId = pawn.thingIDNumber;
            int myOrder = lordJob.EnsureCheckoutOrder(myId);

            IReadOnlyList<Pawn> pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == null || other == pawn) continue;
                if (other.CurJobDef == null || other.CurJobDef.defName != "Customer_PayAtRegister") continue;
                if (other.CurJob?.targetA.Thing != Register) continue;

                int otherId = other.thingIDNumber;
                if (!lordJob.cartValues.TryGetValue(otherId, out float otherOwed) || otherOwed <= 0f) continue;

                int otherOrder = lordJob.GetCheckoutOrder(otherId);
                if (otherOrder < myOrder)
                    return false;
            }

            return true;
        }

        private void HandleCheckoutTimeout(LordJob_CustomerVisit lordJob, GameComponent_ShopFinanceManager finance, int pawnId, Zone_Shop shopZone)
        {
            if (shopZone != null)
            {
                ShopDataUtility.ReturnCartItemsToShop(shopZone, lordJob.GetCartItems(pawnId));
            }

            finance?.ClearPendingBill(pawn);
            lordJob.ClearCustomerCart(pawnId);
            CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.CheckoutTimeout);
            ShopBubbleUtility.ShowTextBubble(pawn, "排队超时，已放回商品", new Color(1f, 0.72f, 0.4f));
        }

        private static List<CustomerCartItem> SnapshotCartItems(LordJob_CustomerVisit lordJob, int pawnId)
        {
            List<CustomerCartItem> raw = lordJob?.GetCartItems(pawnId);
            if (raw.NullOrEmpty()) return new List<CustomerCartItem>();

            return raw
                .Where(item => item != null && item.def != null && item.count > 0)
                .Select(item => new CustomerCartItem
                {
                    def = item.def,
                    count = item.count
                })
                .ToList();
        }

        private void FaceCashierOrRegister()
        {
            Pawn cashier = Register.CurrentCashier;
            if (cashier != null)
                pawn.rotationTracker.FaceTarget(cashier);
            else
                pawn.rotationTracker.FaceTarget(Register);
        }

        private float GetCashierServiceSpeed()
        {
            Pawn cashier = Register.CurrentCashier;
            if (cashier == null || cashier.Destroyed || cashier.Dead)
                return 1f;

            float workSpeed = cashier.GetStatValue(StatDefOf.WorkSpeedGlobal);
            float socialImpact = cashier.GetStatValue(StatDefOf.SocialImpact);
            return Mathf.Max(0.2f, workSpeed * Mathf.Max(0.5f, socialImpact));
        }
    }
}
