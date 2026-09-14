using RimWorld;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //缓存单格运输数据，职责是把固定连接、组件和几何参数与逐步食品快照分开保存。
    internal sealed class ConveyorTransportNode
    {
        internal readonly Building_SushiConveyor belt;
        internal readonly CompPowerTrader power;
        internal readonly Vector3 center, outgoing;
        internal Vector3 incoming;
        internal ConveyorTransportNode next, previous;
        internal Thing food;
        internal ConveyorPlate plate;
        internal float planned;
        internal bool held, moving;

        //绑定不会在运输中变化的建筑数据，职责是避免逐步查询电力组件和格坐标。
        internal ConveyorTransportNode(Building_SushiConveyor belt)
        {
            this.belt = belt;
            power = belt.GetComp<CompPowerTrader>();
            center = belt.Position.ToVector3Shifted();
            outgoing = belt.Rotation.FacingCell.ToVector3();
        }

        //根据缓存几何计算轨迹，职责是只在绘制时求解直线或四分之一圆弧位置。
        internal Vector3 Offset(float progress)
        {
            float t = next == null ? Mathf.Min(progress, 0.5f) : progress;
            if (Vector3.Dot(incoming, outgoing) > 0.5f) return outgoing * (t - 0.5f);
            Vector3 turnCenter = (outgoing - incoming) * 0.5f;
            float angle = t * Mathf.PI * 0.5f;
            return turnCenter - outgoing * (Mathf.Cos(angle) * 0.5f) + incoming * (Mathf.Sin(angle) * 0.5f);
        }
    }
}
