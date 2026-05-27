using RimWorld;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 提供收银服务格和排队格计算，负责让所有顾客使用同一个服务锚点并按顺序站位。
    /// </summary>
    public static class CheckoutQueueCellUtility
    {
        /// <summary>
        /// 查找顾客与收银台交互时使用的服务格，负责避免临时站人的排队状态改变服务锚点。
        /// </summary>
        public static IntVec3 FindServiceCell(Building_CashRegister register, Pawn pawn)
        {
            Map map = register.Map;
            IntVec3 cashierCell = register.InteractionCell;
            IntVec3 delta = cashierCell - register.Position;
            IntVec3 mirrored = register.Position - new IntVec3(Mathf.Clamp(delta.x, -1, 1), 0, Mathf.Clamp(delta.z, -1, 1));

            if (IsServiceCellStructurallyUsable(map, mirrored, pawn))
                return mirrored;

            if (CellFinder.TryFindRandomCellNear(register.Position, map, 3, c => IsServiceCellStructurallyUsable(map, c, pawn), out IntVec3 found))
                return found;

            if (IsServiceCellStructurallyUsable(map, register.Position, pawn))
                return register.Position;

            return pawn.Position;
        }

        /// <summary>
        /// 按服务格和队列序号查找等待格，负责让等待队列避开真正执行收银的服务格。
        /// </summary>
        public static IntVec3 FindQueueCell(Building_CashRegister register, IntVec3 serviceCell, int queueIndex, Pawn pawn)
        {
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

        /// <summary>
        /// 判断服务格是否从地形和可达性上可用，负责忽略临时站人的队列状态。
        /// </summary>
        public static bool IsServiceCellStructurallyUsable(Map map, IntVec3 cell, Pawn pawn)
        {
            if (!cell.IsValid) return false;
            if (!cell.InBounds(map)) return false;
            if (!cell.Standable(map)) return false;
            if (cell.IsForbidden(pawn)) return false;
            if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return false;
            return true;
        }

        /// <summary>
        /// 判断服务格当前是否可由指定顾客进入，负责在真正轮到结账时避免踩到其他 Pawn。
        /// </summary>
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

        /// <summary>
        /// 判断等待格是否可用，负责让排队 Pawn 不占用服务格和其他 Pawn 的当前位置。
        /// </summary>
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

        /// <summary>
        /// 计算排队方向，负责让等待队列沿着顾客服务格背向收银台排开。
        /// </summary>
        private static IntVec3 GetQueueLaneDirection(Building_CashRegister register, IntVec3 serviceCell)
        {
            IntVec3 laneDir = serviceCell - register.Position;
            laneDir = new IntVec3(Mathf.Clamp(laneDir.x, -1, 1), 0, Mathf.Clamp(laneDir.z, -1, 1));
            if (!laneDir.IsValid || (laneDir.x == 0 && laneDir.z == 0))
                laneDir = register.Rotation.FacingCell;
            return laneDir;
        }
    }
}
