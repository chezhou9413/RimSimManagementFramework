using LudeonTK;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.Debug
{
    //补货压力测试调试入口，职责是生成大量缺货货柜、货源和补货员工。
    public static partial class DebugActions_SimShop
    {
        private const int RestockStressStorageCount = 40;
        private const int RestockStressStaffCount = 24;
        private const int RestockStressGoodsPerStorage = 8;
        private const int RestockStressTargetPerItem = 30;
        private const int RestockStressSupplyStacksPerItem = 12;

        [DebugAction("SimShop", "生成补货压力测试商店（点选位置）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnRestockStressShopAtCell()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            IntVec3 clickCell = UI.MouseCell();
            if (!clickCell.InBounds(map)) return;

            ThingDef storageDef = DefDatabase<ThingDef>.GetNamedSilentFail("BigStorageBox");
            GoodsDef goodsDef = DefDatabase<GoodsDef>.GetNamedSilentFail("Goods_ResourcesBasic");
            if (storageDef == null || goodsDef == null || goodsDef.GoodsList.NullOrEmpty())
            {
                Messages.Message("生成补货压力测试失败：缺少 BigStorageBox 或 Goods_ResourcesBasic。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<ThingDef> goods = ResolveStressGoods(goodsDef);
            if (goods.Count <= 0)
            {
                Messages.Message("生成补货压力测试失败：Goods_ResourcesBasic 没有可生成物品。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect shopRect = CellRect.CenteredOn(clickCell, 34, 16).ClipInsideMap(map);
            CellRect supplyRect = CellRect.CenteredOn(clickCell + IntVec3.South * 16, 34, 8).ClipInsideMap(map);
            CellRect staffRect = CellRect.CenteredOn(clickCell + IntVec3.South * 24, 24, 6).ClipInsideMap(map);
            if (shopRect.Width < 32 || shopRect.Height < 14 || supplyRect.Width < 28 || supplyRect.Height < 6)
            {
                Messages.Message("生成补货压力测试失败：点选位置离地图边缘太近。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            ClearAreaForShop(map, shopRect);
            ClearAreaForShop(map, supplyRect);
            ClearAreaForShop(map, staffRect);
            SetFloorTerrain(map, shopRect, TerrainDefOf.PavedTile);
            SetFloorTerrain(map, supplyRect, TerrainDefOf.PavedTile);

            Zone_Shop shop = CreateShopZone(map, shopRect);
            Zone_Stockpile stockpile = CreateStressStockpile(map, supplyRect, goods);
            List<Building_SimContainer> storages = SpawnStressStorages(map, storageDef, shopRect);
            ConfigureStressStorages(storages, goodsDef, goods);
            SpawnStressSupplies(map, supplyRect, goods);
            SpawnRestockStaff(map, staffRect);

            Messages.Message($"已生成补货压力测试：货柜 {storages.Count}/{RestockStressStorageCount}，货源区 {stockpile?.CellCount ?? 0} 格，补货员工 {RestockStressStaffCount}。", MessageTypeDefOf.TaskCompletion, false);
        }

        //解析压力测试使用的货品 Def，职责是选择可生成且可堆叠的稳定资源。
        private static List<ThingDef> ResolveStressGoods(GoodsDef goodsDef)
        {
            List<ThingDef> result = new List<ThingDef>();
            HashSet<ThingDef> seen = new HashSet<ThingDef>();
            IReadOnlyList<RuntimeGoodsItem> items = Tool.GoodsCatalog.GetItems(goodsDef.defName);
            for (int i = 0; i < items.Count && result.Count < RestockStressGoodsPerStorage; i++)
                TryAddStressGood(result, seen, items[i]?.thingDef);

            if (result.Count < RestockStressGoodsPerStorage)
            {
                for (int i = 0; i < goodsDef.GoodsList.Count && result.Count < RestockStressGoodsPerStorage; i++)
                    TryAddStressGood(result, seen, goodsDef.GoodsList[i]);
            }

            return result;
        }

        //尝试加入压力测试货品，职责是过滤不可生成或不可堆叠的物品。
        private static void TryAddStressGood(List<ThingDef> result, HashSet<ThingDef> seen, ThingDef thingDef)
        {
            if (thingDef == null || !thingDef.EverHaulable || thingDef.stackLimit <= 1)
                return;

            if (thingDef.MadeFromStuff || !seen.Add(thingDef))
                return;

            result.Add(thingDef);
        }

        //生成压力测试货柜，职责是在商店区内按网格放置 40 个空货柜。
        private static List<Building_SimContainer> SpawnStressStorages(Map map, ThingDef storageDef, CellRect rect)
        {
            List<Building_SimContainer> storages = new List<Building_SimContainer>();
            int columns = 10;
            int rows = 4;
            int startX = rect.minX + 2;
            int startZ = rect.minZ + 2;

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (storages.Count >= RestockStressStorageCount)
                        return storages;

                    IntVec3 cell = new IntVec3(startX + column * 3, 0, startZ + row * 3);
                    if (!rect.Contains(cell) || !cell.InBounds(map))
                        continue;

                    Thing thing = MakeThingWithDefaultStuff(storageDef);
                    if (thing == null)
                        continue;

                    if (thing.def.CanHaveFaction)
                        thing.SetFactionDirect(Faction.OfPlayer);

                    Building_SimContainer storage = GenSpawn.Spawn(thing, cell, map, Rot4.South, WipeMode.Vanish) as Building_SimContainer;
                    if (storage != null)
                        storages.Add(storage);
                }
            }

            return storages;
        }

        //配置压力测试货柜，职责是让每个货柜都有相同目标库存但保持实际库存为空。
        private static void ConfigureStressStorages(List<Building_SimContainer> storages, GoodsDef goodsDef, List<ThingDef> goods)
        {
            if (storages.NullOrEmpty() || goodsDef == null || goods.NullOrEmpty())
                return;

            for (int i = 0; i < storages.Count; i++)
            {
                ThingComp_GoodsData comp = storages[i].GetComp<ThingComp_GoodsData>();
                if (comp == null)
                    continue;

                Dictionary<string, GoodsItemData> settings = new Dictionary<string, GoodsItemData>();
                for (int j = 0; j < goods.Count; j++)
                {
                    settings[goods[j].defName] = new GoodsItemData
                    {
                        enabled = true,
                        count = RestockStressTargetPerItem,
                        restockThreshold = RestockStressTargetPerItem,
                        price = UnityEngine.Mathf.Max(1f, UnityEngine.Mathf.Round(goods[j].BaseMarketValue))
                    };
                }

                comp.ApplySettings(goodsDef.defName, settings);
            }
        }

        //创建压力测试货源存储区，职责是让生成的货物处于普通原版存储区上。
        private static Zone_Stockpile CreateStressStockpile(Map map, CellRect rect, List<ThingDef> goods)
        {
            Zone_Stockpile zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            zone.label = "RSMF 补货压力测试货源区";
            zone.settings.Priority = StoragePriority.Preferred;
            zone.settings.filter.SetDisallowAll();
            for (int i = 0; i < goods.Count; i++)
                zone.settings.filter.SetAllow(goods[i], true);

            foreach (IntVec3 cell in rect.Cells)
            {
                if (cell.InBounds(map) && cell.Standable(map))
                    zone.AddCell(cell);
            }

            zone.Notify_SettingsChanged();
            return zone;
        }

        //生成压力测试货源，职责是在存储区生成大量可被补货工作扫描到的物品堆。
        private static void SpawnStressSupplies(Map map, CellRect rect, List<ThingDef> goods)
        {
            List<IntVec3> cells = rect.Cells.Where(cell => cell.InBounds(map) && cell.Standable(map)).ToList();
            if (cells.Count <= 0 || goods.NullOrEmpty())
                return;

            int cursor = 0;
            for (int i = 0; i < goods.Count; i++)
            {
                ThingDef thingDef = goods[i];
                int stackCount = UnityEngine.Mathf.Max(1, thingDef.stackLimit);
                for (int stack = 0; stack < RestockStressSupplyStacksPerItem; stack++)
                {
                    Thing thing = ThingMaker.MakeThing(thingDef);
                    if (thing == null)
                        continue;

                    thing.stackCount = stackCount;
                    IntVec3 cell = cells[cursor++ % cells.Count];
                    GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
                }
            }
        }

        //生成补货员工，职责是创建一批可执行搬运和补货 WorkGiver 的殖民者。
        private static void SpawnRestockStaff(Map map, CellRect rect)
        {
            List<IntVec3> cells = rect.Cells.Where(cell => cell.InBounds(map) && cell.Standable(map)).ToList();
            if (cells.Count <= 0)
                return;

            WorkTypeDef hauling = WorkTypeDefOf.Hauling;
            for (int i = 0; i < RestockStressStaffCount; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                IntVec3 cell = cells[i % cells.Count];
                GenSpawn.Spawn(pawn, cell, map);
                ConfigureRestockStaffWork(pawn, hauling);
            }
        }

        //配置补货员工工作优先级，职责是让生成的小人立即参与补货压力测试。
        private static void ConfigureRestockStaffWork(Pawn pawn, WorkTypeDef hauling)
        {
            if (pawn?.workSettings == null)
                return;

            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            pawn.workSettings.DisableAll();
            if (hauling != null && !pawn.WorkTypeIsDisabled(hauling))
                pawn.workSettings.SetPriority(hauling, 1);
            pawn.timetable?.SetAssignment(0, TimeAssignmentDefOf.Work);
        }
    }
}
