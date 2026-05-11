using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把本地图案库图片懒加载为纹理材质，并缓存绘制资源。
    /// </summary>
    public static class SignTextureCache
    {
        private static readonly Dictionary<string, Texture2D> TexturesById = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Material> MaterialsById = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Graphic> GraphicsByKey = new Dictionary<string, Graphic>(StringComparer.OrdinalIgnoreCase);
        private static Texture2D missingTexture;
        private static Material missingMaterial;

        /// <summary>
        /// 负责获取指定图片 ID 的材质，缺图时返回占位材质。
        /// </summary>
        public static Material GetMaterial(string imageId)
        {
            if (string.IsNullOrEmpty(imageId))
                return MissingMaterial;

            if (MaterialsById.TryGetValue(imageId, out Material material))
                return material;

            Texture2D texture = GetTexture(imageId);
            MaterialRequest request = new MaterialRequest(texture, ShaderDatabase.Cutout, Color.white);
            material = MaterialPool.MatFrom(request);
            MaterialsById[imageId] = material;
            return material;
        }

        /// <summary>
        /// 负责获取指定图片 ID 和尺寸对应的 RimWorld Graphic。
        /// </summary>
        public static Graphic GetGraphic(string imageId, Vector2 drawSize)
        {
            string key = (imageId ?? "") + "|" + drawSize.x.ToString("0.###") + "|" + drawSize.y.ToString("0.###");
            if (GraphicsByKey.TryGetValue(key, out Graphic graphic))
                return graphic;

            Texture2D texture = GetTexture(imageId);
            graphic = GraphicDatabase.Get<Graphic_Single>(texture, ShaderDatabase.Cutout, drawSize, Color.white, 0);
            GraphicsByKey[key] = graphic;
            return graphic;
        }

        /// <summary>
        /// 负责获取指定图片 ID 的纹理，缺图或解码失败时返回占位纹理。
        /// </summary>
        public static Texture2D GetTexture(string imageId)
        {
            if (string.IsNullOrEmpty(imageId))
                return MissingTexture;

            if (TexturesById.TryGetValue(imageId, out Texture2D texture))
                return texture;

            string path = SignImageLibrary.GetImagePath(imageId);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return MissingTexture;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.ARGB32, true);
                if (!texture.LoadImage(bytes))
                    return MissingTexture;

                texture.name = "SimSign_" + imageId;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                TexturesById[imageId] = texture;
                return texture;
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 招牌图片加载失败：" + imageId + " " + ex.Message);
                return MissingTexture;
            }
        }

        /// <summary>
        /// 负责清理指定图片 ID 的缓存。
        /// </summary>
        public static void Clear(string imageId)
        {
            if (string.IsNullOrEmpty(imageId))
            {
                TexturesById.Clear();
                MaterialsById.Clear();
                GraphicsByKey.Clear();
                return;
            }

            TexturesById.Remove(imageId);
            MaterialsById.Remove(imageId);
            foreach (string key in new List<string>(GraphicsByKey.Keys))
            {
                if (key.StartsWith(imageId + "|", StringComparison.OrdinalIgnoreCase))
                    GraphicsByKey.Remove(key);
            }
        }

        private static Texture2D MissingTexture
        {
            get
            {
                if (missingTexture != null)
                    return missingTexture;

                missingTexture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                missingTexture.SetPixels(new[]
                {
                    new Color(1f, 0f, 1f, 1f),
                    new Color(0f, 0f, 0f, 1f),
                    new Color(0f, 0f, 0f, 1f),
                    new Color(1f, 0f, 1f, 1f)
                });
                missingTexture.Apply();
                missingTexture.name = "SimSign_Missing";
                missingTexture.filterMode = FilterMode.Point;
                return missingTexture;
            }
        }

        private static Material MissingMaterial
        {
            get
            {
                if (missingMaterial == null)
                    missingMaterial = MaterialPool.MatFrom(new MaterialRequest(MissingTexture, ShaderDatabase.Cutout, Color.white));
                return missingMaterial;
            }
        }
    }
}
