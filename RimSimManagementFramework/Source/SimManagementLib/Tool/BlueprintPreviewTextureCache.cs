using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责缓存蓝图预览图纹理，避免 IMGUI 每帧重复读盘和创建 Texture2D。
    /// </summary>
    public static class BlueprintPreviewTextureCache
    {
        private const float FailedRetrySeconds = 5f;
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>();

        /// <summary>
        /// 按路径读取预览图纹理，负责命中缓存、失败节流和首次加载。
        /// </summary>
        public static Texture2D Get(string previewPath)
        {
            if (string.IsNullOrEmpty(previewPath))
                return null;

            if (Cache.TryGetValue(previewPath, out CacheEntry entry))
            {
                if (entry.Texture != null)
                    return entry.Texture;

                if (Time.realtimeSinceStartup < entry.NextRetryTime)
                    return null;
            }

            return LoadAndCache(previewPath);
        }

        /// <summary>
        /// 清空预览图缓存，负责销毁已经创建的 Unity 纹理资源。
        /// </summary>
        public static void Clear()
        {
            foreach (CacheEntry entry in Cache.Values)
            {
                if (entry?.Texture != null)
                    UnityEngine.Object.Destroy(entry.Texture);
            }

            Cache.Clear();
        }

        /// <summary>
        /// 从磁盘加载预览图并记录缓存状态。
        /// </summary>
        private static Texture2D LoadAndCache(string previewPath)
        {
            CacheEntry entry = new CacheEntry();

            try
            {
                if (!File.Exists(previewPath))
                {
                    entry.NextRetryTime = Time.realtimeSinceStartup + FailedRetrySeconds;
                    Cache[previewPath] = entry;
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(previewPath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(bytes))
                {
                    entry.Texture = texture;
                    Cache[previewPath] = entry;
                    return texture;
                }

                UnityEngine.Object.Destroy(texture);
            }
            catch (Exception)
            {
            }

            entry.NextRetryTime = Time.realtimeSinceStartup + FailedRetrySeconds;
            Cache[previewPath] = entry;
            return null;
        }

        /// <summary>
        /// 保存单个预览图路径的纹理和失败重试时间。
        /// </summary>
        private sealed class CacheEntry
        {
            public Texture2D Texture;
            public float NextRetryTime;
        }
    }
}
