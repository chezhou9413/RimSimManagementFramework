using RimWorld;
using SimManagementLib.GameComp;
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

            int pawnId = pawn.thingIDNumber;
            if (!lordJob.cartValues.TryGetValue(pawnId, out float owed) || owed <= 0f)
            {
                if (lordJob.TryTakeNextPostCheckoutJob(pawnId, out Job postJob))
                {
                    return postJob;
                }

                if (lordJob.NeedsPostCheckoutCompletion(pawnId))
                {
                    lordJob.MarkPostCheckoutCompleted(pawnId);
                    lordJob.CheckAllCheckoutsDone();
                }

                return null;
            }

            Zone_Shop targetShop = ShopDataUtility.FindAssignedShopZone(pawn.Map, lordJob.targetShopZoneId, lordJob.targetShopCell);
            List<Building_CashRegister> registers = pawn.Map.listerBuildings.allBuildingsColonist
                .OfType<Building_CashRegister>()
                .Where(r => r != null && !r.Destroyed && r.Spawned)
                .Where(r => targetShop != null && targetShop.Cells.Contains(r.Position))
                .Where(r => pawn.CanReach(r, PathEndMode.Touch, Danger.Deadly))
                .ToList();

            if (registers.NullOrEmpty())
            {
                Current.Game?.GetComponent<GameComponent_ShopFinanceManager>()?.ClearPendingBill(pawn);
                if (targetShop != null)
                {
                    ShopDataUtility.ReturnCartItemsToShop(targetShop, lordJob.GetCartItems(pawnId));
                }
                lordJob.ResolveServiceOrdersOnCheckoutFailure(pawnId);
                lordJob.ClearCustomerCart(pawnId);
                lordJob.ClearCustomerServiceOrders(pawnId);
                lordJob.CheckAllCheckoutsDone();
                return null;
            }

            // 顾客进入结账阶段就分配固定排队顺序，保证先到先结。
            int myOrder = lordJob.EnsureCheckoutOrder(pawnId);

            Building_CashRegister register = registers
                .OrderByDescending(r => r.IsManned)
                .ThenBy(r => GetQueueSizeForRegister(pawn.Map, r))
                .ThenBy(r => (r.Position - pawn.Position).LengthHorizontalSquared)
                .FirstOrDefault();
            if (register == null) return null;

            int queueIndex = GetQueueIndexForPawn(pawn.Map, register, lordJob, pawnId, myOrder);
            IntVec3 serviceCell = FindServiceCell(register, pawn);
            IntVec3 queueCell = FindQueueCell(register, serviceCell, queueIndex, pawn);

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_PayAtRegister"), register);
            job.SetTarget(TargetIndex.B, queueCell);
            job.SetTarget(TargetIndex.C, serviceCell);
            return job;
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

        /// <summary>
        /// 查找顾客与收银台交互时使用的服务格。
        /// </summary>
        private static IntVec3 FindServiceCell(Building_CashRegister register, Pawn pawn)
        {
            Map map = register.Map;
            IntVec3 cashierCell = register.InteractionCell;
            IntVec3 delta = cashierCell - register.Position;
            IntVec3 mirrored = register.Position - new IntVec3(Mathf.Clamp(delta.x, -1, 1), 0, Mathf.Clamp(delta.z, -1, 1));

            if (IsValidQueueCell(mirrored, map, pawn))
                return mirrored;

            if (CellFinder.TryFindRandomCellNear(register.Position, map, 3, c => IsValidQueueCell(c, map, pawn), out IntVec3 found))
                return found;

            if (IsValidQueueCell(register.Position, map, pawn))
                return register.Position;

            return pawn.Position;
        }

        /// <summary>
        /// 按服务格和队列序号查找顾客应该等待的排队格。
        /// </summary>
        private static IntVec3 FindQueueCell(Building_CashRegister register, IntVec3 serviceCell, int queueIndex, Pawn pawn)
        {
            Map map = register.Map;
            IntVec3 laneDir = serviceCell - register.Position;
            laneDir = new IntVec3(Mathf.Clamp(laneDir.x, -1, 1), 0, Mathf.Clamp(laneDir.z, -1, 1));
            if (!laneDir.IsValid || (laneDir.x == 0 && laneDir.z == 0))
                laneDir = register.Rotation.FacingCell;

            IntVec3 preferred = serviceCell + laneDir * queueIndex;
            if (IsValidQueueCell(preferred, map, pawn))
                return preferred;

            if (CellFinder.TryFindRandomCellNear(preferred, map, 3, c => IsValidQueueCell(c, map, pawn), out IntVec3 found))
                return found;

            if (IsValidQueueCell(register.InteractionCell, map, pawn))
                return register.InteractionCell;

            return register.Position;
        }

        /// <summary>
        /// 判断指定格子是否适合作为排队或服务位置。
        /// </summary>
        private static bool IsValidQueueCell(IntVec3 cell, Map map, Pawn pawn)
        {
            if (!cell.InBounds(map)) return false;
            if (!cell.Standable(map)) return false;
            if (cell.IsForbidden(pawn)) return false;
            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return false;

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn) return false;
            }
            return true;
        }
    }
}
