using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责本地店铺蓝图的导出、索引读取、预览图生成和删除。
    /// </summary>
    public static class ShopBlueprintLibrary
    {
        public const int DefaultMaxSize = 50;
        private const string BlueprintFileName = "blueprint.json";
        private const string PreviewFileName = "preview.png";

        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ShopBlueprintData));

        /// <summary>
        /// 返回本地店铺蓝图根目录。
        /// </summary>
        public static string LibraryDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "SimManagementLib", "ShopBlueprints");

        /// <summary>
        /// 读取本地全部蓝图摘要，并跳过损坏或缺失的条目。
        /// </summary>
        public static List<ShopBlueprintLocalRecord> LoadRecords()
        {
            EnsureDirectoryExists();
            List<ShopBlueprintLocalRecord> records = new List<ShopBlueprintLocalRecord>();
            string[] directories = Directory.GetDirectories(LibraryDirectory);
            for (int i = 0; i < directories.Length; i++)
            {
                string blueprintPath = Path.Combine(directories[i], BlueprintFileName);
                if (!File.Exists(blueprintPath))
                    continue;

                ShopBlueprintData data = TryLoadBlueprint(blueprintPath);
                if (data == null)
                    continue;

                EnsureDataDefaults(data);

                records.Add(new ShopBlueprintLocalRecord
                {
                    DirectoryPath = directories[i],
                    BlueprintPath = blueprintPath,
                    PreviewPath = Path.Combine(directories[i], PreviewFileName),
                    Data = data
                });
            }

            return records
                .OrderByDescending(record => record.Data.createdAtTicks)
                .ThenBy(record => record.Data.label)
                .ToList();
        }

        /// <summary>
        /// 将玩家框选的地图范围导出为本地蓝图，并生成结构预览图。
        /// </summary>
        public static bool TrySaveFromRect(Map map, CellRect bounds, int maxSize, out ShopBlueprintLocalRecord record, out string error)
        {
            record = null;
            error = null;

            if (!TryBuildBlueprint(map, bounds, maxSize, out ShopBlueprintData data, out error))
                return false;

            try
            {
                EnsureDataDefaults(data);
                ShopBlueprintSignPayloadUtility.EmbedImages(data);
                EnsureDirectoryExists();
                string directory = Path.Combine(LibraryDirectory, data.blueprintId);
                Directory.CreateDirectory(directory);

                string blueprintPath = Path.Combine(directory, BlueprintFileName);
                using (FileStream stream = File.Create(blueprintPath))
                {
                    Serializer.WriteObject(stream, data);
                }

                string previewPath = Path.Combine(directory, PreviewFileName);
                SavePreviewPng(map, bounds, data, previewPath);

                record = new ShopBlueprintLocalRecord
                {
                    DirectoryPath = directory,
                    BlueprintPath = blueprintPath,
                    PreviewPath = previewPath,
                    Data = data
                };
                return true;
            }
            catch (Exception ex)
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.SaveFailed", ex.Message.Named("message"));
                return false;
            }
        }

        /// <summary>
        /// 删除指定本地蓝图目录。
        /// </summary>
        public static bool TryDelete(ShopBlueprintLocalRecord record, out string error)
        {
            error = null;
            if (record == null || string.IsNullOrEmpty(record.DirectoryPath))
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.RecordMissing");
                return false;
            }

            try
            {
                if (Directory.Exists(record.DirectoryPath))
                    Directory.Delete(record.DirectoryPath, true);
                return true;
            }
            catch (Exception ex)
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.DeleteFailed", ex.Message.Named("message"));
                return false;
            }
        }

        /// <summary>
        /// 将编辑后的蓝图数据写回本地文件，并重新生成预览图。
        /// </summary>
        public static bool TryUpdateRecord(ShopBlueprintLocalRecord record, ShopBlueprintData data, out string error)
        {
            error = null;
            if (record == null || data == null || string.IsNullOrEmpty(record.BlueprintPath))
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.RecordMissing");
                return false;
            }

            try
            {
                EnsureDataDefaults(data);
                ShopBlueprintSignPayloadUtility.EmbedImages(data);

                string directoryPath = string.IsNullOrWhiteSpace(record.DirectoryPath)
                    ? Path.GetDirectoryName(record.BlueprintPath)
                    : record.DirectoryPath;
                if (string.IsNullOrEmpty(data.blueprintId))
                    data.blueprintId = Path.GetFileName(directoryPath) ?? GenerateBlueprintId(data.label);

                using (FileStream stream = File.Create(record.BlueprintPath))
                {
                    Serializer.WriteObject(stream, data);
                }

                string previewPath = string.IsNullOrEmpty(record.PreviewPath)
                    ? Path.Combine(directoryPath, PreviewFileName)
                    : record.PreviewPath;
                if (!File.Exists(previewPath))
                    SavePreviewPng(data, previewPath);

                record.Data = data;
                record.DirectoryPath = directoryPath;
                record.PreviewPath = previewPath;
                return true;
            }
            catch (Exception ex)
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.SaveFailed", ex.Message.Named("message"));
                return false;
            }
        }

        /// <summary>
        /// 从地图矩形范围扫描可保存数据，并校验蓝图尺寸上限。
        /// </summary>
        private static bool TryBuildBlueprint(Map map, CellRect bounds, int maxSize, out ShopBlueprintData data, out string error)
        {
            data = null;
            error = null;

            if (map == null)
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.NoMap");
                return false;
            }

            bounds = bounds.ClipInsideMap(map);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.EmptyRect");
                return false;
            }

            if (bounds.Width > maxSize || bounds.Height > maxSize)
            {
                error = SimTranslation.T("RSMF.Blueprint.Error.TooLarge",
                    bounds.Width.Named("width"),
                    bounds.Height.Named("height"),
                    maxSize.Named("max"));
                return false;
            }

            Zone_Shop sourceZone = FindFirstShopZoneInRect(map, bounds);
            string label = sourceZone != null && !string.IsNullOrWhiteSpace(sourceZone.label)
                ? sourceZone.label
                : SimTranslation.T("RSMF.Blueprint.DefaultLabel", DateTime.Now.ToString("yyyy-MM-dd HH:mm").Named("time"));

            data = new ShopBlueprintData
            {
                blueprintId = GenerateBlueprintId(label),
                label = label,
                sourceMapName = map.info?.parent?.LabelCap ?? map.ToString(),
                sourceZoneId = sourceZone?.ID ?? -1,
                createdAtTicks = DateTime.UtcNow.Ticks,
                width = bounds.Width,
                height = bounds.Height,
                minX = bounds.minX,
                minZ = bounds.minZ,
                schedule = ToScheduleData(sourceZone?.GetSchedule())
            };

            CaptureZoneCells(map, data, bounds);
            CaptureTerrains(map, bounds, data);
            CaptureBuildings(map, bounds, data);
            BlueprintDependencyCollector.PopulateRequiredMods(data);
            return true;
        }

        /// <summary>
        /// 保存框选范围内商店区格子的相对坐标。
        /// </summary>
        private static void CaptureZoneCells(Map map, ShopBlueprintData data, CellRect bounds)
        {
            foreach (IntVec3 cell in bounds.Cells)
            {
                if (map.zoneManager.ZoneAt(cell) == null || !(map.zoneManager.ZoneAt(cell) is Zone_Shop))
                    continue;

                data.zoneCells.Add(new ShopBlueprintCellData
                {
                    x = cell.x - bounds.minX,
                    z = cell.z - bounds.minZ
                });
            }
        }

        /// <summary>
        /// 保存蓝图范围内由玩家铺设的地板数据，不保存天然地形、地基、屋顶或地板涂色。
        /// </summary>
        private static void CaptureTerrains(Map map, CellRect bounds, ShopBlueprintData data)
        {
            for (int z = bounds.minZ; z <= bounds.maxZ; z++)
            {
                for (int x = bounds.minX; x <= bounds.maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map))
                        continue;

                    TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                    if (!ShouldCaptureTerrain(terrain))
                        continue;

                    data.terrains.Add(new ShopBlueprintTerrainData
                    {
                        x = x - bounds.minX,
                        z = z - bounds.minZ,
                        terrainDefName = terrain?.defName ?? "",
                        foundationDefName = "",
                        colorDefName = "",
                        roofDefName = ""
                    });
                }
            }
        }

        /// <summary>
        /// 判断地形是否是玩家铺设的可移除地板，负责避免把天然地面和基础地板写入蓝图。
        /// </summary>
        private static bool ShouldCaptureTerrain(TerrainDef terrain)
        {
            if (terrain == null) return false;
            if (terrain.isFoundation) return false;
            if (terrain.natural) return false;
            return terrain.layerable || terrain.IsFloor;
        }

        /// <summary>
        /// 扫描范围内玩家建筑，并保存建筑基础信息和经营组件配置。
        /// </summary>
        private static void CaptureBuildings(Map map, CellRect bounds, ShopBlueprintData data)
        {
            HashSet<Thing> captured = new HashSet<Thing>();
            int localIndex = 1;
            for (int z = bounds.minZ; z <= bounds.maxZ; z++)
            {
                for (int x = bounds.minX; x <= bounds.maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map))
                        continue;

                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing thing = things[i];
                        if (!ShouldCaptureBuilding(thing) || !captured.Add(thing))
                            continue;

                        data.buildings.Add(ToBuildingData(thing, bounds, localIndex));
                        localIndex++;
                    }
                }
            }
        }

        /// <summary>
        /// 判断指定对象是否应作为店铺蓝图中的建筑保存。
        /// </summary>
        private static bool ShouldCaptureBuilding(Thing thing)
        {
            if (!(thing is Building building)) return false;
            if (building.Destroyed || building.def == null) return false;
            if (building.def.IsBlueprint || building.def.IsFrame) return false;
            if (building.def.destroyOnDrop) return false;
            return true;
        }

        /// <summary>
        /// 将地图建筑转换为蓝图建筑记录。
        /// </summary>
        private static ShopBlueprintBuildingData ToBuildingData(Thing thing, CellRect bounds, int localIndex)
        {
            CellRect rect = thing.OccupiedRect();
            ShopBlueprintBuildingData data = new ShopBlueprintBuildingData
            {
                localId = "building_" + localIndex.ToString("000"),
                defName = thing.def.defName,
                label = thing.LabelCapNoCount,
                stuffDefName = thing.Stuff?.defName ?? "",
                styleDefName = thing.StyleDef?.defName ?? "",
                rotation = thing.Rotation.ToString(),
                x = thing.Position.x - bounds.minX,
                z = thing.Position.z - bounds.minZ,
                width = rect.Width,
                height = rect.Height
            };

            if (thing is Building building)
                data.paintColorDefName = building.PaintColorDef?.defName ?? "";

            ThingWithComps thingWithComps = thing as ThingWithComps;
            data.goods = ToGoodsConfig(thingWithComps?.GetComp<ThingComp_GoodsData>());
            data.sign = ToSignConfig(thingWithComps?.GetComp<ThingComp_CustomSign>());
            data.service = ToServiceConfig(thingWithComps?.GetComp<ThingComp_ServiceProvider>());
            data.vending = ToVendingConfig(thingWithComps?.GetComp<ThingComp_VendingMachine>());
            data.cash = ToCashConfig(thingWithComps?.GetComp<ThingComp_CashStorage>());
            data.container = ToContainerConfig(thing as Building_SimContainer);
            data.externalConfigs = BlueprintExternalConfigRegistry.CaptureConfigs(thing);
            return data;
        }

        /// <summary>
        /// 将货柜商品组件转换为蓝图配置。
        /// </summary>
        private static ShopBlueprintGoodsConfig ToGoodsConfig(ThingComp_GoodsData comp)
        {
            if (comp == null)
                return null;

            ShopBlueprintGoodsConfig config = new ShopBlueprintGoodsConfig
            {
                activeGoodsDefName = comp.ActiveGoodsDefName ?? ""
            };

            foreach (KeyValuePair<string, GoodsItemData> pair in comp.itemData.OrderBy(kv => kv.Key))
            {
                GoodsItemData item = pair.Value;
                if (item == null)
                    continue;

                config.items.Add(new ShopBlueprintGoodsItemConfig
                {
                    thingDefName = pair.Key,
                    enabled = item.enabled,
                    count = item.count,
                    price = item.price
                });
            }

            return config;
        }

        /// <summary>
        /// 将自定义招牌组件转换为蓝图配置。
        /// </summary>
        private static ShopBlueprintSignConfig ToSignConfig(ThingComp_CustomSign comp)
        {
            if (comp == null)
                return null;

                return new ShopBlueprintSignConfig
                {
                    southLayers = ToSignLayers(comp.SouthFace),
                    eastLayers = ToSignLayers(comp.EastFace),
                    northLayers = ToSignLayers(comp.NorthFace),
                    images = new List<ShopBlueprintSignImagePayload>()
                };
        }

        /// <summary>
        /// 将招牌单面图层列表转换为可序列化配置。
        /// </summary>
        private static List<ShopBlueprintSignLayerConfig> ToSignLayers(SignFaceData face)
        {
            List<ShopBlueprintSignLayerConfig> layers = new List<ShopBlueprintSignLayerConfig>();
            if (face?.layers == null)
                return layers;

            for (int i = 0; i < face.layers.Count; i++)
            {
                SignImageLayerData layer = face.layers[i];
                if (layer == null)
                    continue;

                layers.Add(new ShopBlueprintSignLayerConfig
                {
                    imageId = layer.imageId ?? "",
                    label = layer.label ?? "",
                    enabled = layer.enabled,
                    x = layer.x,
                    y = layer.y,
                    scaleX = layer.scaleX,
                    scaleY = layer.scaleY,
                    angle = layer.angle,
                    drawOrder = layer.drawOrder
                });
            }

            return layers;
        }

        /// <summary>
        /// 将服务组件转换为蓝图配置。
        /// </summary>
        private static ShopBlueprintServiceConfig ToServiceConfig(ThingComp_ServiceProvider comp)
        {
            if (comp == null)
                return null;

            comp.EnsureDefaultSlots();
            ShopBlueprintServiceConfig config = new ShopBlueprintServiceConfig
            {
                enabled = comp.enabled
            };

            for (int i = 0; i < comp.serviceSlots.Count; i++)
            {
                ServiceSlotData slot = comp.serviceSlots[i];
                if (slot == null)
                    continue;

                config.slots.Add(new ShopBlueprintServiceSlotConfig
                {
                    serviceDefName = slot.serviceDefName ?? "",
                    enabled = slot.enabled,
                    priceOverride = slot.priceOverride,
                    maxSimultaneousUsers = slot.maxSimultaneousUsers
                });
            }

            return config;
        }

        /// <summary>
        /// 将自动售货机组件转换为蓝图配置。
        /// </summary>
        private static ShopBlueprintVendingConfig ToVendingConfig(ThingComp_VendingMachine comp)
        {
            return comp == null ? null : new ShopBlueprintVendingConfig { enabled = comp.enabled };
        }

        /// <summary>
        /// 将现金组件转换为蓝图配置，只保存自动取现阈值。
        /// </summary>
        private static ShopBlueprintCashConfig ToCashConfig(ThingComp_CashStorage comp)
        {
            return comp == null ? null : new ShopBlueprintCashConfig { withdrawThreshold = comp.WithdrawThreshold };
        }

        /// <summary>
        /// 将货柜建筑自定义名称转换为蓝图配置。
        /// </summary>
        private static ShopBlueprintContainerConfig ToContainerConfig(Building_SimContainer container)
        {
            if (container == null || string.IsNullOrWhiteSpace(container.RenamableLabel) || container.RenamableLabel == container.BaseLabel)
                return null;

            return new ShopBlueprintContainerConfig { customName = container.RenamableLabel };
        }

        /// <summary>
        /// 将营业日程转换为可序列化配置。
        /// </summary>
        private static ShopBlueprintScheduleData ToScheduleData(ShopScheduleData schedule)
        {
            ShopScheduleData source = schedule?.Clone() ?? new ShopScheduleData();
            return new ShopBlueprintScheduleData
            {
                manualOpen = source.manualOpen,
                useSchedule = source.useSchedule,
                openHours = source.openHours != null ? new List<bool>(source.openHours) : new List<bool>()
            };
        }

        /// <summary>
        /// 从磁盘读取单个蓝图文件。
        /// </summary>
        private static ShopBlueprintData TryLoadBlueprint(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    ShopBlueprintData data = Serializer.ReadObject(stream) as ShopBlueprintData;
                    EnsureDataDefaults(data);
                    return data;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 本地店铺蓝图读取失败：" + path + "\n" + ex);
                return null;
            }
        }

        /// <summary>
        /// 生成本地蓝图唯一目录名。
        /// </summary>
        private static string GenerateBlueprintId(string labelSource)
        {
            string label = Slugify(labelSource);
            if (string.IsNullOrEmpty(label))
                label = "shop";

            return DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + label;
        }

        /// <summary>
        /// 查找框选范围中的第一个商店区域，用于带出蓝图名称和营业日程。
        /// </summary>
        private static Zone_Shop FindFirstShopZoneInRect(Map map, CellRect bounds)
        {
            foreach (IntVec3 cell in bounds.Cells)
            {
                Zone_Shop zone = map.zoneManager.ZoneAt(cell) as Zone_Shop;
                if (zone != null)
                    return zone;
            }

            return null;
        }

        /// <summary>
        /// 将名称转换为文件系统安全的短标识。
        /// </summary>
        private static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length && builder.Length < 32; i++)
            {
                char ch = char.ToLowerInvariant(value[i]);
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                    builder.Append('_');
            }

            return builder.ToString().Trim('_');
        }

        /// <summary>
        /// 确保本地蓝图库目录存在。
        /// </summary>
        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(LibraryDirectory))
                Directory.CreateDirectory(LibraryDirectory);
        }

        /// <summary>
        /// 真实地图截图优先生成封面，失败时回退到结构示意预览图。
        /// </summary>
        private static void SavePreviewPng(Map map, CellRect bounds, ShopBlueprintData data, string path)
        {
            EnsureDataDefaults(data);
            SavePreviewPng(data, path);
            ShopBlueprintPreviewCaptureUtility.TryQueueRealPreviewFromMap(map, bounds, path);
        }

        /// <summary>
        /// 根据蓝图结构生成俯视示意预览图。
        /// </summary>
        private static void SavePreviewPng(ShopBlueprintData data, string path)
        {
            EnsureDataDefaults(data);
            const int imageSize = 256;
            Texture2D texture = new Texture2D(imageSize, imageSize, TextureFormat.RGBA32, false);
            Fill(texture, new Color(0.09f, 0.09f, 0.09f, 1f));

            int maxDim = Math.Max(1, Math.Max(data.width, data.height));
            int cellSize = Math.Max(2, (imageSize - 24) / maxDim);
            int offsetX = (imageSize - data.width * cellSize) / 2;
            int offsetY = (imageSize - data.height * cellSize) / 2;

            for (int i = 0; i < data.terrains.Count; i++)
            {
                ShopBlueprintTerrainData terrain = data.terrains[i];
                Color color = string.IsNullOrEmpty(terrain.terrainDefName) ? new Color(0.16f, 0.16f, 0.16f, 1f) : new Color(0.24f, 0.22f, 0.18f, 1f);
                DrawCell(texture, offsetX, offsetY, cellSize, terrain.x, terrain.z, color);
            }

            for (int i = 0; i < data.zoneCells.Count; i++)
            {
                ShopBlueprintCellData cell = data.zoneCells[i];
                DrawCell(texture, offsetX, offsetY, cellSize, cell.x, cell.z, new Color(0.26f, 0.56f, 0.80f, 0.90f));
            }

            for (int i = 0; i < data.buildings.Count; i++)
            {
                ShopBlueprintBuildingData building = data.buildings[i];
                Color color = GetBuildingPreviewColor(building);
                DrawRectCells(texture, offsetX, offsetY, cellSize, building.x, building.z, building.width, building.height, color);
            }

            DrawGrid(texture, offsetX, offsetY, cellSize, data.width, data.height, new Color(0f, 0f, 0f, 0.35f));
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }

        /// <summary>
        /// 为单个建筑蓝图补齐默认字段，避免旧数据的子配置为空时影响依赖扫描和预览绘制。
        /// </summary>
        private static void EnsureBuildingDefaults(ShopBlueprintBuildingData building)
        {
            building.localId = building.localId ?? "";
            building.defName = building.defName ?? "";
            building.label = building.label ?? "";
            building.stuffDefName = building.stuffDefName ?? "";
            building.styleDefName = building.styleDefName ?? "";
            building.paintColorDefName = building.paintColorDefName ?? "";
            building.rotation = string.IsNullOrWhiteSpace(building.rotation) ? "South" : building.rotation;
            building.width = Math.Max(1, building.width);
            building.height = Math.Max(1, building.height);

            if (building.goods != null)
            {
                building.goods.activeGoodsDefName = building.goods.activeGoodsDefName ?? "";
                building.goods.items = building.goods.items ?? new List<ShopBlueprintGoodsItemConfig>();
                for (int i = building.goods.items.Count - 1; i >= 0; i--)
                {
                    ShopBlueprintGoodsItemConfig item = building.goods.items[i];
                    if (item == null)
                    {
                        building.goods.items.RemoveAt(i);
                        continue;
                    }

                    item.thingDefName = item.thingDefName ?? "";
                }
            }

            if (building.sign != null)
            {
                building.sign.southLayers = building.sign.southLayers ?? new List<ShopBlueprintSignLayerConfig>();
                building.sign.eastLayers = building.sign.eastLayers ?? new List<ShopBlueprintSignLayerConfig>();
                building.sign.northLayers = building.sign.northLayers ?? new List<ShopBlueprintSignLayerConfig>();
                building.sign.images = building.sign.images ?? new List<ShopBlueprintSignImagePayload>();
                EnsureSignLayers(building.sign.southLayers);
                EnsureSignLayers(building.sign.eastLayers);
                EnsureSignLayers(building.sign.northLayers);
                EnsureSignImages(building.sign.images);
            }

            if (building.service != null)
            {
                building.service.slots = building.service.slots ?? new List<ShopBlueprintServiceSlotConfig>();
                for (int i = building.service.slots.Count - 1; i >= 0; i--)
                {
                    ShopBlueprintServiceSlotConfig slot = building.service.slots[i];
                    if (slot == null)
                    {
                        building.service.slots.RemoveAt(i);
                        continue;
                    }

                    slot.serviceDefName = slot.serviceDefName ?? "";
                }
            }

            if (building.container != null)
                building.container.customName = building.container.customName ?? "";

            ShopBlueprintTextureAdjustmentBridge.EnsureConfigDefaults(building.textureAdjustment);
            building.externalConfigs = building.externalConfigs ?? new List<ShopBlueprintExternalConfigData>();
            MigrateLegacyTextureAdjustmentConfig(building);
            BlueprintExternalConfigRegistry.EnsureDefaults(building.externalConfigs);
        }

        /// <summary>
        /// 将旧版专用贴图调整字段迁移到通用外部配置列表，保持旧蓝图可继续应用和上传校验。
        /// </summary>
        private static void MigrateLegacyTextureAdjustmentConfig(ShopBlueprintBuildingData building)
        {
            if (building == null || building.textureAdjustment == null)
                return;

            bool alreadyMigrated = building.externalConfigs.Any(config =>
                config != null
                && (string.Equals(config.bridgeId, ShopBlueprintTextureAdjustmentBridge.BridgeIdValue, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(config.packageId, ShopBlueprintTextureAdjustmentBridge.PackageId, StringComparison.OrdinalIgnoreCase)));
            if (!alreadyMigrated)
            {
                ShopBlueprintExternalConfigData config = ShopBlueprintTextureAdjustmentBridge.ToExternalConfig(building.textureAdjustment);
                if (config != null)
                    building.externalConfigs.Add(config);
            }

            building.textureAdjustment = null;
        }

        /// <summary>
        /// 为招牌图层列表补齐默认字段并清理空条目。
        /// </summary>
        private static void EnsureSignLayers(List<ShopBlueprintSignLayerConfig> layers)
        {
            if (layers == null)
                return;

            for (int i = layers.Count - 1; i >= 0; i--)
            {
                ShopBlueprintSignLayerConfig layer = layers[i];
                if (layer == null)
                {
                    layers.RemoveAt(i);
                    continue;
                }

                layer.imageId = layer.imageId ?? "";
                layer.label = layer.label ?? "";
            }
        }

        /// <summary>
        /// 为招牌图片载荷补齐默认字段并移除损坏条目。
        /// </summary>
        private static void EnsureSignImages(List<ShopBlueprintSignImagePayload> images)
        {
            if (images == null)
                return;

            for (int i = images.Count - 1; i >= 0; i--)
            {
                ShopBlueprintSignImagePayload image = images[i];
                if (image == null || string.IsNullOrWhiteSpace(image.imageId))
                {
                    images.RemoveAt(i);
                    continue;
                }

                image.imageId = image.imageId ?? "";
                image.label = image.label ?? "";
                image.pngBase64 = image.pngBase64 ?? "";
            }
        }

        /// <summary>
        /// 为网络导入蓝图重建本地预览图。
        /// </summary>
        public static void TryUpdateImportedPreview(ShopBlueprintData data, string previewPath)
        {
            if (data == null || string.IsNullOrWhiteSpace(previewPath))
                return;

            EnsureDataDefaults(data);
            SavePreviewPng(data, previewPath);
        }

        /// <summary>
        /// 为本地和网络蓝图补齐默认字段，避免旧版本数据在保存、上传和预览时出现空引用。
        /// </summary>
        public static void EnsureDataDefaults(ShopBlueprintData data)
        {
            if (data == null)
                return;

            data.blueprintId = data.blueprintId ?? "";
            data.label = data.label ?? "";
            data.description = data.description ?? "";
            data.sourceMapName = data.sourceMapName ?? "";
            data.zoneCells = data.zoneCells ?? new List<ShopBlueprintCellData>();
            data.terrains = data.terrains ?? new List<ShopBlueprintTerrainData>();
            data.buildings = data.buildings ?? new List<ShopBlueprintBuildingData>();
            data.schedule = data.schedule ?? new ShopBlueprintScheduleData();
            data.schedule.openHours = data.schedule.openHours ?? new List<bool>();
            data.requiredMods = data.requiredMods ?? new List<ShopBlueprintRequiredModData>();
            data.remoteBlueprintCode = data.remoteBlueprintCode ?? "";
            data.remoteAuthorSteamId = data.remoteAuthorSteamId ?? "";
            data.remoteBlueprintSourceKind = data.remoteBlueprintSourceKind ?? "";

            for (int i = data.zoneCells.Count - 1; i >= 0; i--)
            {
                if (data.zoneCells[i] == null)
                    data.zoneCells.RemoveAt(i);
            }

            for (int i = data.terrains.Count - 1; i >= 0; i--)
            {
                ShopBlueprintTerrainData terrain = data.terrains[i];
                if (terrain == null)
                {
                    data.terrains.RemoveAt(i);
                    continue;
                }

                terrain.terrainDefName = terrain.terrainDefName ?? "";
                terrain.foundationDefName = terrain.foundationDefName ?? "";
                terrain.colorDefName = terrain.colorDefName ?? "";
                terrain.roofDefName = terrain.roofDefName ?? "";
            }

            for (int i = data.buildings.Count - 1; i >= 0; i--)
            {
                ShopBlueprintBuildingData building = data.buildings[i];
                if (building == null)
                {
                    data.buildings.RemoveAt(i);
                    continue;
                }

                EnsureBuildingDefaults(building);
            }

            for (int i = data.requiredMods.Count - 1; i >= 0; i--)
            {
                ShopBlueprintRequiredModData mod = data.requiredMods[i];
                if (mod == null)
                {
                    data.requiredMods.RemoveAt(i);
                    continue;
                }

                mod.packageId = mod.packageId ?? "";
                mod.displayName = mod.displayName ?? "";
                mod.steamWorkshopUrl = mod.steamWorkshopUrl ?? "";
            }
        }

        /// <summary>
        /// 根据经营组件类型返回建筑预览颜色。
        /// </summary>
        private static Color GetBuildingPreviewColor(ShopBlueprintBuildingData building)
        {
            if (building.goods != null) return new Color(0.35f, 0.78f, 0.42f, 1f);
            if (building.cash != null) return new Color(0.95f, 0.76f, 0.28f, 1f);
            if (building.sign != null) return new Color(0.86f, 0.42f, 0.80f, 1f);
            if (building.service != null) return new Color(0.38f, 0.72f, 0.92f, 1f);
            return new Color(0.72f, 0.72f, 0.72f, 1f);
        }

        /// <summary>
        /// 用指定颜色填充整张纹理。
        /// </summary>
        private static void Fill(Texture2D texture, Color color)
        {
            Color[] colors = new Color[texture.width * texture.height];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = color;
            texture.SetPixels(colors);
        }

        /// <summary>
        /// 绘制一个蓝图格子。
        /// </summary>
        private static void DrawCell(Texture2D texture, int offsetX, int offsetY, int cellSize, int x, int z, Color color)
        {
            DrawRect(texture, offsetX + x * cellSize, offsetY + z * cellSize, cellSize, cellSize, color);
        }

        /// <summary>
        /// 绘制建筑占用矩形。
        /// </summary>
        private static void DrawRectCells(Texture2D texture, int offsetX, int offsetY, int cellSize, int x, int z, int width, int height, Color color)
        {
            DrawRect(texture, offsetX + x * cellSize, offsetY + z * cellSize, Math.Max(1, width) * cellSize, Math.Max(1, height) * cellSize, color);
        }

        /// <summary>
        /// 绘制蓝图网格线，帮助玩家识别占地。
        /// </summary>
        private static void DrawGrid(Texture2D texture, int offsetX, int offsetY, int cellSize, int width, int height, Color color)
        {
            for (int x = 0; x <= width; x++)
                DrawRect(texture, offsetX + x * cellSize, offsetY, 1, height * cellSize, color);
            for (int z = 0; z <= height; z++)
                DrawRect(texture, offsetX, offsetY + z * cellSize, width * cellSize, 1, color);
        }

        /// <summary>
        /// 在纹理上绘制像素矩形，并裁剪越界区域。
        /// </summary>
        private static void DrawRect(Texture2D texture, int x, int y, int width, int height, Color color)
        {
            int minX = Math.Max(0, x);
            int minY = Math.Max(0, y);
            int maxX = Math.Min(texture.width, x + width);
            int maxY = Math.Min(texture.height, y + height);
            for (int py = minY; py < maxY; py++)
            {
                for (int px = minX; px < maxX; px++)
                    texture.SetPixel(px, py, color);
            }
        }
    }

    /// <summary>
    /// 负责承载本地蓝图文件路径和已解析数据，供 UI 列表使用。
    /// </summary>
    public sealed class ShopBlueprintLocalRecord
    {
        public string DirectoryPath;
        public string BlueprintPath;
        public string PreviewPath;
        public ShopBlueprintData Data;
    }
}
