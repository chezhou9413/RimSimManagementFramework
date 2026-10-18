using System;
using System.Collections.Generic;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimWorld;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //布置传送带样板店，职责是提供单向闭环、朝向桌面的座椅与实际供电连接。
    internal static class ConveyorRestaurantLayout
    {
        //检查专用建筑与现货定义，职责是在清理目标场地前报告缺失内容。
        internal static bool TryValidate(out string reason)
        {
            reason = "";
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("RSR_SushiConveyor");
            if (def?.thingClass != typeof(Building_SushiConveyor) || ThingDefOf.MealSurvivalPack == null)
            {
                reason = "缺少寿司传送带建筑或生存包装食品定义";
                return false;
            }
            return true;
        }

        //按运输顺序列出矩形周长，职责是统一铺设方向与配置入口的位置。
        internal static List<IntVec3> Path(IntVec3 center)
        {
            var cells = new List<IntVec3>();
            for (int x = -3; x <= 3; x++) cells.Add(center + new IntVec3(x, 0, -2));
            for (int z = -1; z <= 2; z++) cells.Add(center + new IntVec3(3, 0, z));
            for (int x = 2; x >= -3; x--) cells.Add(center + new IntVec3(x, 0, 2));
            for (int z = 1; z >= -1; z--) cells.Add(center + new IntVec3(-3, 0, z));
            return cells;
        }

        //生成二十段闭环和十个外侧座位，职责是保留前厅通道与后厨工作位置。
        internal static int Spawn(Map map, IntVec3 center, ThingDef chairDef)
        {
            var cells = Path(center);
            var beltDef = DefDatabase<ThingDef>.GetNamed("RSR_SushiConveyor");
            for (int i = 0; i < cells.Count; i++)
            {
                Rot4 direction = Rot4.FromIntVec3(cells[(i + 1) % cells.Count] - cells[i]);
                if (!(CompleteRestaurantCreationUtility.SpawnBuilding(map, beltDef, cells[i], direction)
                    is Building_SushiConveyor)) throw new InvalidOperationException("无法生成样板传送带：" + cells[i]);
            }

            int seats = 0;
            for (int x = -2; x <= 2; x += 2)
            {
                seats += SpawnChair(map, chairDef, center + new IntVec3(x, 0, -3), Rot4.North);
                seats += SpawnChair(map, chairDef, center + new IntVec3(x, 0, 3), Rot4.South);
            }
            for (int z = -1; z <= 1; z += 2)
            {
                seats += SpawnChair(map, chairDef, center + new IntVec3(-4, 0, z), Rot4.East);
                seats += SpawnChair(map, chairDef, center + new IntVec3(4, 0, z), Rot4.West);
            }

            //北侧导线接入既有发电机干线，传送带各段通过相邻电网自然供电。
            var conduit = DefDatabase<ThingDef>.GetNamed("PowerConduit");
            for (int z = 2; z < 5; z++)
                CompleteRestaurantCreationUtility.SpawnBuilding(map, conduit, center + new IntVec3(0, 0, z), Rot4.North);
            return seats;
        }

        //摆放朝向相邻传送带的单椅，职责是按实际成功生成数量报告可用座位。
        private static int SpawnChair(Map map, ThingDef chairDef, IntVec3 cell, Rot4 rotation)
            => CompleteRestaurantCreationUtility.SpawnBuilding(map, chairDef, cell, rotation) != null ? 1 : 0;
    }
}
