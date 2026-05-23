using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把本地店铺蓝图放置到地图上，并创建地板、建筑施工蓝图、商店区和待应用经营配置。
    /// </summary>
    public static class ShopBlueprintPlacementUtility
    {
        /// <summary>
        /// 负责承载一次蓝图放置的创建、复用和错误结果。
        /// </summary>
        private sealed class BlueprintPlacementResult
        {
            public int createdBlueprintCount;
            public int reusedExistingCount;
            public string error;

            public int totalSatisfiedCount => createdBlueprintCount + reusedExistingCount;
        }

        /// <summary>
        /// 判断蓝图是否可以放置在指定锚点。
        /// </summary>
        public static AcceptanceReport CanPlaceAt(Map map, IntVec3 origin, ShopBlueprintData data)
        {
            return CanPlaceAt(map, origin, data, Rot4.North);
        }

        /// <summary>
        /// 判断蓝图是否可以按指定旋转放置在指定锚点。
        /// </summary>
        public static AcceptanceReport CanPlaceAt(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot)
        {
            if (map == null)
                return SimTranslation.T("RSMF.Blueprint.Error.NoMap");
            if (data == null)
                return SimTranslation.T("RSMF.Blueprint.Error.RecordMissing");

            CellRect bounds = GetPlacementRect(origin, data, blueprintRot);
            if (!bounds.InBounds(map))
                return SimTranslation.T("RSMF.Blueprint.Place.Error.OutOfBounds");

            string firstError = GetFirstPlacementError(map, origin, data, blueprintRot);
            return string.IsNullOrEmpty(firstError) ? AcceptanceReport.WasAccepted : firstError;
        }

        /// <summary>
        /// 将蓝图放置到指定锚点，成功后返回创建的施工蓝图和商店区域数量。
        /// </summary>
        public static bool TryPlaceAt(Map map, IntVec3 origin, ShopBlueprintData data, out int plannedCount, out string error)
        {
            return TryPlaceAt(map, origin, data, Rot4.North, out plannedCount, out error);
        }

        /// <summary>
        /// 将蓝图按指定旋转放置到指定锚点，成功后返回创建的施工蓝图和商店区域数量。
        /// </summary>
        public static bool TryPlaceAt(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot, out int plannedCount, out string error)
        {
            BlueprintPlacementResult result = EvaluatePlacement(map, origin, data, blueprintRot);
            plannedCount = result?.totalSatisfiedCount ?? 0;
            error = result?.error;

            if (result == null || !string.IsNullOrEmpty(result.error))
            {
                return false;
            }

            MapComponent_ShopBlueprintPlacement component = map.GetComponent<MapComponent_ShopBlueprintPlacement>();
            PlaceTerrainBlueprints(map, origin, data, blueprintRot, ref result.createdBlueprintCount);
            PlaceBuildingBlueprints(map, origin, data, blueprintRot, component, ref result.createdBlueprintCount, ref result.reusedExistingCount);
            CreateShopZone(map, origin, data, blueprintRot);
            plannedCount = result.totalSatisfiedCount;
            return true;
        }

        /// <summary>
        /// 绘制蓝图放置时的矩形范围和内部格子提示。
        /// </summary>
        public static void DrawPlacementPreview(Map map, IntVec3 origin, ShopBlueprintData data, bool canPlace)
        {
            DrawPlacementPreview(map, origin, data, Rot4.North, canPlace);
        }

        /// <summary>
        /// 绘制蓝图按指定旋转放置时的整体范围、建筑虚影和内部格子提示。
        /// </summary>
        public static void DrawPlacementPreview(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot, bool canPlace)
        {
            if (map == null || data == null)
                return;

            CellRect rect = GetPlacementRect(origin, data, blueprintRot);
            if (!rect.InBounds(map))
                return;

            Color ghostColor = canPlace ? Designator_Place.CanPlaceColor : Designator_Place.CannotPlaceColor;
            List<IntVec3> cells = rect.Cells.ToList();
            GenDraw.DrawFieldEdges(cells, ghostColor);
            DrawBuildingGhosts(map, origin, data, blueprintRot, ghostColor);
        }

        /// <summary>
        /// 根据锚点和蓝图尺寸返回放置占用矩形。
        /// </summary>
        private static CellRect GetPlacementRect(IntVec3 origin, ShopBlueprintData data)
        {
            return GetPlacementRect(origin, data, Rot4.North);
        }

        /// <summary>
        /// 根据锚点、蓝图尺寸和旋转返回放置占用矩形。
        /// </summary>
        private static CellRect GetPlacementRect(IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot)
        {
            int width = Math.Max(1, data.width);
            int height = Math.Max(1, data.height);
            if (blueprintRot.IsHorizontal)
            {
                int tmp = width;
                width = height;
                height = tmp;
            }

            return new CellRect(origin.x, origin.z, width, height);
        }

        /// <summary>
        /// 返回首个阻止蓝图放置的原版建筑或地形错误。
        /// </summary>
        private static string GetFirstPlacementError(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot)
        {
            if (data.terrains != null)
            {
                for (int i = 0; i < data.terrains.Count; i++)
                {
                    ShopBlueprintTerrainData terrainData = data.terrains[i];
                    TerrainDef terrainDef = GetTerrainDef(terrainData);
                    if (terrainDef == null)
                        continue;

                    IntVec3 cell = ToWorldCell(origin, data, terrainData.x, terrainData.z, blueprintRot);
                    AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(terrainDef, cell, Rot4.North, map, DebugSettings.godMode);
                    if (!report.Accepted)
                        return report.Reason;
                }
            }

            if (data.buildings != null)
            {
                for (int i = 0; i < data.buildings.Count; i++)
                {
                    ShopBlueprintBuildingData buildingData = data.buildings[i];
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(buildingData.defName);
                    if (def == null)
                        continue;

                    IntVec3 cell = ToWorldCell(origin, data, buildingData.x, buildingData.z, blueprintRot);
                    if (TryFindReusableExistingBuilding(map, cell, buildingData, def, out _))
                        continue;

                    ThingDef stuff = GetStuffDef(buildingData, def);
                    Rot4 rotation = RotateBuildingRotation(ParseRotation(buildingData.rotation), blueprintRot);
                    AcceptanceReport report = CanPlaceBuildingOrBlueprintAt(def, cell, rotation, map, stuff);
                    if (!report.Accepted)
                        return report.Reason;
                }
            }

            return null;
        }

        /// <summary>
        /// 创建蓝图中保存的玩家铺设地板施工计划。
        /// </summary>
        private static void PlaceTerrainBlueprints(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot, ref int plannedCount)
        {
            if (data.terrains == null)
                return;

            for (int i = 0; i < data.terrains.Count; i++)
            {
                ShopBlueprintTerrainData terrainData = data.terrains[i];
                TerrainDef terrainDef = GetTerrainDef(terrainData);
                if (terrainDef == null)
                    continue;

                IntVec3 cell = ToWorldCell(origin, data, terrainData.x, terrainData.z, blueprintRot);
                if (!cell.InBounds(map))
                    continue;

                if (!GenConstruct.CanPlaceBlueprintAt(terrainDef, cell, Rot4.North, map, DebugSettings.godMode).Accepted)
                    continue;

                if (DebugSettings.godMode || terrainDef.GetStatValueAbstract(StatDefOf.WorkToBuild) == 0f)
                    map.terrainGrid.SetTerrain(cell, terrainDef);
                else
                    GenConstruct.PlaceBlueprintForBuild(terrainDef, cell, map, Rot4.North, Faction.OfPlayer, null);

                plannedCount++;
            }
        }

        /// <summary>
        /// 按蓝图中的相对商店格创建新的商店区域。
        /// </summary>
        private static void CreateShopZone(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot)
        {
            if (data.zoneCells.NullOrEmpty())
                return;

            Zone_Shop zone = new Zone_Shop(map.zoneManager)
            {
                label = string.IsNullOrEmpty(data.label) ? null : map.zoneManager.NewZoneName(data.label),
            };
            ApplySchedule(zone, data.schedule);
            map.zoneManager.RegisterZone(zone);

            for (int i = 0; i < data.zoneCells.Count; i++)
            {
                ShopBlueprintCellData cellData = data.zoneCells[i];
                IntVec3 cell = ToWorldCell(origin, data, cellData.x, cellData.z, blueprintRot);
                if (!cell.InBounds(map))
                    continue;
                if (map.zoneManager.ZoneAt(cell) != null)
                    continue;

                zone.AddCell(cell);
            }

            if (zone.Cells.NullOrEmpty())
                zone.Deregister();
        }

        /// <summary>
        /// 把蓝图日程写入新建商店区域。
        /// </summary>
        private static void ApplySchedule(Zone_Shop zone, ShopBlueprintScheduleData schedule)
        {
            if (zone == null || schedule == null)
                return;

            ShopScheduleData data = new ShopScheduleData
            {
                manualOpen = schedule.manualOpen,
                useSchedule = schedule.useSchedule,
                openHours = schedule.openHours != null ? new List<bool>(schedule.openHours) : new List<bool>()
            };
            zone.ApplySchedule(data);
        }

        /// <summary>
        /// 返回蓝图地形记录中真正需要玩家建造的地板 Def。
        /// </summary>
        private static TerrainDef GetTerrainDef(ShopBlueprintTerrainData data)
        {
            if (data == null || string.IsNullOrEmpty(data.terrainDefName))
                return null;

            return DefDatabase<TerrainDef>.GetNamedSilentFail(data.terrainDefName);
        }

        /// <summary>
        /// 返回蓝图建筑使用的材料 Def。
        /// </summary>
        private static ThingDef GetStuffDef(ShopBlueprintBuildingData data)
        {
            if (data == null || string.IsNullOrEmpty(data.stuffDefName))
                return null;

            return DefDatabase<ThingDef>.GetNamedSilentFail(data.stuffDefName);
        }

        /// <summary>
        /// 返回蓝图建筑真正可使用的材料 Def，并过滤不需要材料或非法材料的情况。
        /// </summary>
        private static ThingDef GetStuffDef(ShopBlueprintBuildingData data, ThingDef def)
        {
            if (def == null || !def.MadeFromStuff)
                return null;

            ThingDef stuff = GetStuffDef(data);
            if (stuff != null && stuff.IsStuff)
                return stuff;

            return GenStuff.DefaultStuffFor(def);
        }

        /// <summary>
        /// 返回蓝图建筑使用的样式 Def。
        /// </summary>
        private static ThingStyleDef GetStyleDef(ShopBlueprintBuildingData data)
        {
            if (data == null || string.IsNullOrEmpty(data.styleDefName))
                return null;

            return DefDatabase<ThingStyleDef>.GetNamedSilentFail(data.styleDefName);
        }

        /// <summary>
        /// 将蓝图朝向文本转换为 RimWorld 朝向。
        /// </summary>
        private static Rot4 ParseRotation(string rotation)
        {
            if (string.IsNullOrEmpty(rotation))
                return Rot4.North;

            return Rot4.FromString(rotation);
        }

        /// <summary>
        /// 将蓝图相对坐标转换为地图坐标。
        /// </summary>
        private static IntVec3 ToWorldCell(IntVec3 origin, int x, int z)
        {
            return new IntVec3(origin.x + x, 0, origin.z + z);
        }

        /// <summary>
        /// 将蓝图相对坐标按整体旋转转换为地图坐标。
        /// </summary>
        private static IntVec3 ToWorldCell(IntVec3 origin, ShopBlueprintData data, int x, int z, Rot4 blueprintRot)
        {
            IntVec3 local = RotateLocalCell(data, x, z, blueprintRot);
            return ToWorldCell(origin, local.x, local.z);
        }

        /// <summary>
        /// 将蓝图内部单格坐标绕蓝图矩形旋转。
        /// </summary>
        private static IntVec3 RotateLocalCell(ShopBlueprintData data, int x, int z, Rot4 blueprintRot)
        {
            int width = Math.Max(1, data.width);
            int height = Math.Max(1, data.height);
            switch (blueprintRot.AsInt)
            {
                case Rot4.EastInt:
                    return new IntVec3(height - 1 - z, 0, x);
                case Rot4.SouthInt:
                    return new IntVec3(width - 1 - x, 0, height - 1 - z);
                case Rot4.WestInt:
                    return new IntVec3(z, 0, width - 1 - x);
                default:
                    return new IntVec3(x, 0, z);
            }
        }

        /// <summary>
        /// 将建筑自身朝向叠加蓝图整体旋转。
        /// </summary>
        private static Rot4 RotateBuildingRotation(Rot4 buildingRot, Rot4 blueprintRot)
        {
            return new Rot4(buildingRot.AsInt + blueprintRot.AsInt);
        }

        /// <summary>
        /// 绘制蓝图地板格子的淡色提示。
        /// </summary>
        private static void DrawBuildingGhosts(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot, Color ghostColor)
        {
            if (data.buildings.NullOrEmpty())
                return;

            for (int i = 0; i < data.buildings.Count; i++)
            {
                ShopBlueprintBuildingData building = data.buildings[i];
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(building.defName);
                if (def == null)
                    continue;

                ThingDef stuff = GetStuffDef(building, def);
                Rot4 rotation = RotateBuildingRotation(ParseRotation(building.rotation), blueprintRot);
                IntVec3 cell = ToWorldCell(origin, data, building.x, building.z, blueprintRot);
                if (!cell.InBounds(map))
                    continue;

                GhostDrawer.DrawGhostThing(cell, rotation, def, null, ghostColor, AltitudeLayer.Blueprint, null, false, stuff);
            }
        }

        /// <summary>
        /// 预先评估当前蓝图放置是否成功，并收集复用结果。
        /// </summary>
        private static BlueprintPlacementResult EvaluatePlacement(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot)
        {
            AcceptanceReport report = CanPlaceAt(map, origin, data, blueprintRot);
            return new BlueprintPlacementResult
            {
                error = report.Accepted ? null : report.Reason
            };
        }

        /// <summary>
        /// 创建蓝图中保存的建筑施工计划，或复用已存在的普通建筑。
        /// </summary>
        private static void PlaceBuildingBlueprints(
            Map map,
            IntVec3 origin,
            ShopBlueprintData data,
            Rot4 blueprintRot,
            MapComponent_ShopBlueprintPlacement component,
            ref int createdBlueprintCount,
            ref int reusedExistingCount)
        {
            if (data.buildings == null)
                return;

            for (int i = 0; i < data.buildings.Count; i++)
            {
                ShopBlueprintBuildingData buildingData = data.buildings[i];
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(buildingData.defName);
                if (def == null)
                    continue;

                ShopBlueprintBuildingData placementData = PrepareBuildingDataForPlacement(buildingData, blueprintRot);
                Rot4 rotation = RotateBuildingRotation(ParseRotation(buildingData.rotation), blueprintRot);
                IntVec3 cell = ToWorldCell(origin, data, buildingData.x, buildingData.z, blueprintRot);
                if (TryFindReusableExistingBuilding(map, cell, buildingData, def, out Building existing))
                {
                    TryApplyReusableBuildingConfig(existing, placementData);
                    reusedExistingCount++;
                    continue;
                }

                ThingDef stuff = GetStuffDef(buildingData, def);
                ThingStyleDef styleDef = GetStyleDef(buildingData);
                if (!CanPlaceBuildingOrBlueprintAt(def, cell, rotation, map, stuff).Accepted)
                    continue;

                if (ShouldSpawnBuildingDirectly(def, stuff))
                {
                    Thing spawned = SpawnBuildingDirectly(def, stuff, styleDef, cell, map, rotation);
                    if (spawned == null)
                        continue;
                    component?.RegisterPendingBuilding(cell, def, rotation, placementData);
                    component?.TryApplyPendingConfig(spawned);
                }
                else
                {
                    GenConstruct.PlaceBlueprintForBuild(def, cell, map, rotation, Faction.OfPlayer, stuff, null, styleDef);
                    component?.RegisterPendingBuilding(cell, def, rotation, placementData);
                }

                createdBlueprintCount++;
            }
        }

        /// <summary>
        /// 判断建筑应创建施工蓝图还是直接生成真实建筑。
        /// </summary>
        private static bool ShouldSpawnBuildingDirectly(ThingDef def, ThingDef stuff)
        {
            if (def == null)
                return false;

            return DebugSettings.godMode
                || def.blueprintDef == null
                || def.GetStatValueAbstract(StatDefOf.WorkToBuild, stuff) == 0f;
        }

        /// <summary>
        /// 直接生成无法创建普通施工蓝图的建筑，并负责写入阵营和样式。
        /// </summary>
        private static Thing SpawnBuildingDirectly(ThingDef def, ThingDef stuff, ThingStyleDef styleDef, IntVec3 cell, Map map, Rot4 rotation)
        {
            try
            {
                Thing thing = ThingMaker.MakeThing(def, stuff);
                thing.SetFactionDirect(Faction.OfPlayer);
                thing.StyleDef = styleDef;
                return GenSpawn.Spawn(thing, cell, map, rotation);
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 店铺蓝图直接生成建筑失败：" + (def?.defName ?? "null") + "：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 检查普通施工蓝图或直接生成建筑是否能放在目标位置。
        /// </summary>
        private static AcceptanceReport CanPlaceBuildingOrBlueprintAt(ThingDef def, IntVec3 cell, Rot4 rotation, Map map, ThingDef stuff)
        {
            if (def == null)
                return false;

            if (def.blueprintDef != null)
                return GenConstruct.CanPlaceBlueprintAt(def, cell, rotation, map, DebugSettings.godMode, null, null, stuff);

            return GenSpawn.CanSpawnAt(def, cell, map, rotation, false)
                ? AcceptanceReport.WasAccepted
                : "Cannot place " + def.LabelCap + " here.";
        }

        /// <summary>
        /// 为本次放置创建建筑配置副本，负责让外部贴图偏移跟随蓝图整体旋转。
        /// </summary>
        private static ShopBlueprintBuildingData PrepareBuildingDataForPlacement(ShopBlueprintBuildingData data, Rot4 blueprintRot)
        {
            if (data == null || (data.textureAdjustment == null && !BlueprintExternalConfigRegistry.HasConfigs(data.externalConfigs)))
                return data;

            return new ShopBlueprintBuildingData
            {
                localId = data.localId,
                defName = data.defName,
                label = data.label,
                stuffDefName = data.stuffDefName,
                styleDefName = data.styleDefName,
                paintColorDefName = data.paintColorDefName,
                rotation = data.rotation,
                x = data.x,
                z = data.z,
                width = data.width,
                height = data.height,
                goods = data.goods,
                sign = data.sign,
                service = data.service,
                vending = data.vending,
                cash = data.cash,
                container = data.container,
                externalConfigs = BlueprintExternalConfigRegistry.PrepareForPlacement(data.externalConfigs, blueprintRot),
                textureAdjustment = ShopBlueprintTextureAdjustmentBridge.PrepareForPlacement(data.textureAdjustment, blueprintRot)
            };
        }

        /// <summary>
        /// 判断指定蓝图建筑是否允许复用现有普通建筑。
        /// </summary>
        private static bool TryFindReusableExistingBuilding(Map map, IntVec3 cell, ShopBlueprintBuildingData buildingData, ThingDef expectedDef, out Building existing)
        {
            existing = null;
            if (map == null || !cell.InBounds(map) || buildingData == null || expectedDef == null)
                return false;
            if (IsStrictManagedBlueprintBuilding(buildingData, expectedDef))
                return false;

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Building building = things[i] as Building;
                if (building == null || building.Destroyed)
                    continue;
                if (building is Frame)
                    continue;
                if (!string.Equals(building.def?.defName, expectedDef.defName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsReusableForPlayer(building))
                    continue;

                existing = building;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断蓝图建筑是否属于必须严格新建的货柜或招牌。
        /// </summary>
        private static bool IsStrictManagedBlueprintBuilding(ShopBlueprintBuildingData buildingData, ThingDef def)
        {
            if (def == null || buildingData == null)
                return true;
            if (typeof(SimThingClass.Building_SimContainer).IsAssignableFrom(def.thingClass))
                return true;
            if (buildingData.goods != null || buildingData.container != null || buildingData.sign != null)
                return true;
            if (def.comps != null && def.comps.Any(comp => comp?.compClass == typeof(SimThingComp.ThingComp_CustomSign)))
                return true;

            return false;
        }

        /// <summary>
        /// 判断现有建筑是否可以视为玩家可直接复用的建筑。
        /// </summary>
        private static bool IsReusableForPlayer(Building building)
        {
            if (building == null)
                return false;
            if (building.Faction == null)
                return true;
            if (building.Faction == Faction.OfPlayer)
                return true;

            return !building.Faction.HostileTo(Faction.OfPlayer);
        }

        /// <summary>
        /// 把复用命中的普通建筑立即套用兼容蓝图配置。
        /// </summary>
        private static void TryApplyReusableBuildingConfig(Building building, ShopBlueprintBuildingData data)
        {
            if (building == null || data == null)
                return;

            MapComponent_ShopBlueprintPlacement.ApplyBlueprintConfigDirectly(building, new ShopBlueprintBuildingData
            {
                paintColorDefName = data.paintColorDefName ?? "",
                service = data.service,
                vending = data.vending,
                cash = data.cash,
                externalConfigs = data.externalConfigs,
                textureAdjustment = data.textureAdjustment
            });
        }
    }
}
