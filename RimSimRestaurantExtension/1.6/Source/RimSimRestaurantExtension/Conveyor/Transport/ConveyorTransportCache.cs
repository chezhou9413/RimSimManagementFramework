using System;
using System.Collections.Generic;
using RimSimRestaurantExtension.Conveyor.Placement;
using UnityEngine;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //保存整条线路的运行缓冲，职责是只在拓扑变化时分配节点与阻塞队列。
    internal sealed class ConveyorTransportCache
    {
        internal ConveyorTransportNode[] nodes = Array.Empty<ConveyorTransportNode>();
        internal ConveyorTransportNode[] blocked = Array.Empty<ConveyorTransportNode>();
        internal int interval = 60;

        //重建固定拓扑和工作缓冲，职责是把地图邻格查询从逐步运输中移出。
        internal void Rebuild(List<Building_SushiConveyor> segments)
        {
            nodes = new ConveyorTransportNode[segments.Count];
            blocked = new ConveyorTransportNode[segments.Count];
            interval = segments.Count == 0 ? 60 : Mathf.Max(1, segments[0].def.GetModExtension<ConveyorSettings>().ticksPerCell);
            for (int i = 0; i < segments.Count; i++)
            {
                var belt = segments[i];
                nodes[i] = new ConveyorTransportNode(belt);
                belt.transportNode = nodes[i];
                if (belt.plate != null)
                { belt.plate.previousDrawNode = null; belt.plate.previousDrawTick = -1; }
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                node.next = ConveyorLinks.Next(node.belt)?.transportNode;
                if (node.next != null) node.next.previous = node;
            }
            for (int i = 0; i < nodes.Length; i++)
                nodes[i].incoming = nodes[i].previous?.outgoing ?? nodes[i].outgoing;
        }

        //读取已缓存组件的当前供电状态，职责是不降低断电判定频率且避免反复扫描组件列表。
        internal bool Powered()
        {
            if (nodes.Length == 0) return false;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].power == null || !nodes[i].power.PowerOn) return false;
            return true;
        }
    }
}
