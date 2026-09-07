using System;
using System.Linq;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Models;
using SimManagementLib.SimMapComp;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Inventory
{
    //管理食材批次交接，职责是统一数量校验、柜前路径和携带恢复，避免重复提取。
    internal static class RestaurantIngredientTransfer
    {
        //核实取料队列并计算当前批次，职责是让出发和到达时都遵守真实预留与携带容量。
        public static bool Prepare(Pawn pawn, Job job, RestaurantOrder order, out string reason)
        {
            reason = "";
            if (order == null || order.IsTerminal) { reason = "取料订单已结束或不存在"; return false; }
            RestaurantOrderStock.Validate(order, pawn.Map);
            if (order.IsTerminal) { reason = order.failReason; return false; }
            if (job.targetQueueB.NullOrEmpty() || job.countQueue == null
                || job.targetQueueB.Count != job.countQueue.Count || job.countQueue[0] <= 0)
            { reason = "取料目标与数量队列不一致"; return false; }
            if (pawn.carryTracker.CarriedThing != null)
            {
                if (IsCarryingBatch(pawn, job, order)) return true;
                reason = $"厨师手中已有不属于当前批次的物品：{pawn.carryTracker.CarriedThing}";
                return false;
            }
            Thing source = job.targetQueueB[0].Thing;
            if (source == null || source.Destroyed || source.MapHeld != pawn.Map)
            { reason = "取料目标已毁坏或离开地图"; return false; }
            int reserved = RestaurantOrderStock.Reserved(order, pawn.Map).Where(t => t.Thing == source).Sum(t => t.Count);
            if (reserved < job.countQueue[0] || source.stackCount < reserved)
            { reason = $"{source} 数量不符：队列剩余={job.countQueue[0]}，本单预留={reserved}，实物={source.stackCount}"; return false; }
            job.count = Math.Min(job.countQueue[0], Math.Min(pawn.carryTracker.MaxStackSpaceEver(source.def),
                pawn.carryTracker.innerContainer.GetCountCanAccept(source, false)));
            if (job.count <= 0) { reason = $"厨师无法携带一件 {source.def.label}"; return false; }
            if (!TryGetPickup(source, out LocalTargetInfo target, out _))
            { reason = $"{source} 已不在可取料的冰箱或地面"; return false; }
            job.SetTarget(TargetIndex.C, target);
            return true;
        }

        //识别已经提取到手中的批次，职责是读档恢复时继续送往灶台而不再次提取或退单。
        public static bool IsCarryingBatch(Pawn pawn, Job job, RestaurantOrder order)
        {
            Thing carried = pawn.carryTracker.CarriedThing;
            return order != null && carried != null && carried == job.GetTarget(TargetIndex.B).Thing
                && job.countQueue?.Count > 0 && carried.stackCount == job.count && job.count <= job.countQueue[0]
                && RestaurantOrderStock.Reserved(order, pawn.Map).Any(t => t.Thing == carried && t.Count == carried.stackCount);
        }

        //解析食材实际持有者的交互策略，职责是保证寻路与到达校验使用相同目标和结束模式。
        public static bool TryGetPickup(Thing source, out LocalTargetInfo target, out PathEndMode mode)
        {
            if (source?.ParentHolder is Building_RestaurantStorage cabinet)
            {
                target = cabinet.InventoryInteractionTarget;
                mode = cabinet.InventoryInteractionEndMode;
                return cabinet.Spawned;
            }
            target = source;
            mode = PathEndMode.Touch;
            return source?.Spawned == true;
        }

        //完成一批真实食材提取，职责是到达时重新核实预留、数量和接收对象并保留具体失败原因。
        public static bool Extract(Pawn pawn, Job job, RestaurantOrder order, out string reason)
        {
            if (!Prepare(pawn, job, order, out reason)) return false;
            if (IsCarryingBatch(pawn, job, order)) return true;
            Thing source = job.targetQueueB[0].Thing;
            if (!TryGetPickup(source, out LocalTargetInfo target, out PathEndMode mode)
                || !pawn.CanReachImmediate(target, mode))
            { reason = $"尚未到达 {source} 的取料位置：厨师={pawn.Position}，目标={target}"; return false; }
            Thing taken = pawn.Map.GetComponent<MapComponent_InventoryReservations>().Extract(
                RestaurantStockUtility.Key(order), source, job.count, pawn.carryTracker.innerContainer, out reason);
            if (taken == null) return false;
            job.SetTarget(TargetIndex.B, taken);
            return true;
        }
    }
}
