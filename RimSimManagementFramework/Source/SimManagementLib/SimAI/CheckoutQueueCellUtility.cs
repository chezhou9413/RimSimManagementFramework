using RimWorld;
using SimManagementLib.Tool;
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
        //按服务格和队列序号查找等待格，负责让等待队列避开真正执行收银的服务格。
        public static IntVec3 FindQueueCell(Building_CashRegister register, IntVec3 serviceCell, int queueIndex, Pawn pawn)
        {
            if (!serviceCell.IsValid) return IntVec3.Invalid;
            Map map = register.Map;
            IntVec3 laneDir = GetQueueLaneDirection(register, serviceCell);
            int distanceFromService = Mathf.Max(1, queueIndex + 1);
            IntVec3 preferred = serviceCell + laneDir * distanceFromService;

            if (IsWaitingCellUsable(preferred, map, pawn, serviceCell))
                return preferred;

            if (CellFinder.TryFindRandomCellNear(preferred, map, 3, c => IsWaitingCellUsable(c, map, pawn, serviceCell), out IntVec3 found))
                return found;

            if (IsWaitingCellUsable(pawn.Position, map, pawn, serviceCell))
                return pawn.Position;

            return serviceCell;
        }

        //判断服务格是否结构可用，职责是只检查常数时间条件并把真实路径交给 Job 路径器。
        public static bool IsServiceCellStructurallyUsable(Map map, IntVec3 cell, Pawn pawn)
        {
            if (!cell.IsValid) return false;
            if (!cell.InBounds(map)) return false;
            if (!cell.Standable(map)) return false;
            if (cell.IsForbidden(pawn)) return false;
            return true;
        }
        //判断服务格当前是否可由指定顾客进入，负责在真正轮到结账时避免踩到其他 Pawn。
        public static bool IsServiceCellFreeForPawn(Map map, IntVec3 cell, Pawn pawn)
        {
            if (!IsServiceCellStructurallyUsable(map, cell, pawn)) return false;

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn other && other != pawn) return false;
            }

            return true;
        }
        //判断等待格是否可用，负责让排队 Pawn 不占用服务格和其他 Pawn 的当前位置。
        public static bool IsWaitingCellUsable(IntVec3 cell, Map map, Pawn pawn, IntVec3 serviceCell)
        {
            if (cell == serviceCell) return false;
            if (!IsServiceCellStructurallyUsable(map, cell, pawn)) return false;

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
