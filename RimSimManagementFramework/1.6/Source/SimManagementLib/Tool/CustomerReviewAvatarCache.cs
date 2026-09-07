using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 管理顾客点评头像缓存，负责把离店顾客头像保存为本地 PNG 并在 UI 中读取。
    /// </summary>
    public static class CustomerReviewAvatarCache
    {
        private const int AvatarSize = 160;
        private static readonly Dictionary<string, Texture2D> AvatarTextures = new Dictionary<string, Texture2D>();

        /// <summary>
        /// 为指定顾客保存头像并返回头像缓存编号。
        /// </summary>
        public static string SaveAvatar(Pawn pawn, string reviewId)
        {
            if (pawn == null || string.IsNullOrEmpty(reviewId)) return "";
            try
            {
                string directory = AvatarDirectory;
                Directory.CreateDirectory(directory);
                string fileName = reviewId + ".png";
                string path = Path.Combine(directory, fileName);
                RenderTexture portrait = PortraitsCache.Get(
                    pawn,
                    new Vector2(AvatarSize, AvatarSize),
                    Rot4.South,
                    new Vector3(0f, 0f, 0.08f),
                    1.8f,
                    renderHeadgear: true,
                    renderClothes: true);
                Texture2D texture = portrait.CreateTexture2D(TextureFormat.ARGB32, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.Destroy(texture);
                return fileName;
            }
            catch (Exception ex)
            {
                Log.Warning("保存顾客点评头像失败: " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// 按头像编号读取本地 PNG 纹理，失败时返回空值。
        /// </summary>
        public static Texture2D LoadAvatar(string avatarImageId)
        {
            if (string.IsNullOrEmpty(avatarImageId)) return null;
            if (AvatarTextures.TryGetValue(avatarImageId, out Texture2D cached))
                return cached;

            string path = Path.Combine(AvatarDirectory, avatarImageId);
            if (!File.Exists(path)) return null;
            try
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (texture.LoadImage(File.ReadAllBytes(path)))
                {
                    AvatarTextures[avatarImageId] = texture;
                    return texture;
                }
                UnityEngine.Object.Destroy(texture);
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// 清理不再被点评记录或待生成快照引用的头像文件，并同步释放内存纹理缓存。
        /// </summary>
        public static void CleanupUnusedAvatars(IEnumerable<string> referencedAvatarImageIds)
        {
            try
            {
                string directory = AvatarDirectory;
                if (!Directory.Exists(directory))
                    return;

                HashSet<string> referencedIds = BuildReferencedIdSet(referencedAvatarImageIds);
                string[] files = Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = Path.GetFileName(files[i]);
                    if (string.IsNullOrEmpty(fileName) || referencedIds.Contains(fileName))
                        continue;

                    DeleteAvatarFile(files[i], fileName);
                }

                CleanupTextureCache(referencedIds);
            }
            catch (Exception ex)
            {
                Log.Warning("清理顾客点评头像缓存失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 根据当前仍被引用的头像编号创建查找集合。
        /// </summary>
        private static HashSet<string> BuildReferencedIdSet(IEnumerable<string> referencedAvatarImageIds)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (referencedAvatarImageIds == null)
                return result;

            foreach (string avatarImageId in referencedAvatarImageIds)
            {
                if (string.IsNullOrWhiteSpace(avatarImageId))
                    continue;

                result.Add(Path.GetFileName(avatarImageId));
            }

            return result;
        }

        /// <summary>
        /// 删除单个头像文件，并移除同名内存纹理。
        /// </summary>
        private static void DeleteAvatarFile(string path, string avatarImageId)
        {
            try
            {
                File.Delete(path);
                RemoveTextureCache(avatarImageId);
            }
            catch (Exception ex)
            {
                Log.Warning("删除顾客点评头像失败: " + avatarImageId + "，" + ex.Message);
            }
        }

        /// <summary>
        /// 移除内存中已经没有引用的头像纹理。
        /// </summary>
        private static void CleanupTextureCache(HashSet<string> referencedIds)
        {
            List<string> unusedKeys = AvatarTextures.Keys
                .Where(key => !referencedIds.Contains(key))
                .ToList();
            for (int i = 0; i < unusedKeys.Count; i++)
                RemoveTextureCache(unusedKeys[i]);
        }

        /// <summary>
        /// 从内存缓存移除指定头像纹理，并释放 Unity 纹理对象。
        /// </summary>
        private static void RemoveTextureCache(string avatarImageId)
        {
            if (string.IsNullOrEmpty(avatarImageId))
                return;
            if (!AvatarTextures.TryGetValue(avatarImageId, out Texture2D texture))
                return;

            AvatarTextures.Remove(avatarImageId);
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }

        private static string AvatarDirectory => Path.Combine(GenFilePaths.SaveDataFolderPath, "SimManagementLib", "CustomerReviewAvatars");
    }
}
