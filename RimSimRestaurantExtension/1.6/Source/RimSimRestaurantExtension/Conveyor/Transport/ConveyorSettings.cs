using Verse;

namespace RimSimRestaurantExtension.Conveyor.Transport
{
    //定义传送带运输参数，职责是集中控制每格移动耗时。
    public sealed class ConveyorSettings : DefModExtension
    {
        public int ticksPerCell = 60;
    }
}

