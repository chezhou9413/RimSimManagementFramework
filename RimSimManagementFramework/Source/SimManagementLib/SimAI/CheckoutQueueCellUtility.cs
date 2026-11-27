using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    //提供收银服务格和排队格计算，负责让所有顾客使用同一个服务锚点并按顺序站位。
    public static class CheckoutQueueCellUtility
    {
        //按实际占地找到员工正对面的顾客格，临时站人不改变服务锚点。
        public static IntVec3 FindServiceCell(Building_CashRegister register, Pawn pawn)
        {
            Map map = register.Map;
            CellRect occupied = register.OccupiedRect();
            IntVec3 cashierCell = register.InteractionCell;
            IntVec3 mirrored;
            if (cashierCell.z < occupied.minZ)
                mirrored = new IntVec3(Mathf.Clamp(cashierCell.x, occupied.minX, occupied.maxX), 0, occupied.maxZ + 1);
            else if (cashierCell.z > occupied.maxZ)
                mirrored = new IntVec3(Mathf.Clamp(cashierCell.x, occupied.minX, occupied.maxX), 0, occupied.minZ - 1);
            else if (cashierCell.x < occupied.minX)
                mirrored = new IntVec3(occupied.maxX + 1, 0, cashierCell.z);
            else if (cashierCell.x > occupied.maxX)
                mirrored = new IntVec3(occupied.minX - 1, 0, cashierCell.z);
            else
                return IntVec3.Invalid;

            if (IsServiceCellStructurallyUsable(map, mirrored, pawn))
                return mirrored;

            //正对面被固定障碍挡住时，只沿同一侧柜沿选择最近的空格。
            bool alongX = mirrored.z < occupied.minZ || mirrored.z > occupied.maxZ;
            int min = alongX ? occupied.minX : occupied.minZ;
            int max = alongX ? occupied.maxX : occupied.maxZ;
            IntVec3 best = IntVec3.Invalid;
            int bestDistance = int.MaxValue;
            for (int offset = min; offset <= max; offset++)
            {
                IntVec3 cell = alongX ? new IntVec3(offset, 0, mirrored.z) : new IntVec3(mirrored.x, 0, offset);
                int distance = cell.DistanceToSquared(mirrored);
                if (distance >= bestDistance || !IsServiceCellStructurallyUsable(map, cell, pawn)) continue;
                best = cell;
                bestDistance = distance;
            }
            return best;
        }
        //按当前排队序号计算目标等待格，前排离开时允许后排稳步前移修正队形，同时避免同距摆动或越位插队。
        public static IntVec3 FindQueueCell(Building_CashRegister register, IntVec3 serviceCell, int queueIndex, Pawn pawn)
        {
            if (!serviceCell.IsValid) return IntVec3.Invalid;
            Map map = register.Map;
            CustomerCheckoutQueueRegistry queue = map.GetComponent<CustomerArrivalManager>()?.CheckoutQueue;
            if (queue?.Acquire(pawn, register) == null) return IntVec3.Invalid;

            IntVec3 laneDir = GetQueueLaneDirection(register, serviceCell);
            //队首已进入服务格（不持有等待格）时，第二位紧跟服务格后 1 格；队首也在等待格时整体顺延 1 格。
            int slotOffset = queueIndex == 0 || queue.IsHeadHoldingWaitingCell(register)
                ? queueIndex + 1
                : queueIndex;
            int distanceFromService = Mathf.Clamp(slotOffset, 1, 8);
            IntVec3 preferred = serviceCell + laneDir * distanceFromService;

            IntVec3 current = queue.WaitingCellFor(pawn.thingIDNumber);
            bool currentValid = IsWaitingLocation(register, current, serviceCell, pawn) && pawn.CanReserve(current);
            //已在当前序号的理想位置时直接保持，路人临时经过不会打乱队形。
            if (currentValid && current == preferred)
                return current;

            if (!currentValid)
                queue.ReleaseWaitingCell(pawn.thingIDNumber);

            IntVec3 best = currentValid ? current : IntVec3.Invalid;
            int bestAheadPenalty = currentValid ? QueueAheadPenalty(current - serviceCell, laneDir, distanceFromService) : int.MaxValue;
            int bestDistance = currentValid ? current.DistanceToSquared(preferred) : int.MaxValue;
            int bestLateral = currentValid ? LateralOffsetSquared(current - serviceCell, laneDir) : int.MaxValue;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(serviceCell, 8f, false))
            {
                int aheadPenalty = QueueAheadPenalty(cell - serviceCell, laneDir, distanceFromService);
                if (aheadPenalty > bestAheadPenalty) continue;
                int distance = cell.DistanceToSquared(preferred);
                if (aheadPenalty == bestAheadPenalty && distance > bestDistance) continue;
                int lateral = LateralOffsetSquared(cell - serviceCell, laneDir);
                if (aheadPenalty == bestAheadPenalty && distance == bestDistance && lateral >= bestLateral) continue;
                if (!IsWaitingLocation(register, cell, serviceCell, pawn)
                    || !IsQueueSlotAvailable(cell, map, pawn, serviceCell, queue)
                    || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                best = cell;
                bestAheadPenalty = aheadPenalty;
                bestDistance = distance;
                bestLateral = lateral;
            }

            if (best.IsValid)
            {
                if (best != current) queue.SetWaitingCell(pawn.thingIDNumber, best);
                return best;
            }
            //空间已满时保留排队票据，不让等待者退回服务格。
            return IntVec3.Invalid;
        }

        //优先选择不越过当前序号深度的位置，避免后排在异步 Tick 中抢占前排刚空出的格子。
        private static int QueueAheadPenalty(IntVec3 offset, IntVec3 laneDir, int targetDepth)
        {
            int depth = offset.x * laneDir.x + offset.z * laneDir.z;
            return depth < targetDepth ? 1 : 0;
        }

        //计算候选格相对队列主轴的横向偏移平方，同距离时优先排成直线队形。
        private static int LateralOffsetSquared(IntVec3 offset, IntVec3 laneDir)
        {
            int lateral = laneDir.x == 0 ? offset.x : offset.z;
            return lateral * lateral;
        }

        //检查排队格是否可进入；允许接续前排正在离开的格子，但仍避开静止人物与其他排队票据。
        private static bool IsQueueSlotAvailable(IntVec3 cell, Map map, Pawn pawn, IntVec3 serviceCell, CustomerCheckoutQueueRegistry queue)
        {
            if (cell == serviceCell) return false;
            if (!IsServiceCellStructurallyUsable(map, cell, pawn)) return false;
            if (!pawn.CanReserve(cell) || !map.pawnDestinationReservationManager.CanReserve(cell, pawn)
                || queue.IsWaitingCellTaken(cell, pawn.thingIDNumber))
                return false;

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Pawn other) || other == pawn) continue;
                if (other.pather?.Moving == true && other.pather.Destination.Cell != cell) continue;
                return false;
            }
            return true;
        }

        //只允许队首预约服务格，取得服务位置后释放原等待位置。
        public static bool TryReserveServiceCell(Building_CashRegister register, IntVec3 cell, Pawn pawn, Job job)
        {
            CustomerCheckoutQueueRegistry queue = register.Map.GetComponent<CustomerArrivalManager>()?.CheckoutQueue;
            if (queue?.IsHead(pawn.thingIDNumber) != true || !IsServiceCellStructurallyUsable(register.Map, cell, pawn))
                return false;
            if (!pawn.HasReserved(cell, job) && (!IsServiceCellFreeForPawn(register.Map, cell, pawn)
                || !pawn.Reserve(cell, job, errorOnFailed: false))) return false;
            queue.ReleaseWaitingCell(pawn.thingIDNumber);
            return true;
        }

        //排队只使用顾客侧的地面，不占门口、柜台本体和员工交互格。
        private static bool IsWaitingLocation(Building_CashRegister register, IntVec3 cell, IntVec3 serviceCell, Pawn pawn)
        {
            if (!IsServiceCellStructurallyUsable(register.Map, cell, pawn) || cell == serviceCell
                || cell == register.InteractionCell || cell.GetEdifice(register.Map) != null) return false;
            IntVec3 direction = GetQueueLaneDirection(register, serviceCell);
            IntVec3 offset = cell - serviceCell;
            return offset.x * direction.x + offset.z * direction.z >= 0;
        }

        //判断服务格是否结构可用，允许无指定人物时用于商店入口锚点查询。
        public static bool IsServiceCellStructurallyUsable(Map map, IntVec3 cell, Pawn pawn)
        {
            if (!cell.IsValid) return false;
            if (!cell.InBounds(map)) return false;
            if (!cell.Standable(map)) return false;
            if (pawn != null && cell.IsForbidden(pawn)) return false;
            return true;
        }
        //判断服务格当前是否可由指定顾客进入；只避让正在办理业务的静止顾客，避免路过殖民者或趴在柜前的宠物永久卡死整条队伍。
        public static bool IsServiceCellFreeForPawn(Map map, IntVec3 cell, Pawn pawn)
        {
            if (!IsServiceCellStructurallyUsable(map, cell, pawn)) return false;

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Pawn other) || other == pawn) continue;
                if (!other.RaceProps.Humanlike) continue;
                if (other.pather?.Moving == true) continue;
                string jobDefName = other.CurJobDef?.defName;
                if (jobDefName == "Customer_PayAtRegister"
                    || jobDefName == "RSH_Reception"
                    || jobDefName == "RSH_UseSpa")
                    return false;
            }

            return true;
        }
        //选点时同时避开人物、工作预约、行走目的地和其他票据持有的位置。
        public static bool IsWaitingCellUsable(IntVec3 cell, Map map, Pawn pawn, IntVec3 serviceCell)
        {
            if (cell == serviceCell) return false;
            if (!IsServiceCellStructurallyUsable(map, cell, pawn)) return false;
            if (!pawn.CanReserve(cell) || !map.pawnDestinationReservationManager.CanReserve(cell, pawn)
                || map.GetComponent<CustomerArrivalManager>()?.CheckoutQueue.IsWaitingCellTaken(cell, pawn.thingIDNumber) == true)
                return false;

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && other != pawn) return false;
            }

            return true;
        }
        //沿柜台边缘的垂直方向排队，避免员工横向偏移把队列带向斜角。
        private static IntVec3 GetQueueLaneDirection(Building_CashRegister register, IntVec3 serviceCell)
        {
            CellRect occupied = register.OccupiedRect();
            if (serviceCell.z < occupied.minZ) return new IntVec3(0, 0, -1);
            if (serviceCell.z > occupied.maxZ) return new IntVec3(0, 0, 1);
            if (serviceCell.x < occupied.minX) return new IntVec3(-1, 0, 0);
            return new IntVec3(1, 0, 0);
        }
    }
}
