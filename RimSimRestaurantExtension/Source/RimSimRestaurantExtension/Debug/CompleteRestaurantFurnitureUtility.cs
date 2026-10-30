using RimWorld;
using SimManagementLib.Tool;
using Verse;
namespace RimSimRestaurantExtension.Debug
{
    //布置样板店的补充家具与电力，职责是提供全部餐厅家具及酒水配送的调试入口。
    internal static class CompleteRestaurantFurnitureUtility
    {
        //在生成商圈前核对模板使用的家具、供电和货源定义。
        public static bool TryValidate(out string reason)
        {
            foreach (string name in new[] { "RSR_BarCounter", "RSR_BarStool", "RSR_BoothSofa",
                "RSR_DiningTableSquare", "RSR_WallWineCabinet", "RSR_WallCabinet",
                "RSR_KitchenStorageCabinet", "WoodFiredGenerator", "PowerConduit", "ShelfSmall", "Beer" })
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) != null) continue;
                reason = SimTranslation.T("RSR.Debug.MissingTemplateDef", name.Named("DEF"));
                return false;
            }
            reason = "";
            return true;
        }

        //摆放吧台、双人卡座和三种商品柜，职责是覆盖常规桌面与墙体依附布局。
        public static void Spawn(Map map, IntVec3 center)
        {
            SpawnNamed(map, "RSR_BarCounter", center + new IntVec3(-1, 0, -3), Rot4.North);
            SpawnNamed(map, "RSR_BarCounter", center + new IntVec3(0, 0, -3), Rot4.North);
            SpawnNamed(map, "RSR_BarStool", center + new IntVec3(0, 0, -4), Rot4.North);
            var booth = (Building)SpawnNamed(map, "RSR_BoothSofa", center + new IntVec3(3, 0, -3), Rot4.South);
            foreach (var cell in booth.OccupiedRect())
                SpawnNamed(map, "RSR_DiningTableSquare", cell + IntVec3.South, Rot4.North);
            SpawnKitchenAndPower(map, center);
        }

        //布置货柜和已加满燃料的发电机，职责是让不同用餐布局共用库存与供电设施。
        internal static void SpawnKitchenAndPower(Map map, IntVec3 center)
        {
            SpawnNamed(map, "RSR_WallWineCabinet", center + new IntVec3(-6, 0, 4), Rot4.West);
            SpawnNamed(map, "RSR_WallCabinet", center + new IntVec3(-6, 0, 2), Rot4.West);
            SpawnNamed(map, "RSR_KitchenStorageCabinet", center + new IntVec3(6, 0, 4), Rot4.South);
            var generator = SpawnNamed(map, "WoodFiredGenerator", center + new IntVec3(0, 0, 5), Rot4.North);
            var fuel = generator.TryGetComp<CompRefuelable>();
            fuel.Refuel(fuel.Props.fuelCapacity);
            CellRect generatorCells = generator.OccupiedRect();
            for (int x = 0; x <= 4; x++)
            {
                IntVec3 cell = center + new IntVec3(x, 0, 5);
                //发电机占地本身导电，同格叠放电线会重复登记电网节点。
                if (!generatorCells.Contains(cell)) SpawnNamed(map, "PowerConduit", cell, Rot4.North);
            }
        }

        //生成具有适当材质的样板家具，职责是复用样板店的受控建筑放置。
        private static Thing SpawnNamed(Map map, string name, IntVec3 cell, Rot4 rotation) =>
            CompleteRestaurantCreationUtility.SpawnBuilding(map, DefDatabase<ThingDef>.GetNamed(name), cell, rotation);
    }
}
