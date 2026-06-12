using RimWorld;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 为进入结账阶段的顾客分配收银台排队 Job 或付款后的服务使用 Job。
    /// </summary>
    public class JobGiver_CustomerCheckout : ThinkNode_JobGiver
    {
        /// <summary>
        /// 根据顾客账单、购后队列和收银台状态决定下一项结账阶段工作。
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            Lord lord = pawn.Map.lordManager.LordOf(pawn);
            LordJob_CustomerVisit lordJob = lord?.LordJob as LordJob_CustomerVisit;
            if (lordJob == null) return null;
            CustomerVisitSession session = lordJob.GetOrCreateSession(pawn);
            bool isPostCheckout = session?.stage == CustomerVisitStage.PostCheckout;
            if (session == null || (!isPostCheckout && !session.AllowsJobGiver(CustomerVisitStage.Checkout)))
                return null;
            if (!isPostCheckout)
                session.NotifyCheckoutStarted(lordJob, pawn);

            int pawnId = pawn.thingIDNumber;
            float owed = lordJob.GetAmountOwedForCheckout(pawnId);
            if (owed <= 0f)
            {
                if (lordJob.TryTakeNextPostCheckoutJob(pawnId, out Job postJob))
                {
                    return postJob;
                }

                if (lordJob.NeedsPostCheckoutCompletion(pawnId))
                {
                    lordJob.MarkPostCheckoutCompleted(pawnId);
                    session.NotifyPostCheckoutCompleted(lordJob, pawn, "购后行为完成");
                    lordJob.CheckAllCheckoutsDone();
                }

                return null;
            }

            Zone_Shop targetShop = lordJob.GetCurrentShop(pawn);
            Building_CashRegister register = FindBestRegister(pawn, targetShop);
            if (register == null)
            {
                SimDebugLogger.Journey("RSMF.Checkout", $"没有找到可用收银台，取消本次结账 owed={owed}", pawn, targetShop, -1);
                lordJob.FailCheckoutAndLeave(pawn, SimTranslation.T("RSMF.Checkout.NoReachableRegister"));
                return null;
            }

            // 顾客进入结账阶段就分配固定排队顺序，保证先到先结。
            int myOrder = lordJob.EnsureCheckoutOrder(pawnId);

            int queueIndex = GetQueueIndexForPawn(pawn.Map, register, lordJob, pawnId, myOrder);
            IntVec3 serviceCell = CheckoutQueueCellUtility.FindServiceCell(register, pawn);
            IntVec3 queueCell = CheckoutQueueCellUtility.FindQueueCell(register, serviceCell, queueIndex, pawn);

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_PayAtRegister"), register);
            job.SetTarget(TargetIndex.B, queueCell);
            job.SetTarget(TargetIndex.C, serviceCell);
            return job;
        }

        /// <summary>
        /// 判断收银台是否属于目标商店，负责兼容建筑主体、交互格或占用格落在商店区内的情况。
        /// </summary>
        private static bool RegisterBelongsToShop(Building_CashRegister register, Zone_Shop shop)
        {
            if (register == null || shop == null) return false;
            if (shop.Cells.Contains(register.Position)) return true;
            if (register.InteractionCell.IsValid && shop.Cells.Contains(register.InteractionCell)) return true;
            foreach (IntVec3 cell in register.OccupiedRect())
            {
                if (shop.Cells.Contains(cell)) return true;
            }
            return false;
        }

        /// <summary>
        /// 选择当前最合适的可达收银台，负责避免在职责树热路径中创建临时列表和排序枚举器。
        /// </summary>
        private static Building_CashRegister FindBestRegister(Pawn pawn, Zone_Shop targetShop)
        {
            if (pawn?.Map == null || targetShop == null) return null;
            Building_CashRegister best = null;
            int bestQueue = int.MaxValue;
            int bestDistance = int.MaxValue;
            ConsiderRegisterList(pawn, targetShop, pawn.Map.listerBuildings.allBuildingsNonColonist, ref best, ref bestQueue, ref bestDistance);
            ConsiderRegisterList(pawn, targetShop, pawn.Map.listerBuildings.allBuildingsColonist, ref best, ref bestQueue, ref bestDistance);
            return best;
        }

        /// <summary>
        /// 遍历一组建筑并更新最佳收银台候选。
        /// </summary>
        private static void ConsiderRegisterList(Pawn pawn, Zone_Shop targetShop, List<Building> buildings, ref Building_CashRegister best, ref int bestQueue, ref int bestDistance)
        {
            if (buildings == null) return;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_CashRegister register = buildings[i] as Building_CashRegister;
                if (register == null || register.Destroyed || !register.Spawned) continue;
                if (!RegisterBelongsToShop(register, targetShop)) continue;
                if (!CustomerSafetyUtility.CanCustomerReach(pawn, register, PathEndMode.Touch, Danger.Deadly)) continue;

                int queue = GetQueueSizeForRegister(pawn.Map, register);
                int distance = (register.Position - pawn.Position).LengthHorizontalSquared;
                if (IsBetterRegister(register, queue, distance, best, bestQueue, bestDistance))
                {
                    best = register;
                    bestQueue = queue;
                    bestDistance = distance;
                }
            }
        }

        /// <summary>
        /// 判断候选收银台是否优于当前最佳收银台。
        /// </summary>
        private static bool IsBetterRegister(Building_CashRegister candidate, int queue, int distance, Building_CashRegister best, int bestQueue, int bestDistance)
        {
            if (best == null) return true;
            if (candidate.IsManned != best.IsManned) return candidate.IsManned;
            if (queue != bestQueue) return queue < bestQueue;
            return distance < bestDistance;
        }

        /// <summary>
        /// 统计指定收银台当前已有多少顾客正在排队或结账。
        /// </summary>
        private static int GetQueueSizeForRegister(Map map, Building_CashRegister register)
        {
            int count = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p?.CurJobDef == null) continue;
                if (p.CurJobDef.defName != "Customer_PayAtRegister") continue;
                if (p.CurJob?.targetA.Thing != register) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// 计算当前顾客在指定收银台前面还有多少更早进入队列的顾客。
        /// </summary>
        private static int GetQueueIndexForPawn(Map map, Building_CashRegister register, LordJob_CustomerVisit lordJob, int pawnId, int myOrder)
        {
            int ahead = 0;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p.thingIDNumber == pawnId) continue;
                if (p.CurJobDef == null || p.CurJobDef.defName != "Customer_PayAtRegister") continue;
                if (p.CurJob?.targetA.Thing != register) continue;

                int otherOrder = lordJob.GetCheckoutOrder(p.thingIDNumber);
                if (otherOrder < myOrder)
                    ahead++;
            }
            return ahead;
        }

    }
}
