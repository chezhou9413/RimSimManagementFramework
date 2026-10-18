using LudeonTK;
using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.Debug
{
    //品质商品实时货柜测试场景，职责是生成并填满专业货柜、收银员和顾客业务环境。
    public static partial class DebugActions_SimShop
    {
        //在点选位置生成可直接验证品质销售和实时商品绘制的专业商店。
        [DebugAction("SimShop", "生成品质商品实时货柜测试场景（点选位置）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnQualityRealtimeGoodsShopScenarioAtCell()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            IntVec3 center = UI.MouseCell();
            if (!center.InBounds(map)) return;

            ThingDef registerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sim_CashRegister");
            List<ThingDef> containerDefs = new[]
            {
                "BigWeaponStorageBox",
                "SmallWeaponStorageBox",
                "MiddleClothesStorageBox",
                "ManClothesStorageBox",
                "WomanClothesStorageBox"
            }.Select(DefDatabase<ThingDef>.GetNamedSilentFail).ToList();
            if (registerDef == null || containerDefs.Any(def => def == null))
            {
                Messages.Message("生成品质商品商店失败：缺少收银台或实时绘制货柜 Def。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect outer = CellRect.CenteredOn(center, 6).ClipInsideMap(map);
            if (outer.Width < 11 || outer.Height < 11)
            {
                Messages.Message("生成品质商品商店失败：位置太靠近地图边缘。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect inner = outer.ContractedBy(1);
            ClearAreaForShop(map, outer);
            BuildRoomShell(map, outer, ThingDefOf.Wall, ThingDefOf.Door);
            SetConstructedRoof(map, inner);
            SetFloorTerrain(map, inner, TerrainDefOf.PavedTile);
            Zone_Shop zone = CreateShopZone(map, inner);
            Building_CashRegister register = SpawnRegister(map, registerDef, inner.CenterCell + IntVec3.South * 4);
            List<Building_UniqueGoodsContainer> containers = SpawnRealtimeUniqueGoodsContainers(map, inner, containerDefs);
            if (zone == null || register == null || containers.Count != containerDefs.Count)
            {
                Messages.Message("生成品质商品商店失败：无法创建全部商店设施。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Pawn cashier = SpawnScenarioStaff(map, register.InteractionCell, "SimShopRole_Cashier", zone, 2);
            StartScenarioCashierJob(cashier, register);
            int stocked = FillRealtimeUniqueGoodsContainers(containers);
            int capacity = containers.Sum(container => container.UniqueSlotCount);

            CustomerArrivalManager manager = map.GetComponent<CustomerArrivalManager>();
            string customerResult = "";
            bool customerSpawned = manager != null && manager.ForceSpawnOneWave(true, out customerResult);
            string customerText = customerSpawned ? customerResult : "未能立即生成，可稍后使用强制刷新顾客";
            Messages.Message(
                $"已生成品质商品实时货柜商店：货柜 {containers.Count} 个，现货 {stocked}/{capacity} 件，收银员已分配。顾客：{customerText}。",
                MessageTypeDefOf.TaskCompletion,
                false);
            Find.Selector.ClearSelection();
            Find.Selector.Select(containers[containers.Count - 1]);
        }

        //生成并分配一名测试店员。
        private static Pawn SpawnScenarioStaff(Map map, IntVec3 nearCell, string roleDefName, Zone_Shop zone, int maxCount)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            IntVec3 spawnCell = nearCell;
            if (!spawnCell.InBounds(map) || !spawnCell.Standable(map))
                CellFinder.TryFindRandomCellNear(nearCell, map, 6, cell => cell.Standable(map), out spawnCell);
            GenSpawn.Spawn(pawn, spawnCell, map);
            zone.AddAssignedPawn(roleDefName, pawn, maxCount);
            return pawn;
        }

        //让测试收银员立即开始操作收银台。
        private static void StartScenarioCashierJob(Pawn cashier, Building_CashRegister register)
        {
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Sim_ManCashRegister");
            if (cashier == null || register == null || jobDef == null) return;
            cashier.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, register), JobTag.MiscWork);
        }

        //生成五种实时绘制专业货柜，职责是按两行布局避免不同尺寸建筑互相覆盖。
        private static List<Building_UniqueGoodsContainer> SpawnRealtimeUniqueGoodsContainers(
            Map map,
            CellRect inner,
            IReadOnlyList<ThingDef> containerDefs)
        {
            IntVec3 center = inner.CenterCell;
            IntVec3[] cells =
            {
                center + IntVec3.North * 3 + IntVec3.West * 3,
                center + IntVec3.North * 3 + IntVec3.East * 3,
                center + IntVec3.North * 3,
                center + IntVec3.North + IntVec3.West,
                center + IntVec3.North + IntVec3.East
            };
            List<Building_UniqueGoodsContainer> result = new List<Building_UniqueGoodsContainer>();
            for (int i = 0; i < containerDefs.Count && i < cells.Length; i++)
            {
                Building_UniqueGoodsContainer container = SpawnFurnitureForShop(
                    map,
                    containerDefs[i],
                    cells[i],
                    Rot4.South) as Building_UniqueGoodsContainer;
                if (container != null)
                    result.Add(container);
            }
            return result;
        }

        //填满全部实时绘制货柜，职责是为每件商品设置不同材质、品质和耐久。
        private static int FillRealtimeUniqueGoodsContainers(IReadOnlyList<Building_UniqueGoodsContainer> containers)
        {
            List<ThingDef> apparelDefs = ResolveScenarioQualityDefs(def => def.IsApparel);
            List<ThingDef> weaponDefs = ResolveScenarioQualityDefs(def => def.IsWeapon);
            ThingDef shirtDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_BasicShirt");
            ThingDef hatDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_CowboyHat");
            int stocked = 0;
            int sequence = 0;
            for (int i = 0; i < containers.Count; i++)
            {
                Building_UniqueGoodsContainer container = containers[i];
                IReadOnlyList<ThingDef> candidates = container is Building_MannequinClothesStorageBox
                    ? new[] { shirtDef, hatDef }.Where(def => def != null).ToList()
                    : container.def.defName.Contains("Weapon") ? weaponDefs : apparelDefs;
                stocked += FillRealtimeUniqueGoodsContainer(container, candidates, ref sequence);
            }
            return stocked;
        }

        //收集能够生成材质或品质差异的测试商品定义，职责是优先使用原版内容保证场景稳定。
        private static List<ThingDef> ResolveScenarioQualityDefs(System.Func<ThingDef, bool> predicate)
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def != null && predicate(def) && (def.MadeFromStuff || def.HasComp(typeof(CompQuality))))
                .Where(def => !def.MadeFromStuff || GenStuff.AllowedStuffsFor(def).Any())
                .OrderByDescending(def => def.modContentPack?.IsCoreMod == true)
                .ThenBy(def => def.defName)
                .ToList();
        }

        //把候选商品逐件存入指定货柜，职责是跳过不符合货柜硬限制的定义并填满所有槽位。
        private static int FillRealtimeUniqueGoodsContainer(
            Building_UniqueGoodsContainer container,
            IReadOnlyList<ThingDef> candidates,
            ref int sequence)
        {
            if (container == null || candidates == null || candidates.Count == 0)
                return 0;

            int stocked = 0;
            int attempts = 0;
            int maxAttempts = Mathf.Max(container.UniqueSlotCount * candidates.Count, candidates.Count * 2);
            while (stocked < container.UniqueSlotCount && attempts < maxAttempts)
            {
                ThingDef def = candidates[attempts % candidates.Count];
                Thing thing = MakeScenarioQualityThing(def, sequence++);
                attempts++;
                if (thing != null && container.TryStoreDirectly(thing))
                {
                    stocked++;
                    continue;
                }
                if (thing != null && !thing.Destroyed)
                    thing.Destroy(DestroyMode.Vanish);
            }
            return stocked;
        }

        //生成一件带材质、品质和耐久差异的商品，职责是为销售测试保留真实实例属性。
        private static Thing MakeScenarioQualityThing(ThingDef def, int sequence)
        {
            if (def == null) return null;
            List<ThingDef> stuffs = def.MadeFromStuff ? GenStuff.AllowedStuffsFor(def).ToList() : new List<ThingDef>();
            ThingDef stuff = stuffs.Count > 0 ? stuffs[sequence % stuffs.Count] : null;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            QualityCategory[] qualities =
            {
                QualityCategory.Normal,
                QualityCategory.Good,
                QualityCategory.Excellent,
                QualityCategory.Masterwork
            };
            thing.TryGetComp<CompQuality>()?.SetQuality(qualities[sequence % qualities.Length], ArtGenerationContext.Colony);
            if (thing.MaxHitPoints > 1)
                thing.HitPoints = Mathf.Max(1, Mathf.RoundToInt(thing.MaxHitPoints * (1f - sequence % 3 * 0.15f)));
            return thing;
        }
    }
}
