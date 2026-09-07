using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.Debug
{
    public static partial class DebugActions_SimShop
    {
        private static void ClearAreaForShop(Map map, CellRect rect)
        {
            foreach (IntVec3 cell in rect.Cells)
            {
                List<Thing> things = map.thingGrid.ThingsListAt(cell).ToList();
                foreach (Thing thing in things)
                {
                    if (thing == null || thing.Destroyed) continue;
                    if (thing is Pawn) continue;
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static void BuildRoomShell(Map map, CellRect outer, ThingDef wallDef, ThingDef doorDef)
        {
            IntVec3 doorCell = outer.GetCenterCellOnEdge(Rot4.South);

            foreach (IntVec3 cell in outer.EdgeCells)
            {
                ThingDef buildDef = cell == doorCell ? doorDef : wallDef;
                Thing thing = MakeThingWithDefaultStuff(buildDef);
                if (thing.def.CanHaveFaction)
                    thing.SetFactionDirect(Faction.OfPlayer);

                Rot4 rot = thing.def == doorDef ? Rot4.East : Rot4.North;
                GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
            }
        }

        private static void SetConstructedRoof(Map map, CellRect inner)
        {
            foreach (IntVec3 cell in inner.Cells)
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
        }

        private static Zone_Shop CreateShopZone(Map map, CellRect inner)
        {
            Zone_Shop zone = new Zone_Shop(map.zoneManager);
            map.zoneManager.RegisterZone(zone);

            foreach (IntVec3 cell in inner.Cells)
                zone.AddCell(cell);

            return zone;
        }

        private static Building_CashRegister SpawnRegister(Map map, ThingDef registerDef, IntVec3 cell)
        {
            Thing thing = MakeThingWithDefaultStuff(registerDef);
            if (thing.def.CanHaveFaction)
                thing.SetFactionDirect(Faction.OfPlayer);

            return GenSpawn.Spawn(thing, cell, map, Rot4.South, WipeMode.Vanish) as Building_CashRegister;
        }

        private static List<Building_SimContainer> SpawnStorages(Map map, ThingDef storageDef, CellRect inner, IntVec3 registerCell)
        {
            List<Building_SimContainer> result = new List<Building_SimContainer>();
            IntVec3[] candidates =
            {
                registerCell + IntVec3.North * 2,
                registerCell + IntVec3.North * 2 + IntVec3.East * 2,
                registerCell + IntVec3.North * 2 + IntVec3.West * 2
            };

            foreach (IntVec3 cell in candidates)
            {
                if (!inner.Contains(cell)) continue;

                Thing thing = MakeThingWithDefaultStuff(storageDef);
                if (thing.def.CanHaveFaction)
                    thing.SetFactionDirect(Faction.OfPlayer);

                Building_SimContainer storage = GenSpawn.Spawn(thing, cell, map, Rot4.North, WipeMode.Vanish) as Building_SimContainer;
                if (storage != null)
                    result.Add(storage);
            }

            return result;
        }

        private static List<Building_SimContainer> SpawnLuxuryStorages(Map map, ThingDef storageDef, CellRect inner)
        {
            List<Building_SimContainer> result = new List<Building_SimContainer>();
            if (map == null || storageDef == null) return result;

            IntVec3 northBase = inner.CenterCell + IntVec3.North * UnityEngine.Mathf.Max(2, inner.Height / 2 - 2);
            IntVec3[] candidates =
            {
                northBase + IntVec3.West * 3,
                northBase + IntVec3.West,
                northBase + IntVec3.East,
                northBase + IntVec3.East * 3
            };

            foreach (IntVec3 cell in candidates)
            {
                if (!inner.Contains(cell)) continue;

                Thing thing = MakeThingWithDefaultStuff(storageDef);
                if (thing == null) continue;
                if (thing.def.CanHaveFaction)
                    thing.SetFactionDirect(Faction.OfPlayer);

                Building_SimContainer storage = GenSpawn.Spawn(thing, cell, map, Rot4.North, WipeMode.Vanish) as Building_SimContainer;
                if (storage != null)
                    result.Add(storage);
            }

            return result;
        }

        /// <summary>
        /// 使用默认食品分类配置货柜，并按货柜容量快速铺满库存。
        /// </summary>
        private static void ConfigureAndFillStorage(Building_SimContainer storage, int goodsLimit = 8)
        {
            GoodsDef goodsDef = DefDatabase<GoodsDef>.GetNamedSilentFail("Goods_Foods")
                               ?? DefDatabase<GoodsDef>.AllDefs.FirstOrDefault(d => d.GoodsList != null && d.GoodsList.Count > 0);
            if (goodsDef == null || goodsDef.GoodsList.NullOrEmpty()) return;

            ShopStorageUtility.ConfigureCategoryAndFillToCapacity(storage, goodsDef, UnityEngine.Mathf.Max(1, goodsLimit));
        }

        /// <summary>
        /// 使用指定货品分类配置货柜，并按货柜容量快速铺满库存。
        /// </summary>
        private static void ConfigureAndFillStorage(Building_SimContainer storage, GoodsDef goodsDef)
        {
            ShopStorageUtility.ConfigureCategoryAndFillToCapacity(storage, goodsDef);
        }

        /// <summary>
        /// 将一组货柜按全部 GoodsDef 轮换配置，并分别铺满容量。
        /// </summary>
        private static void FillStoragesWithAllGoods(List<Building_SimContainer> storages)
        {
            if (storages.NullOrEmpty()) return;

            List<GoodsDef> allGoodsDefs = DefDatabase<GoodsDef>.AllDefsListForReading
                .Where(d => d != null && !d.GoodsList.NullOrEmpty())
                .OrderBy(d => d.defName)
                .ToList();
            if (allGoodsDefs.NullOrEmpty()) return;

            for (int i = 0; i < storages.Count; i++)
                ConfigureAndFillStorage(storages[i], allGoodsDefs[i % allGoodsDefs.Count]);
        }

        /// <summary>
        /// 将一组货柜按指定 GoodsDef 集合轮换配置，并分别铺满容量。
        /// </summary>
        private static void FillStoragesWithGoodsSet(List<Building_SimContainer> storages, IEnumerable<GoodsDef> goodsDefs)
        {
            if (storages.NullOrEmpty() || goodsDefs == null) return;

            List<GoodsDef> list = goodsDefs.Where(d => d != null && !d.GoodsList.NullOrEmpty()).ToList();
            if (list.NullOrEmpty()) return;

            for (int i = 0; i < storages.Count; i++)
                ConfigureAndFillStorage(storages[i], list[i % list.Count]);
        }

        private static void SetFloorTerrain(Map map, CellRect inner, TerrainDef floorDef)
        {
            if (map == null || floorDef == null) return;

            foreach (IntVec3 cell in inner.Cells)
            {
                if (!cell.InBounds(map)) continue;
                map.terrainGrid.SetTerrain(cell, floorDef);
            }
        }

        private static void SpawnLuxuryDiningLayout(Map map, CellRect inner, ThingDef tableDef, ThingDef chairDef, ThingDef lampDef, ThingDef plantDef)
        {
            if (map == null) return;

            IntVec3 center = inner.CenterCell + IntVec3.North;

            TrySpawnFurniture(map, tableDef, center, Rot4.North);
            TrySpawnFurniture(map, tableDef, center + IntVec3.East * 3, Rot4.North);

            if (chairDef != null)
            {
                IntVec3[] seats =
                {
                    center + IntVec3.South,
                    center + IntVec3.West,
                    center + IntVec3.East,
                    center + IntVec3.North,
                    center + IntVec3.East * 3 + IntVec3.South,
                    center + IntVec3.East * 3 + IntVec3.West,
                    center + IntVec3.East * 3 + IntVec3.East,
                    center + IntVec3.East * 3 + IntVec3.North
                };

                foreach (IntVec3 seat in seats)
                {
                    if (!inner.Contains(seat)) continue;
                    TrySpawnFurniture(map, chairDef, seat, Rot4.Random);
                }
            }

            if (lampDef != null)
            {
                TrySpawnFurniture(map, lampDef, inner.CenterCell + IntVec3.West * 4 + IntVec3.South * 2, Rot4.North);
                TrySpawnFurniture(map, lampDef, inner.CenterCell + IntVec3.East * 4 + IntVec3.South * 2, Rot4.North);
            }

            if (plantDef != null)
            {
                TrySpawnFurniture(map, plantDef, inner.CenterCell + IntVec3.West * 4 + IntVec3.North * 2, Rot4.North);
                TrySpawnFurniture(map, plantDef, inner.CenterCell + IntVec3.East * 4 + IntVec3.North * 2, Rot4.North);
            }
        }

        private static void TrySpawnFurniture(Map map, ThingDef thingDef, IntVec3 cell, Rot4 rot)
        {
            SpawnFurnitureForShop(map, thingDef, cell, rot);
        }

        /// <summary>
        /// 在商店测试场景中生成指定建筑或家具，并返回生成后的 Thing。
        /// </summary>
        private static Thing SpawnFurnitureForShop(Map map, ThingDef thingDef, IntVec3 cell, Rot4 rot)
        {
            if (map == null || thingDef == null) return null;
            if (!cell.InBounds(map) || !cell.Standable(map)) return null;

            Thing thing = MakeThingWithDefaultStuff(thingDef);
            if (thing == null) return null;

            if (thing.def.CanHaveFaction)
                thing.SetFactionDirect(Faction.OfPlayer);

            return GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
        }

        private static void SpawnCashier(Map map, Building_CashRegister register)
        {
            Pawn cashier = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);

            IntVec3 spawnCell = register.InteractionCell;
            if (!spawnCell.InBounds(map) || !spawnCell.Standable(map))
            {
                if (!CellFinder.TryFindRandomCellNear(register.Position, map, 6, c => c.Standable(map), out spawnCell))
                    spawnCell = register.Position;
            }

            GenSpawn.Spawn(cashier, spawnCell, map);

            JobDef manJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Sim_ManCashRegister");
            if (manJobDef == null) return;

            Job job = JobMaker.MakeJob(manJobDef, register);
            cashier.jobs.TryTakeOrderedJob(job, JobTag.MiscWork);
        }

        private static void SpawnStaffPawn(Map map, IntVec3 nearCell)
        {
            Pawn staff = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);

            IntVec3 spawnCell = nearCell;
            if (!spawnCell.InBounds(map) || !spawnCell.Standable(map))
            {
                if (!CellFinder.TryFindRandomCellNear(nearCell, map, 6, c => c.Standable(map), out spawnCell))
                    spawnCell = nearCell;
            }

            GenSpawn.Spawn(staff, spawnCell, map);
        }

        private static Thing MakeThingWithDefaultStuff(ThingDef def)
        {
            if (def == null) return null;
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            return ThingMaker.MakeThing(def, stuff);
        }
    }
}
