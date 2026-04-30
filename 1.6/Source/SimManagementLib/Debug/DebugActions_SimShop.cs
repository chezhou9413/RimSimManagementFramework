using LudeonTK;
using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using Verse;

namespace SimManagementLib.Debug
{
    public static partial class DebugActions_SimShop
    {
        [DebugAction("SimShop", "强制刷新顾客（当前地图）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceSpawnCustomerWaveOnCurrentMap()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("强制刷新失败：当前没有地图。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CustomerArrivalManager manager = map.GetComponent<CustomerArrivalManager>();
            if (manager == null)
            {
                Messages.Message("强制刷新失败：顾客到访管理器未初始化。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool ok = manager.ForceSpawnOneWave(ignoreConditions: true, out string resultMessage);
            Messages.Message(resultMessage, ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("SimShop", "生成完整商店（点选位置）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnCompleteShopAtCell()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            IntVec3 clickCell = UI.MouseCell();
            if (!clickCell.InBounds(map)) return;

            ThingDef registerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sim_CashRegister");
            ThingDef storageDef = DefDatabase<ThingDef>.GetNamedSilentFail("MegaStorageBox")
                                  ?? DefDatabase<ThingDef>.AllDefsListForReading
                                      .FirstOrDefault(d => d?.thingClass != null && typeof(Building_SimContainer).IsAssignableFrom(d.thingClass));
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDefOf.Door;
            if (registerDef == null || storageDef == null || wallDef == null || doorDef == null)
            {
                Messages.Message("生成完整商店失败：缺少必要 Def。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect outer = CellRect.CenteredOn(clickCell, 4).ClipInsideMap(map);
            if (outer.Width < 7 || outer.Height < 7)
            {
                Messages.Message("生成完整商店失败：位置太靠近地图边缘。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect inner = outer.ContractedBy(1);
            if (inner.Width < 5 || inner.Height < 5)
            {
                Messages.Message("生成完整商店失败：内部空间不足。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            ClearAreaForShop(map, outer);
            BuildRoomShell(map, outer, wallDef, doorDef);
            SetConstructedRoof(map, inner);

            SimZone.Zone_Shop zone = CreateShopZone(map, inner);
            if (zone == null)
            {
                Messages.Message("生成完整商店失败：无法创建商店区。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            IntVec3 registerCell = inner.CenterCell;
            Building_CashRegister register = SpawnRegister(map, registerDef, registerCell);
            if (register == null)
            {
                Messages.Message("生成完整商店失败：无法放置收银台。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            System.Collections.Generic.List<Building_SimContainer> storages = SpawnStorages(map, storageDef, inner, registerCell);
            if (storages.NullOrEmpty())
            {
                Messages.Message("生成完整商店失败：无法放置货柜。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            FillStoragesWithAllGoods(storages, 60);
            SpawnCashier(map, register);
            Messages.Message("已生成完整商店：货柜会按 GoodsDef 轮换铺满。", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("SimShop", "生成豪华餐厅（点选位置）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnLuxuryRestaurantAtCell()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            IntVec3 clickCell = UI.MouseCell();
            if (!clickCell.InBounds(map)) return;

            ThingDef registerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sim_CashRegister");
            ThingDef storageDef = DefDatabase<ThingDef>.GetNamedSilentFail("MegaStorageBox")
                                  ?? DefDatabase<ThingDef>.AllDefsListForReading
                                      .FirstOrDefault(d => d?.thingClass != null && typeof(Building_SimContainer).IsAssignableFrom(d.thingClass));
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDefOf.Door;
            ThingDef tableDef = DefDatabase<ThingDef>.GetNamedSilentFail("Table2x2c")
                                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Table1x2c");
            ThingDef chairDef = DefDatabase<ThingDef>.GetNamedSilentFail("DiningChair")
                                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Stool");
            ThingDef lampDef = DefDatabase<ThingDef>.GetNamedSilentFail("StandingLamp");
            ThingDef plantDef = DefDatabase<ThingDef>.GetNamedSilentFail("PlantPot");
            TerrainDef floorDef = DefDatabase<TerrainDef>.GetNamedSilentFail("FineTile")
                                 ?? DefDatabase<TerrainDef>.GetNamedSilentFail("TileMarble")
                                 ?? TerrainDefOf.PavedTile;

            if (registerDef == null || storageDef == null || wallDef == null || doorDef == null)
            {
                Messages.Message("生成豪华餐厅失败：缺少必要 Def。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect outer = CellRect.CenteredOn(clickCell, 6).ClipInsideMap(map);
            if (outer.Width < 11 || outer.Height < 11)
            {
                Messages.Message("生成豪华餐厅失败：位置太靠近地图边缘。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect inner = outer.ContractedBy(1);
            if (inner.Width < 9 || inner.Height < 9)
            {
                Messages.Message("生成豪华餐厅失败：内部空间不足。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            ClearAreaForShop(map, outer);
            BuildRoomShell(map, outer, wallDef, doorDef);
            SetConstructedRoof(map, inner);
            SetFloorTerrain(map, inner, floorDef);

            SimZone.Zone_Shop zone = CreateShopZone(map, inner);
            if (zone == null)
            {
                Messages.Message("生成豪华餐厅失败：无法创建商店区。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            IntVec3 registerCell = inner.CenterCell + IntVec3.South * UnityEngine.Mathf.Max(1, inner.Height / 2 - 2);
            if (!inner.Contains(registerCell))
                registerCell = inner.CenterCell;

            Building_CashRegister register = SpawnRegister(map, registerDef, registerCell);
            if (register == null)
            {
                Messages.Message("生成豪华餐厅失败：无法放置收银台。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            System.Collections.Generic.List<Building_SimContainer> storages = SpawnLuxuryStorages(map, storageDef, inner);
            if (storages.NullOrEmpty())
            {
                Messages.Message("生成豪华餐厅失败：无法放置货柜。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            foreach (Building_SimContainer storage in storages)
                ConfigureAndFillStorage(storage, goodsLimit: 10, targetCount: 80);

            SpawnLuxuryDiningLayout(map, inner, tableDef, chairDef, lampDef, plantDef);
            SpawnCashier(map, register);
            Messages.Message("已生成豪华餐厅：含装修、货柜、收银台和店员。", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("SimShop", "生成豪华大烟店（点选位置）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnLuxuryDrugShopAtCell()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            IntVec3 clickCell = UI.MouseCell();
            if (!clickCell.InBounds(map)) return;

            ThingDef registerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sim_CashRegister");
            ThingDef storageDef = DefDatabase<ThingDef>.GetNamedSilentFail("MegaStorageBox")
                                  ?? DefDatabase<ThingDef>.AllDefsListForReading
                                      .FirstOrDefault(d => d?.thingClass != null && typeof(Building_SimContainer).IsAssignableFrom(d.thingClass));
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDefOf.Door;
            ThingDef tableDef = DefDatabase<ThingDef>.GetNamedSilentFail("Table2x2c")
                                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Table1x2c");
            ThingDef chairDef = DefDatabase<ThingDef>.GetNamedSilentFail("DiningChair")
                                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Stool");
            ThingDef lampDef = DefDatabase<ThingDef>.GetNamedSilentFail("StandingLamp");
            TerrainDef floorDef = DefDatabase<TerrainDef>.GetNamedSilentFail("FineTile")
                                 ?? DefDatabase<TerrainDef>.GetNamedSilentFail("TileMarble")
                                 ?? TerrainDefOf.PavedTile;

            GoodsDef addictiveGoods = DefDatabase<GoodsDef>.GetNamedSilentFail("Goods_AddictiveDrugs");
            GoodsDef mixedDrugGoods = DefDatabase<GoodsDef>.GetNamedSilentFail("Goods_Drugs");

            if (registerDef == null || storageDef == null || wallDef == null || doorDef == null || addictiveGoods == null)
            {
                Messages.Message("生成豪华大烟店失败：缺少必要 Def 或 Goods_AddictiveDrugs。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect outer = CellRect.CenteredOn(clickCell, 6).ClipInsideMap(map);
            if (outer.Width < 11 || outer.Height < 11)
            {
                Messages.Message("生成豪华大烟店失败：位置太靠近地图边缘。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect inner = outer.ContractedBy(1);
            ClearAreaForShop(map, outer);
            BuildRoomShell(map, outer, wallDef, doorDef);
            SetConstructedRoof(map, inner);
            SetFloorTerrain(map, inner, floorDef);

            SimZone.Zone_Shop zone = CreateShopZone(map, inner);
            if (zone == null)
            {
                Messages.Message("生成豪华大烟店失败：无法创建商店区。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            IntVec3 registerCell = inner.CenterCell + IntVec3.South * UnityEngine.Mathf.Max(1, inner.Height / 2 - 2);
            if (!inner.Contains(registerCell))
                registerCell = inner.CenterCell;

            Building_CashRegister register = SpawnRegister(map, registerDef, registerCell);
            if (register == null)
            {
                Messages.Message("生成豪华大烟店失败：无法放置收银台。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            System.Collections.Generic.List<Building_SimContainer> storages = SpawnLuxuryStorages(map, storageDef, inner);
            if (storages.NullOrEmpty())
            {
                Messages.Message("生成豪华大烟店失败：无法放置货柜。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            FillStoragesWithGoodsSet(
                storages,
                mixedDrugGoods != null ? new[] { addictiveGoods, mixedDrugGoods } : new[] { addictiveGoods },
                120);

            SpawnLuxuryDiningLayout(map, inner, tableDef, chairDef, lampDef, null);
            SpawnCashier(map, register);
            SpawnStaffPawn(map, register.Position + IntVec3.West * 2);
            SpawnStaffPawn(map, register.Position + IntVec3.East * 2);
            Messages.Message("已生成豪华大烟店：货柜装满成瘾品，收银员和两名店员已就位。", MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
