using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
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

        private static string AvatarDirectory => Path.Combine(GenFilePaths.SaveDataFolderPath, "SimManagementLib", "CustomerReviewAvatars");
    }
}
