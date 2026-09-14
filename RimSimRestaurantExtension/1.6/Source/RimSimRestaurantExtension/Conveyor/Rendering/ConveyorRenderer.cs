using System.Collections.Generic;
using RimSimRestaurantExtension.Conveyor.Placement;
using RimSimRestaurantExtension.Conveyor.Transport;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //绘制线路和食品，职责是从缓存帧中选择当前动画并维持地图坐标连续。
    public static class ConveyorRenderer
    {
        //绘制底座动画与真实食品，职责是暂停时保留最后一帧。
        public static void Draw(Building_SushiConveyor belt, Vector3 location)
        {
            var line = belt.Line;
            float fraction = line?.renderClock.Fraction() ?? 1f;
            float clock = (line?.clock ?? 0) - 1f + fraction;
            int frame = Mathf.FloorToInt(Mathf.Repeat(clock / Mathf.Max(1, line?.Interval ?? 60) * 3f, 1f)
                * ConveyorTextureCache.PhaseCount);
            int mask = ConveyorLinks.Mask(belt.Map, belt.Position);
            var mat = ConveyorTextureCache.Material(mask, belt.Rotation, frame, belt.DrawColor, belt.DrawColorTwo);
            Graphics.DrawMesh(MeshPool.plane10, location, Quaternion.identity, mat, 0);
            if (belt.Food != null)
            {
                var pos = belt.Position.ToVector3Shifted() + ConveyorMovement.Offset(belt);
                var plate = belt.plate;
                if (plate?.previousDrawTick == Find.TickManager.TicksGame && plate.previousDrawNode != null)
                    pos = Vector3.Lerp(plate.previousDrawNode.center + plate.previousDrawNode.Offset(plate.previousDrawProgress), pos, fraction);
                pos.y = location.y + 0.05f;
                Graphics.DrawMesh(MeshPool.plane03, pos, Quaternion.identity, belt.Food.Graphic.MatSingleFor(belt.Food), 0);
            }
        }

        //绘制提交前的静态方向图块，职责是不修改线路与实际建筑。
        public static void Preview(Map map, IntVec3 cell, IDictionary<IntVec3, Rot4> plan, Color color)
        {
            if (!cell.InBounds(map)) return;
            var position = cell.ToVector3Shifted();
            position.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            int mask = ConveyorLinks.Mask(map, cell, plan);
            var material = ConveyorTextureCache.Material(mask, plan[cell], 0, color, Color.white);
            Graphics.DrawMesh(MeshPool.plane10, position, Quaternion.identity, material, 0);
            GenDraw.DrawArrowRotated(position + new Vector3(0f, 0.01f, 0f), plan[cell].AsAngle, true);
        }
    }
}
