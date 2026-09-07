using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //创建可直接营业的完整餐厅测试场景，职责是统一生成房间、设施、菜单、库存和岗位员工。
    public static class CompleteRestaurantCreationUtility
    {
        private const int RestaurantRadius = 7;

        //在指定地图中心点创建完整餐厅，职责是提供扩展内部和其他开发工具可复用的单一入口。
        public static bool TryCreateCompleteRestaurant(Map map, IntVec3 center,
            out Zone_Shop shop, out string failReason)
        {
            shop = null;
            failReason = "";
            if (map == null || !center.InBounds(map))
            {
                failReason = "地图或中心位置无效";
                return false;
            }

            CellRect outer = CellRect.CenteredOn(center, RestaurantRadius).ClipInsideMap(map);
            if (outer.Width < RestaurantRadius * 2 + 1 || outer.Height < RestaurantRadius * 2 + 1)
            {
                failReason = "位置太靠近地图边缘";
                return false;
            }
            CellRect inner = outer.ContractedBy(1);
            if (inner.Cells.Any(cell => map.zoneManager.ZoneAt(cell) != null))
            {
                failReason = "目标区域存在其他区划";
                return false;
            }

            ThingDef registerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sim_CashRegister");
            ThingDef storageDef = DefDatabase<ThingDef>.GetNamedSilentFail("RSR_Refrigerator");
            ThingDef stoveDef = DefDatabase<ThingDef>.GetNamedSilentFail("FueledStove");
            ThingDef tableDef = DefDatabase<ThingDef>.GetNamedSilentFail("RSR_DiningTableRectangular");
            ThingDef chairDef = DefDatabase<ThingDef>.GetNamedSilentFail("RSR_DiningChair");
            TerrainDef floorDef = DefDatabase<TerrainDef>.GetNamedSilentFail("FineTile") ?? TerrainDefOf.PavedTile;
            if (registerDef == null || storageDef == null || stoveDef == null || tableDef == null
                || chairDef == null || DefOfRefs.RSR_RestaurantOrderCounter == null
                || ThingDefOf.Wall == null || ThingDefOf.Door == null)
            {
                failReason = "缺少餐厅结构或设施 Def";
                return false;
            }

            if (!CompleteRestaurantInventoryUtility.TryBuildStockPlan(out List<RestaurantMenuItem> menuItems,
                    out Dictionary<ThingDef, int> stockPlan, out failReason)
                || !CompleteRestaurantInventoryUtility.CanCookAllMenuItems(stoveDef, menuItems, out failReason))
                return false;
            if (!CompleteRestaurantStaffUtility.TryResolveDefinitions(out ShopStaffRoleDef cashierRole,
                    out ShopStaffRoleDef chefRole, out ShopStaffRoleDef waiterRole,
                    out JobDef cashierJobDef, out failReason))
                return false;

            GameComponent_RestaurantSettings settingsManager = Current.Game?.GetComponent<GameComponent_RestaurantSettings>();
            if (settingsManager == null)
            {
                failReason = "餐厅设置组件尚未初始化";
                return false;
            }

            ClearArea(map, outer);
            if (!BuildRoomShell(map, outer, ThingDefOf.Wall, ThingDefOf.Door))
            {
                failReason = "无法生成餐厅墙体或入口";
                return false;
            }
            SetFloorAndRoof(map, inner, floorDef);
            shop = CreateShopZone(map, inner);

            Building_CashRegister register = SpawnBuilding(map, registerDef,
                center + IntVec3.South * 5 + IntVec3.West * 3, Rot4.South) as Building_CashRegister;
            Thing counter = SpawnBuilding(map, DefOfRefs.RSR_RestaurantOrderCounter,
                center + IntVec3.South * 5 + IntVec3.East * 3, Rot4.South);
            Building_WorkTable stove = SpawnBuilding(map, stoveDef,
                center + IntVec3.North * 5 + IntVec3.West * 3, Rot4.North) as Building_WorkTable;
            Building_SimContainer storage = SpawnBuilding(map, storageDef,
                center + IntVec3.North * 5 + IntVec3.East * 4, Rot4.South) as Building_SimContainer;
            if (register == null || counter == null || stove == null || storage == null)
            {
                failReason = "无法生成收银台、接待与出餐台、燃料灶或食品货柜";
                return false;
            }

            CompleteRestaurantFurnitureUtility.Spawn(map, center);
            CompRefuelable fuel = stove.TryGetComp<CompRefuelable>();
            if (fuel == null)
            {
                failReason = "燃料灶缺少燃料组件";
                return false;
            }
            fuel.Refuel(fuel.Props.fuelCapacity);

            int seatCount = SpawnDiningLayout(map, inner, tableDef, chairDef, center);
            if (seatCount < 4)
            {
                failReason = "无法生成足够的餐桌座位";
                return false;
            }

            HashSet<IntVec3> reservedCells = new HashSet<IntVec3>
            {
                register.InteractionCell,
                stove.InteractionCell
            };
            List<IntVec3> kitchenCells = CompleteRestaurantKitchenCells.Find(map, inner, center, reservedCells);
            if (!CompleteRestaurantInventoryUtility.TrySpawnStock(map, kitchenCells, stockPlan,
                    out int ingredientCount, out failReason))
                return false;

            RestaurantShopSettings settings = settingsManager.GetOrCreate(shop.ID);
            settings.menuItems = menuItems;
            settings.enabled = true;
            settings.maxServiceWaitTicks = 12000;
            settings.maxWaitTicks = 12000;
            settings.Normalize();
            if (!CompleteRestaurantPantryUtility.TryConfigure(map, shop, kitchenCells, settings, stockPlan, out failReason))
                return false;

            if (!CompleteRestaurantStaffUtility.TrySpawnAndAssign(map, inner, shop, register, stove,
                    cashierRole, chefRole, waiterRole, cashierJobDef, out failReason))
                return false;

            SimManagementLib.Pojo.ShopScheduleData schedule = shop.GetSchedule().Clone();
            schedule.manualOpen = true;
            schedule.useSchedule = false;
            shop.ApplySchedule(schedule);
            if (!shop.IsValidShop())
            {
                failReason = "餐厅生成后未通过商店校验：" + shop.GetValidationMessage();
                return false;
            }

            failReason = $"座位 {seatCount} 个，食材 {ingredientCount} 份，四类员工已分配";
            return true;
        }

        //清空餐厅占地区域，职责是保留 Pawn 并移除会阻挡测试建筑的地图对象。
        private static void ClearArea(Map map, CellRect rect)
        {
            foreach (IntVec3 cell in rect.Cells)
            {
                List<Thing> things = map.thingGrid.ThingsListAt(cell).ToList();
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || thing is Pawn) continue;
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        //生成封闭墙体和南侧入口，职责是让全部商店区格子满足室内营业条件。
        private static bool BuildRoomShell(Map map, CellRect outer, ThingDef wallDef, ThingDef doorDef)
        {
            IntVec3 doorCell = outer.GetCenterCellOnEdge(Rot4.South);
            foreach (IntVec3 cell in outer.EdgeCells)
            {
                ThingDef def = cell == doorCell ? doorDef : wallDef;
                Rot4 rotation = def == doorDef ? Rot4.East : Rot4.North;
                if (SpawnBuilding(map, def, cell, rotation) == null) return false;
            }
            return true;
        }

        //铺设地板和人工屋顶，职责是构成可营业且不会受天气影响的室内空间。
        private static void SetFloorAndRoof(Map map, CellRect inner, TerrainDef floorDef)
        {
            foreach (IntVec3 cell in inner.Cells)
            {
                map.terrainGrid.SetTerrain(cell, floorDef);
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
        }

        //注册并填充商店区，职责是让框架后续按区域扫描全部餐厅设施与库存。
        private static Zone_Shop CreateShopZone(Map map, CellRect inner)
        {
            Zone_Shop zone = new Zone_Shop(map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 cell in inner.Cells)
                zone.AddCell(cell);
            return zone;
        }

        //生成两张餐桌及其相邻座椅，职责是提供顾客和服务员都可到达的真实用餐点。
        private static int SpawnDiningLayout(Map map, CellRect inner, ThingDef tableDef, ThingDef chairDef, IntVec3 center)
        {
            int seatCount = 0;
            IntVec3[] tableCells = { center + IntVec3.West * 3, center + IntVec3.East * 3 };
            for (int i = 0; i < tableCells.Length; i++)
            {
                Building table = SpawnBuilding(map, tableDef, tableCells[i], Rot4.North) as Building;
                if (table == null) continue;
                seatCount += SpawnChairsAroundTable(map, inner, table, chairDef);
            }
            return seatCount;
        }

        //沿桌面南北两侧生成座椅，职责是保证每个座位正交邻接可进食桌面。
        private static int SpawnChairsAroundTable(Map map, CellRect inner, Building table, ThingDef chairDef)
        {
            CellRect rect = table.OccupiedRect();
            IntVec3[] cells =
            {
                new IntVec3(rect.minX, 0, rect.minZ - 1),
                new IntVec3(rect.maxX, 0, rect.minZ - 1),
                new IntVec3(rect.minX, 0, rect.maxZ + 1),
                new IntVec3(rect.maxX, 0, rect.maxZ + 1)
            };
            int count = 0;
            cells = cells.Distinct().ToArray();
            for (int i = 0; i < cells.Length; i++)
            {
                if (!inner.Contains(cells[i])) continue;
                Rot4 rotation = cells[i].z < rect.minZ ? Rot4.North : Rot4.South;
                if (SpawnBuilding(map, chairDef, cells[i], rotation) != null) count++;
            }
            return count;
        }

        //按默认材质生成建筑或家具，职责是统一设置玩家派系并使用确定的覆盖模式落图。
        internal static Thing SpawnBuilding(Map map, ThingDef def, IntVec3 cell, Rot4 rotation)
        {
            if (map == null || def == null || !cell.InBounds(map)) return null;
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            if (thing.def.CanHaveFaction)
                thing.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, cell, map, rotation, WipeMode.Vanish);
        }
    }
}
