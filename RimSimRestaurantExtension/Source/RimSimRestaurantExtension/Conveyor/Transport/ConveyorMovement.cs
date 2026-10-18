using System;
using RimSimRestaurantExtension.Conveyor.Stocking;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //推进实体餐盘，职责是复用拓扑缓冲并以两阶段提交保持满环和跨格物权一致。
    public static class ConveyorMovement
    {
        //更新实物并按供电决定是否运输，职责是让稳定拓扑下的每步更新不创建临时集合。
        public static void Tick(ConveyorLine line, bool advancing, int tick)
        {
            bool occupied = ReadContents(line, advancing, tick);
            if (!advancing || !occupied) return;
            PlanProgress(line.transport);
            PlanDepartures(line.transport);
            Commit(line.transport);
        }

        //读取当前容器并记录腐坏损耗，职责是供电暂停时仍处理已经消失的餐盘。
        private static bool ReadContents(ConveyorLine line, bool advancing, int tick)
        {
            var nodes = line.transport.nodes;
            float step = 1f / line.Interval;
            bool occupied = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                var belt = node.belt;
                node.food = belt.Food;
                node.plate = belt.plate;
                node.moving = false;
                node.held = false;
                if (node.food == null)
                {
                    if (node.plate != null)
                    {
                        if (!node.plate.wasteRecorded) { line.lostPlates++; line.wasteCost += node.plate.cost; }
                        belt.plate = null;
                        node.plate = null;
                    }
                    continue;
                }
                occupied = true;
                if (!advancing) continue;
                //只记录几何节点与进度，实际坐标和圆弧在可见餐盘绘制时计算。
                node.plate.previousDrawNode = node;
                node.plate.previousDrawProgress = belt.progress;
                node.plate.previousDrawTick = tick;
                node.held = ConveyorStockPlanner.HoldsPlate(belt);
                node.planned = node.held ? belt.progress : belt.progress + step;
            }
            return occupied;
        }

        //反向传播实物间距，职责是以固定两轮遍历覆盖普通路径和闭环首尾的阻塞。
        private static void PlanProgress(ConveyorTransportCache cache)
        {
            var nodes = cache.nodes;
            float freeLimit = 1f + 1f / cache.interval;
            for (int pass = 0; pass < 2; pass++)
                for (int i = nodes.Length - 1; i >= 0; i--)
                {
                    var node = nodes[i];
                    if (node.food == null) continue;
                    var next = node.next;
                    float limit = next == null ? 0.5f : next.food == null ? freeLimit : next.planned;
                    node.planned = Mathf.Max(node.belt.progress, Mathf.Min(node.planned, limit));
                }
        }

        //通过上游队列传播跨格阻塞，职责是让每个节点最多入队一次并保留满环整体推进。
        private static void PlanDepartures(ConveyorTransportCache cache)
        {
            var nodes = cache.nodes;
            int head = 0, tail = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node.food == null) continue;
                node.moving = node.planned >= 1f - 0.00001f && !node.held && node.next != null && !node.next.held;
                if (!node.moving) cache.blocked[tail++] = node;
            }
            while (head < tail)
            {
                var node = cache.blocked[head];
                cache.blocked[head++] = null;
                var previous = node.previous;
                if (previous == null || !previous.moving) continue;
                previous.moving = false;
                cache.blocked[tail++] = previous;
            }
        }

        //先移出全部离开格的实物再统一入格，职责是防止满环死锁、重复餐盘和跨格余量丢失。
        private static void Commit(ConveyorTransportCache cache)
        {
            var nodes = cache.nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node.food == null) continue;
                node.belt.progress = Mathf.Min(1f, node.planned);
                if (!node.moving) continue;
                node.belt.GetDirectlyHeldThings().Remove(node.food);
                node.belt.plate = null;
                node.belt.progress = 0f;
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (!node.moving) continue;
                var target = node.next.belt;
                if (!target.GetDirectlyHeldThings().TryAdd(node.food, false))
                    throw new InvalidOperationException("传送带两阶段运输目的容器拒绝餐盘");
                target.plate = node.plate;
                target.progress = Mathf.Max(0f, node.planned - 1f);
            }
        }

        //选择空格中的入盘位置，职责是用缓存上下游检查前后盘间距而不暂停运输。
        public static bool TryInsertionProgress(Building_SushiConveyor belt, out float progress)
        {
            belt.Manager.Rebuild();
            var node = belt.transportNode;
            progress = 0.5f;
            if (belt.Food != null) return false;
            var next = node.next?.belt;
            var previous = node.previous?.belt;
            float maximum = next == null ? 0.5f : next.Food == null ? 1f : next.progress;
            float minimum = previous?.Food == null ? 0f : previous.progress;
            if (minimum > maximum) return false;
            progress = Mathf.Clamp(progress, minimum, maximum);
            return true;
        }

        //读取成品的缓存轨迹，职责是让绘制不再扫描相邻地图格以推断弯道。
        public static Vector3 Offset(Building_SushiConveyor belt)
        {
            return belt.transportNode.Offset(belt.progress);
        }
    }
}
