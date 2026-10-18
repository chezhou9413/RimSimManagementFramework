using System.Collections.Generic;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Placement
{
    //生成有序正交路径，职责是让拖拽保持按下点到释放点的运输顺序。
    public sealed class DrawStyle_SushiPath : DrawStyle
    {
        public static bool horizontalFirst = true;
        public override bool CanHaveDuplicates => false;

        //按锁定轴生成直线或L形路径，职责是避免重复格与对角线跳跃。
        public override void Update(IntVec3 origin, IntVec3 target, List<IntVec3> buffer)
        {
            buffer.Clear();
            buffer.Add(origin);
            var cell = origin;
            for (int axis = 0; axis < 2; axis++)
            {
                bool horizontal = (axis == 0) == horizontalFirst;
                while (horizontal ? cell.x != target.x : cell.z != target.z)
                {
                    if (horizontal) cell.x += System.Math.Sign(target.x - cell.x);
                    else cell.z += System.Math.Sign(target.z - cell.z);
                    buffer.Add(cell);
                }
            }
        }
    }
}

