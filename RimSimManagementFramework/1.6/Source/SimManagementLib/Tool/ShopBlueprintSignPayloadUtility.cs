using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把自定义招牌图片打包进蓝图，并在导入或放置前恢复到本地招牌图库。
    /// </summary>
    public static class ShopBlueprintSignPayloadUtility
    {
        /// <summary>
        /// 为蓝图内所有招牌配置补齐图片内容，职责是让上传的 blueprint.json 自带招牌图片。
        /// </summary>
        public static void EmbedImages(ShopBlueprintData data)
        {
            if (data?.buildings == null)
                return;

            for (int i = 0; i < data.buildings.Count; i++)
                EmbedImages(data.buildings[i]?.sign);
        }

        /// <summary>
        /// 把蓝图里携带的招牌图片写入本地图库，职责是让下载蓝图放置后能正常绘制招牌。
        /// </summary>
        public static void ImportImages(ShopBlueprintData data)
        {
            if (data?.buildings == null)
                return;

            for (int i = 0; i < data.buildings.Count; i++)
                ImportImages(data.buildings[i]?.sign);
        }

        /// <summary>
        /// 为单个招牌配置补齐图片内容。
        /// </summary>
        private static void EmbedImages(ShopBlueprintSignConfig sign)
        {
            if (sign == null)
                return;

            sign.images = sign.images ?? new List<ShopBlueprintSignImagePayload>();
            HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sign.images.Count; i++)
            {
                string imageId = sign.images[i]?.imageId;
                if (!string.IsNullOrWhiteSpace(imageId))
                    existing.Add(imageId);
            }

            HashSet<string> required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectImageIds(sign.southLayers, required);
            CollectImageIds(sign.eastLayers, required);
            CollectImageIds(sign.northLayers, required);

            foreach (string imageId in required)
            {
                if (existing.Contains(imageId))
                    continue;

                ShopBlueprintSignImagePayload payload = BuildPayload(imageId);
                if (payload == null)
                    continue;

                sign.images.Add(payload);
                existing.Add(imageId);
            }
        }

        /// <summary>
        /// 导入单个招牌配置中携带的图片内容。
        /// </summary>
        private static void ImportImages(ShopBlueprintSignConfig sign)
        {
            if (sign?.images == null)
                return;

            for (int i = 0; i < sign.images.Count; i++)
            {
                ShopBlueprintSignImagePayload image = sign.images[i];
                if (image == null || string.IsNullOrWhiteSpace(image.pngBase64))
                    continue;

                try
                {
                    byte[] pngBytes = Convert.FromBase64String(image.pngBase64);
                    string actualId = SignImageLibrary.CalculateSha256(pngBytes);
                    string expectedId = SignImageLibrary.NormalizeId(image.imageId);
                    if (!string.IsNullOrEmpty(expectedId) && !string.Equals(actualId, expectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warning("[SimManagementLib] 蓝图招牌图片校验失败，已跳过：" + (image.label ?? expectedId));
                        continue;
                    }

                    Texture2D probe = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!probe.LoadImage(pngBytes))
                        continue;

                    SignImageLibrary.SaveImageBytes(actualId, image.label, probe.width, probe.height, pngBytes);
                    UnityEngine.Object.Destroy(probe);
                }
                catch (Exception ex)
                {
                    Log.Warning("[SimManagementLib] 蓝图招牌图片导入失败：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 从本地图库构造蓝图图片载荷。
        /// </summary>
        private static ShopBlueprintSignImagePayload BuildPayload(string imageId)
        {
            string path = SignImageLibrary.GetImagePath(imageId);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            byte[] pngBytes = File.ReadAllBytes(path);
            SignImageRecord record = SignImageLibrary.GetRecord(imageId);
            return new ShopBlueprintSignImagePayload
            {
                imageId = imageId,
                label = record?.label ?? imageId,
                width = record?.width ?? 0,
                height = record?.height ?? 0,
                pngBase64 = Convert.ToBase64String(pngBytes)
            };
        }

        /// <summary>
        /// 收集图层列表中引用的图片 ID。
        /// </summary>
        private static void CollectImageIds(List<ShopBlueprintSignLayerConfig> layers, HashSet<string> imageIds)
        {
            if (layers == null || imageIds == null)
                return;

            for (int i = 0; i < layers.Count; i++)
            {
                string imageId = layers[i]?.imageId;
                if (!string.IsNullOrWhiteSpace(imageId))
                    imageIds.Add(imageId);
            }
        }
    }
}
