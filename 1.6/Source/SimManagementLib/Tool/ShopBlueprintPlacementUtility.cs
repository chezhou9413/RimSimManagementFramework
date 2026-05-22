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
            plannedCount = 0;
            error = null;

            AcceptanceReport report = CanPlaceAt(map, origin, data, blueprintRot);
            if (!report.Accepted)
            {
                error = report.Reason;
                return false;
            }

            MapComponent_ShopBlueprintPlacement component = map.GetComponent<MapComponent_ShopBlueprintPlacement>();
            PlaceTerrainBlueprints(map, origin, data, blueprintRot, ref plannedCount);
            PlaceBuildingBlueprints(map, origin, data, blueprintRot, component, ref plannedCount);
            CreateShopZone(map, origin, data, blueprintRot);
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
            DrawTerrainPreview(origin, data, blueprintRot, ghostColor);
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

                    ThingDef stuff = GetStuffDef(buildingData);
                    Rot4 rotation = RotateBuildingRotation(ParseRotation(buildingData.rotation), blueprintRot);
                    IntVec3 cell = ToWorldCell(origin, data, buildingData.x, buildingData.z, blueprintRot);
                    AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(def, cell, rotation, map, DebugSettings.godMode, null, null, stuff);
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
        /// 创建蓝图中保存的建筑施工计划，并登记完工后要应用的经营配置。
        /// </summary>
        private static void PlaceBuildingBlueprints(Map map, IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot, MapComponent_ShopBlueprintPlacement component, ref int plannedCount)
        {
            if (data.buildings == null)
                return;

            for (int i = 0; i < data.buildings.Count; i++)
            {
                ShopBlueprintBuildingData buildingData = data.buildings[i];
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(buildingData.defName);
                if (def == null)
                    continue;

                ThingDef stuff = GetStuffDef(buildingData);
                ThingStyleDef styleDef = GetStyleDef(buildingData);
                Rot4 rotation = RotateBuildingRotation(ParseRotation(buildingData.rotation), blueprintRot);
                IntVec3 cell = ToWorldCell(origin, data, buildingData.x, buildingData.z, blueprintRot);
                if (!GenConstruct.CanPlaceBlueprintAt(def, cell, rotation, map, DebugSettings.godMode, null, null, stuff).Accepted)
                    continue;

                if (DebugSettings.godMode || def.GetStatValueAbstract(StatDefOf.WorkToBuild, stuff) == 0f)
                {
                    Thing thing = ThingMaker.MakeThing(def, stuff);
                    thing.SetFactionDirect(Faction.OfPlayer);
                    thing.StyleDef = styleDef;
                    Thing spawned = GenSpawn.Spawn(thing, cell, map, rotation);
                    component?.RegisterPendingBuilding(cell, def, rotation, buildingData);
                    component?.TryApplyPendingConfig(spawned);
                }
                else
                {
                    GenConstruct.PlaceBlueprintForBuild(def, cell, map, rotation, Faction.OfPlayer, stuff, null, styleDef);
                    component?.RegisterPendingBuilding(cell, def, rotation, buildingData);
                }

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
        private static void DrawTerrainPreview(IntVec3 origin, ShopBlueprintData data, Rot4 blueprintRot, Color ghostColor)
        {
            if (data.terrains.NullOrEmpty())
                return;

            List<IntVec3> cells = new List<IntVec3>();
            for (int i = 0; i < data.terrains.Count; i++)
            {
                ShopBlueprintTerrainData terrain = data.terrains[i];
                if (GetTerrainDef(terrain) == null)
                    continue;
                cells.Add(ToWorldCell(origin, data, terrain.x, terrain.z, blueprintRot));
            }

            if (cells.Count > 0)
                GenDraw.DrawFieldEdges(cells, ghostColor);
        }

        /// <summary>
        /// 绘制蓝图中每个建筑的放置虚影。
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

                ThingDef stuff = GetStuffDef(building);
                Rot4 rotation = RotateBuildingRotation(ParseRotation(building.rotation), blueprintRot);
                IntVec3 cell = ToWorldCell(origin, data, building.x, building.z, blueprintRot);
                if (!cell.InBounds(map))
                    continue;

                GhostDrawer.DrawGhostThing(cell, rotation, def, null, ghostColor, AltitudeLayer.Blueprint, null, false, stuff);
                GenDraw.DrawFieldEdges(GenAdj.OccupiedRect(cell, rotation, def.Size).Cells.ToList(), ghostColor);
            }
        }
    }
}
