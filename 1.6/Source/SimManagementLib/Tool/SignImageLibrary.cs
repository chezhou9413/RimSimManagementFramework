using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责管理招牌图片的本地图库、压缩导入和分享包导入导出。
    /// </summary>
    public static class SignImageLibrary
    {
        public const int MaxFaceLayerCount = 10;
        public const int MaxImportedTextureEdge = 256;
        private const string IndexFileName = "sign_image_index.json";
        private const string SharePrefix = "SIMSIGN1:";

        private static SignImageLibraryData cache;
        private static readonly DataContractJsonSerializer IndexSerializer = new DataContractJsonSerializer(typeof(SignImageLibraryData));
        private static readonly DataContractJsonSerializer ShareSerializer = new DataContractJsonSerializer(typeof(SignSharePackage));

        /// <summary>
        /// 负责返回本地图库目录。
        /// </summary>
        public static string LibraryDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "SimManagementLib", "SignImages");

        private static string IndexFilePath => Path.Combine(LibraryDirectory, IndexFileName);

        /// <summary>
        /// 负责读取本地图库索引的安全副本。
        /// </summary>
        public static SignImageLibraryData Load()
        {
            if (cache == null)
                cache = LoadFromDisk();

            return Clone(cache);
        }

        /// <summary>
        /// 负责按图片 ID 查找图库记录。
        /// </summary>
        public static SignImageRecord GetRecord(string imageId)
        {
            if (string.IsNullOrEmpty(imageId))
                return null;

            SignImageLibraryData data = Load();
            return data.images.FirstOrDefault(record => string.Equals(record.imageId, imageId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 负责返回图片 ID 对应的本地 PNG 路径。
        /// </summary>
        public static string GetImagePath(string imageId)
        {
            if (string.IsNullOrEmpty(imageId))
                return string.Empty;

            SignImageRecord record = GetRecord(imageId);
            string fileName = !string.IsNullOrEmpty(record?.fileName) ? record.fileName : imageId + ".png";
            return Path.Combine(LibraryDirectory, fileName);
        }

        /// <summary>
        /// 负责判断指定图片 ID 的 PNG 是否存在。
        /// </summary>
        public static bool ImageExists(string imageId)
        {
            string path = GetImagePath(imageId);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        /// <summary>
        /// 负责从玩家给出的本地路径导入图片，并返回图库记录。
        /// </summary>
        public static bool TryImportFromPath(string sourcePath, out SignImageRecord record, out string error)
        {
            record = null;
            error = null;

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                error = "图片路径为空。";
                return false;
            }

            string path = sourcePath.Trim().Trim('"');
            if (!File.Exists(path))
            {
                error = "找不到图片文件。";
                return false;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            {
                error = "只支持 png、jpg、jpeg 图片。";
                return false;
            }

            try
            {
                byte[] sourceBytes = File.ReadAllBytes(path);
                Texture2D sourceTexture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!sourceTexture.LoadImage(sourceBytes))
                {
                    error = "图片解码失败。";
                    return false;
                }

                Texture2D normalizedTexture = ResizeIfNeeded(sourceTexture);
                byte[] pngBytes = normalizedTexture.EncodeToPNG();
                string imageId = CalculateSha256(pngBytes);
                string label = Path.GetFileNameWithoutExtension(path);

                record = SaveImageBytes(imageId, label, normalizedTexture.width, normalizedTexture.height, pngBytes);
                return true;
            }
            catch (Exception ex)
            {
                error = "导入图片失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 负责把图片字节保存到本地图库并更新索引。
        /// </summary>
        public static SignImageRecord SaveImageBytes(string imageId, string label, int width, int height, byte[] pngBytes)
        {
            EnsureDirectoryExists();
            string safeId = NormalizeId(imageId);
            string fileName = safeId + ".png";
            string filePath = Path.Combine(LibraryDirectory, fileName);

            if (!File.Exists(filePath))
                File.WriteAllBytes(filePath, pngBytes);

            SignImageLibraryData data = Load();
            SignImageRecord record = data.images.FirstOrDefault(item => string.Equals(item.imageId, safeId, StringComparison.OrdinalIgnoreCase));
            if (record == null)
            {
                record = new SignImageRecord();
                data.images.Add(record);
            }

            record.imageId = safeId;
            record.label = string.IsNullOrWhiteSpace(label) ? safeId : label.Trim();
            record.fileName = fileName;
            record.width = width;
            record.height = height;
            if (record.createdAtTicks == 0)
                record.createdAtTicks = DateTime.UtcNow.Ticks;

            SaveIndex(data);
            SignTextureCache.Clear(safeId);
            return Clone(record);
        }

        /// <summary>
        /// 负责把当前招牌三面数据导出为包含图片内容的分享文本。
        /// </summary>
        public static bool TryExportShareText(SignFaceData south, SignFaceData east, SignFaceData north, out string shareText, out string error)
        {
            shareText = null;
            error = null;

            try
            {
                SignSharePackage package = new SignSharePackage();
                package.southLayers = ToShareLayers(south);
                package.eastLayers = ToShareLayers(east);
                package.northLayers = ToShareLayers(north);

                HashSet<string> imageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectImageIds(south, imageIds);
                CollectImageIds(east, imageIds);
                CollectImageIds(north, imageIds);

                foreach (string imageId in imageIds)
                {
                    string path = GetImagePath(imageId);
                    if (!File.Exists(path))
                    {
                        error = "图片缺失，无法导出：" + imageId;
                        return false;
                    }

                    byte[] pngBytes = File.ReadAllBytes(path);
                    SignImageRecord record = GetRecord(imageId);
                    package.images.Add(new SignShareImageRecord
                    {
                        imageId = imageId,
                        label = record?.label ?? imageId,
                        width = record?.width ?? 0,
                        height = record?.height ?? 0,
                        pngBase64 = Convert.ToBase64String(pngBytes)
                    });
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    ShareSerializer.WriteObject(stream, package);
                    shareText = SharePrefix + Convert.ToBase64String(stream.ToArray());
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "导出招牌失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 负责导入分享文本，把图片同步写入本地图库，并返回三面数据。
        /// </summary>
        public static bool TryImportShareText(string shareText, out SignFaceData south, out SignFaceData east, out SignFaceData north, out string error)
        {
            south = null;
            east = null;
            north = null;
            error = null;

            if (string.IsNullOrWhiteSpace(shareText) || !shareText.Trim().StartsWith(SharePrefix))
            {
                error = "招牌分享文本格式不正确。";
                return false;
            }

            try
            {
                string payload = shareText.Trim().Substring(SharePrefix.Length);
                byte[] jsonBytes = Convert.FromBase64String(payload);
                SignSharePackage package;
                using (MemoryStream stream = new MemoryStream(jsonBytes))
                {
                    package = (SignSharePackage)ShareSerializer.ReadObject(stream);
                }

                if (package == null)
                {
                    error = "招牌分享包为空。";
                    return false;
                }

                for (int i = 0; i < (package.images?.Count ?? 0); i++)
                {
                    SignShareImageRecord image = package.images[i];
                    if (image == null || string.IsNullOrEmpty(image.pngBase64))
                        continue;

                    byte[] pngBytes = Convert.FromBase64String(image.pngBase64);
                    string actualId = CalculateSha256(pngBytes);
                    string expectedId = NormalizeId(image.imageId);
                    if (!string.IsNullOrEmpty(expectedId) && !string.Equals(actualId, expectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "图片校验失败：" + image.label;
                        return false;
                    }

                    Texture2D probe = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!probe.LoadImage(pngBytes))
                    {
                        error = "分享包中包含无法解码的图片：" + image.label;
                        return false;
                    }

                    SaveImageBytes(actualId, image.label, probe.width, probe.height, pngBytes);
                }

                south = FromShareLayers(package.southLayers);
                east = FromShareLayers(package.eastLayers);
                north = FromShareLayers(package.northLayers);
                return true;
            }
            catch (Exception ex)
            {
                error = "导入招牌失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 负责归一化图片 ID 字符串。
        /// </summary>
        public static string NormalizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        /// <summary>
        /// 负责计算字节内容的 SHA256 字符串。
        /// </summary>
        public static string CalculateSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static Texture2D ResizeIfNeeded(Texture2D source)
        {
            int maxEdge = Math.Max(source.width, source.height);
            if (maxEdge <= MaxImportedTextureEdge)
                return source;

            float scale = MaxImportedTextureEdge / (float)maxEdge;
            int width = Math.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Math.Max(1, Mathf.RoundToInt(source.height * scale));
            Texture2D resized = new Texture2D(width, height, TextureFormat.ARGB32, false);

            for (int y = 0; y < height; y++)
            {
                float v = height <= 1 ? 0f : y / (float)(height - 1);
                for (int x = 0; x < width; x++)
                {
                    float u = width <= 1 ? 0f : x / (float)(width - 1);
                    resized.SetPixel(x, y, source.GetPixelBilinear(u, v));
                }
            }

            resized.Apply();
            return resized;
        }

        private static void SaveIndex(SignImageLibraryData data)
        {
            cache = Sanitize(data);
            EnsureDirectoryExists();
            using (FileStream stream = File.Create(IndexFilePath))
            {
                IndexSerializer.WriteObject(stream, cache);
            }
        }

        private static SignImageLibraryData LoadFromDisk()
        {
            EnsureDirectoryExists();
            if (!File.Exists(IndexFilePath))
                return new SignImageLibraryData();

            try
            {
                using (FileStream stream = File.OpenRead(IndexFilePath))
                {
                    return Sanitize((SignImageLibraryData)IndexSerializer.ReadObject(stream));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 招牌图库索引读取失败，将使用空索引。" + ex);
                return new SignImageLibraryData();
            }
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(LibraryDirectory))
                Directory.CreateDirectory(LibraryDirectory);
        }

        private static SignImageLibraryData Sanitize(SignImageLibraryData data)
        {
            SignImageLibraryData sanitized = new SignImageLibraryData();
            if (data?.images == null)
                return sanitized;

            Dictionary<string, SignImageRecord> merged = new Dictionary<string, SignImageRecord>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < data.images.Count; i++)
            {
                SignImageRecord source = data.images[i];
                string imageId = NormalizeId(source?.imageId);
                if (string.IsNullOrEmpty(imageId))
                    continue;

                merged[imageId] = new SignImageRecord
                {
                    imageId = imageId,
                    label = string.IsNullOrWhiteSpace(source.label) ? imageId : source.label.Trim(),
                    fileName = string.IsNullOrWhiteSpace(source.fileName) ? imageId + ".png" : source.fileName.Trim(),
                    width = Math.Max(0, source.width),
                    height = Math.Max(0, source.height),
                    createdAtTicks = source.createdAtTicks
                };
            }

            sanitized.images = merged.Values.OrderBy(record => record.label).ThenBy(record => record.imageId).ToList();
            return sanitized;
        }

        private static SignImageLibraryData Clone(SignImageLibraryData source)
        {
            SignImageLibraryData clone = new SignImageLibraryData
            {
                version = source?.version ?? 1,
                images = new List<SignImageRecord>()
            };

            if (source?.images == null)
                return clone;

            for (int i = 0; i < source.images.Count; i++)
                clone.images.Add(Clone(source.images[i]));

            return clone;
        }

        private static SignImageRecord Clone(SignImageRecord source)
        {
            if (source == null)
                return null;

            return new SignImageRecord
            {
                imageId = source.imageId ?? "",
                label = source.label ?? "",
                fileName = source.fileName ?? "",
                width = source.width,
                height = source.height,
                createdAtTicks = source.createdAtTicks
            };
        }

        private static void CollectImageIds(SignFaceData face, HashSet<string> imageIds)
        {
            if (face?.layers == null)
                return;

            for (int i = 0; i < face.layers.Count; i++)
            {
                string imageId = NormalizeId(face.layers[i]?.imageId);
                if (!string.IsNullOrEmpty(imageId))
                    imageIds.Add(imageId);
            }
        }

        private static List<SignShareLayerRecord> ToShareLayers(SignFaceData face)
        {
            List<SignShareLayerRecord> records = new List<SignShareLayerRecord>();
            if (face?.layers == null)
                return records;

            for (int i = 0; i < face.layers.Count; i++)
            {
                SignImageLayerData layer = face.layers[i];
                if (layer == null)
                    continue;

                records.Add(new SignShareLayerRecord
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

            return records;
        }

        private static SignFaceData FromShareLayers(List<SignShareLayerRecord> records)
        {
            SignFaceData face = new SignFaceData();
            int count = Math.Min(MaxFaceLayerCount, records?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                SignShareLayerRecord record = records[i];
                if (record == null)
                    continue;

                face.layers.Add(new SignImageLayerData
                {
                    imageId = record.imageId ?? "",
                    label = record.label ?? "",
                    enabled = record.enabled,
                    x = Mathf.Clamp(record.x, -1f, 1f),
                    y = Mathf.Clamp(record.y, -1f, 1f),
                    scaleX = Mathf.Clamp(record.scaleX, 0.05f, 4f),
                    scaleY = Mathf.Clamp(record.scaleY, 0.05f, 4f),
                    angle = record.angle,
                    drawOrder = record.drawOrder
                });
            }

            return face;
        }
    }
}
