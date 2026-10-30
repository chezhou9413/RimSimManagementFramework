using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.Tool;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimHotelExtension.Debug
{
    //固定样板布局集中在一处，房间、池位和员工通道都留出真实寻路空间。
    public static partial class HotelDistrictCreationUtility
    {
        //将样板中的格子偏移换算为地图格子。
        private static IntVec3 At(IntVec3 origin, int x, int z) => origin + new IntVec3(x, 0, z);

        //按左下角和尺寸取得房间范围。
        private static CellRect Rect(IntVec3 origin, int x, int z, int width, int height)
            => new CellRect(origin.x + x, origin.z + z, width, height);

        //只清理预检允许的植物和污物，并铺设连接所有入口的商圈地面。
        private static void PrepareGround(Map map, CellRect district)
        {
            foreach (IntVec3 cell in district)
            {
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell).ToList())
                    if (thing is Plant || thing is Filth) thing.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.PavedTile);
                map.areaManager.Home[cell] = true;
            }
        }

        //先完成外墙、客房和温泉隔墙，所有人工屋顶都有六格以内的支撑。
        private static void BuildRooms(Map map, IntVec3 origin)
        {
            BuildShell(map, origin, 0, 8, 25, 21, 12, 8);
            BuildShell(map, origin, 0, 0, 12, 7, 5, 6);
            BuildShell(map, origin, 13, 0, 12, 7, 18, 6);
            BuildShell(map, origin, 27, 0, 9, 9, 27, 3);
            BuildShell(map, origin, 27, 11, 9, 7, 27, 14);
            for (int x = 1; x < 24; x++)
            {
                Spawn(map, origin, x == 6 || x == 18 ? "Door" : "Wall", x, 15);
                Spawn(map, origin, x == 3 || x == 9 || x == 15 || x == 21 ? "Door" : "Wall", x, 23);
            }
            for (int z = 16; z <= 22; z++) Spawn(map, origin, "Wall", 12, z);
            for (int x = 6; x <= 18; x += 6)
                for (int z = 24; z <= 27; z++) Spawn(map, origin, "Wall", x, z);
        }

        //建立矩形外墙和单一入口，屋顶只覆盖房间内部。
        private static void BuildShell(Map map, IntVec3 origin, int x, int z, int width, int height, int doorX, int doorZ)
        {
            CellRect room = Rect(origin, x, z, width, height);
            IntVec3 door = At(origin, doorX, doorZ);
            foreach (IntVec3 cell in room.EdgeCells)
                Spawn(map, IntVec3.Zero, cell == door ? "Door" : "Wall", cell.x, cell.z);
            foreach (IntVec3 cell in room.ContractedBy(1)) map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
        }

        //室内区域包含房间之间的门，实心隔墙不作为商店格。
        private static Zone_Shop CreateShop(Map map, CellRect inner, string name)
        {
            var shop = new Zone_Shop(map.zoneManager) { label = name };
            map.zoneManager.RegisterZone(shop);
            foreach (IntVec3 cell in inner)
            {
                Building edifice = cell.GetEdifice(map);
                if (edifice == null || edifice is Building_Door) shop.AddCell(cell);
            }
            return shop;
        }

        //前厅和员工区提供实际餐桌，客房及泡汤区有基础照明和供暖。
        private static void BuildCommonFurniture(Map map, IntVec3 origin)
        {
            foreach (int x in new[] { 13, 16 })
            {
                Spawn(map, origin, "Table1x2c", x, 11);
                Spawn(map, origin, "DiningChair", x - 1, 11, Rot4.East);
                Spawn(map, origin, "DiningChair", x + 1, 11, Rot4.West);
            }
            foreach (int x in new[] { 2, 22 })
            {
                Spawn(map, origin, "StandingLamp", x, 13);
                Spawn(map, origin, "Heater", x, 9);
            }
            foreach (int x in new[] { 1, 13 })
            {
                Spawn(map, origin, "StandingLamp", x, 18);
                Spawn(map, origin, "Heater", x, 16);
            }
        }

        //独立发电并连通各房间，只初始化新设施，不打开全局无限电力。
        private static void BuildPower(Map map, IntVec3 origin, CellRect district)
        {
            for (int i = 0; i < 4; i++) Spawn(map, origin, "VanometricPowerCell", 28 + i * 2, 13);
            var conduits = new HashSet<IntVec3>();
            for (int x = 0; x <= 35; x++) conduits.Add(At(origin, x, 8));
            for (int z = 8; z <= 28; z++) conduits.Add(At(origin, 24, z));
            for (int z = 0; z <= 16; z++) conduits.Add(At(origin, 35, z));
            for (int x = 0; x <= 24; x++)
                foreach (int z in new[] { 0, 15, 23, 28 }) conduits.Add(At(origin, x, z));
            for (int z = 0; z <= 8; z++) conduits.Add(At(origin, 24, z));
            for (int x = 24; x <= 35; x++) conduits.Add(At(origin, x, 0));
            //发电机本身导电，连接其间的空格，避免在同一格登记两个电网节点。
            for (int x = 27; x <= 35; x += 2) conduits.Add(At(origin, x, 13));
            foreach (IntVec3 cell in conduits) Spawn(map, IntVec3.Zero, "PowerConduit", cell.x, cell.z);

            map.powerNetManager.UpdatePowerNetsAndConnections_First();
            List<CompPowerTrader> consumers = map.listerBuildings.allBuildingsColonist
                .Where(b => district.Contains(b.Position)).Select(b => b.GetComp<CompPowerTrader>()).Where(c => c != null).ToList();
            foreach (CompPowerPlant generator in consumers.OfType<CompPowerPlant>())
            {
                generator.PowerOn = true;
                generator.UpdateDesiredPowerOutput();
            }
            foreach (CompPowerTrader consumer in consumers.Where(c => !(c is CompPowerPlant)))
            {
                if (consumer.PowerNet == null || !consumer.PowerNet.CanPowerNow(consumer))
                    throw new InvalidOperationException(HotelText.T("RSH.Debug.PowerConnectionFailed") + consumer.parent.LabelCap);
                consumer.PowerOn = true;
            }
        }

        //固定布局使用默认合法材质，家具统一良好品质；调用前已核对全部用地。
        private static Building Spawn(Map map, IntVec3 origin, string defName, int x, int z, Rot4? rotation = null)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.SetFactionDirect(Faction.OfPlayer);
            thing.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Good, ArtGenerationContext.Colony);
            return (Building)GenSpawn.Spawn(thing, At(origin, x, z), map, rotation ?? Rot4.North, WipeMode.Vanish);
        }
    }
}
