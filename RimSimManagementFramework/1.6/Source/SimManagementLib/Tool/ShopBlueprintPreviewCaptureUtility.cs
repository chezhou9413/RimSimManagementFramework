using System;
using System.IO;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责在地图真实画面已经完成渲染后，按蓝图区域从屏幕直接裁切真实封面图。
    /// </summary>
    public static class ShopBlueprintPreviewCaptureUtility
    {
        /// <summary>
        /// 负责承载一次待执行的蓝图真实封面截图任务。
        /// </summary>
        private sealed class PendingPreviewCapture
        {
            public Map Map;
            public CellRect Bounds;
            public string Path;
        }

        private const float ScreenPaddingRatio = 0.03f;
        private const int MinimumCapturePixels = 32;
        private const int MaxPreviewPixels = 256;
        private static PendingPreviewCapture pendingCapture;
        private static bool captureQueued;

        /// <summary>
        /// 负责把指定地图区域登记为下一次真实画面裁图任务，成功后会覆盖已有的兜底预览图。
        /// </summary>
        public static bool TryQueueRealPreviewFromMap(Map map, CellRect bounds, string path)
        {
            if (map == null || string.IsNullOrWhiteSpace(path))
                return false;

            bounds = bounds.ClipInsideMap(map);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return false;

            if (Find.CurrentMap != map || Find.Camera == null)
                return false;

            pendingCapture = new PendingPreviewCapture
            {
                Map = map,
                Bounds = bounds,
                Path = path
            };

            if (!captureQueued)
            {
                captureQueued = true;
                LongEventHandler.ExecuteWhenFinished(BeginPendingCapture);
            }

            return true;
        }

        /// <summary>
        /// 负责在当前长事件结束后挂接一次渲染后回调，确保读取到真实屏幕像素。
        /// </summary>
        private static void BeginPendingCapture()
        {
            PendingPreviewCapture capture = pendingCapture;
            if (capture == null)
            {
                captureQueued = false;
                return;
            }

            if (Find.CurrentMap != capture.Map || Find.Camera == null)
            {
                ClearPendingCapture();
                return;
            }

            try
            {
                OnPostRenderHook.HookOnce(Find.Camera, CompletePendingCapture);
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 蓝图真实封面挂接截图失败，将继续使用结构预览图。\n" + ex);
                ClearPendingCapture();
            }
        }

        /// <summary>
        /// 负责在主相机完成当前帧渲染后，直接从屏幕区域裁切真实建筑封面。
        /// </summary>
        private static void CompletePendingCapture()
        {
            PendingPreviewCapture capture = pendingCapture;
            if (capture == null)
            {
                captureQueued = false;
                return;
            }

            Texture2D previewTexture = null;
            try
            {
                Rect captureRect = BuildCaptureRect(capture.Bounds);
                if (captureRect.width < MinimumCapturePixels || captureRect.height < MinimumCapturePixels)
                    return;

                previewTexture = new Texture2D(Mathf.RoundToInt(captureRect.width), Mathf.RoundToInt(captureRect.height), TextureFormat.RGBA32, false);
                previewTexture.ReadPixels(captureRect, 0, 0);
                previewTexture.Apply();

                if (!IsBlankCapture(previewTexture))
                    SaveCompressedPreview(previewTexture, capture.Path);
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 蓝图真实封面截图失败，将继续使用结构预览图。\n" + ex);
            }
            finally
            {
                if (previewTexture != null)
                    UnityEngine.Object.Destroy(previewTexture);

                ClearPendingCapture();
            }
        }

        /// <summary>
        /// 负责把蓝图占地换算为屏幕包围矩形，并扩成适合封面的方形裁剪区域。
        /// </summary>
        private static Rect BuildCaptureRect(CellRect bounds)
        {
            Camera camera = Find.Camera;
            if (camera == null)
                return Rect.zero;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, 0f);

            foreach (Vector3 worldCorner in EnumerateWorldCorners(bounds))
            {
                Vector3 screenPoint = camera.WorldToScreenPoint(worldCorner);
                if (screenPoint.z <= 0.01f)
                    continue;

                min.x = Mathf.Min(min.x, screenPoint.x);
                min.y = Mathf.Min(min.y, screenPoint.y);
                max.x = Mathf.Max(max.x, screenPoint.x);
                max.y = Mathf.Max(max.y, screenPoint.y);
            }

            if (min.x == float.MaxValue || min.y == float.MaxValue)
                return Rect.zero;

            float width = Mathf.Max(1f, max.x - min.x);
            float height = Mathf.Max(1f, max.y - min.y);
            float paddedWidth = width * (1f + 2f * ScreenPaddingRatio);
            float paddedHeight = height * (1f + 2f * ScreenPaddingRatio);
            float centerX = (min.x + max.x) * 0.5f;
            float centerY = (min.y + max.y) * 0.5f;
            float side = Mathf.Max(paddedWidth, paddedHeight);
            float left = centerX - side * 0.5f;
            float bottom = centerY - side * 0.5f;

            left = Mathf.Clamp(left, 0f, Mathf.Max(0f, Screen.width - side));
            bottom = Mathf.Clamp(bottom, 0f, Mathf.Max(0f, Screen.height - side));
            side = Mathf.Min(side, Screen.width, Screen.height);

            return new Rect(left, bottom, side, side);
        }

        /// <summary>
        /// 负责枚举蓝图矩形的地面四角和中心，提升屏幕包围框在斜视角下的稳定性。
        /// </summary>
        private static System.Collections.Generic.IEnumerable<Vector3> EnumerateWorldCorners(CellRect bounds)
        {
            float minX = bounds.minX;
            float maxX = bounds.maxX + 1f;
            float minZ = bounds.minZ;
            float maxZ = bounds.maxZ + 1f;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            yield return new Vector3(minX, 0f, minZ);
            yield return new Vector3(minX, 0f, maxZ);
            yield return new Vector3(maxX, 0f, minZ);
            yield return new Vector3(maxX, 0f, maxZ);
            yield return new Vector3(centerX, 0f, centerZ);
        }

        /// <summary>
        /// 负责检测截图是否是纯黑或纯透明空图，避免把失败结果覆盖到蓝图封面。
        /// </summary>
        private static bool IsBlankCapture(Texture2D texture)
        {
            if (texture == null)
                return true;

            int stepX = Mathf.Max(1, texture.width / 16);
            int stepY = Mathf.Max(1, texture.height / 16);
            for (int y = 0; y < texture.height; y += stepY)
            {
                for (int x = 0; x < texture.width; x += stepX)
                {
                    Color pixel = texture.GetPixel(x, y);
                    if (pixel.a > 0.01f || pixel.r > 0.01f || pixel.g > 0.01f || pixel.b > 0.01f)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 负责把真实截图压缩到不超过 256x256 后再写入磁盘，减少封面体积并统一显示尺寸。
        /// </summary>
        private static void SaveCompressedPreview(Texture2D sourceTexture, string path)
        {
            Texture2D outputTexture = null;
            try
            {
                outputTexture = ResizeIfNeeded(sourceTexture, MaxPreviewPixels);
                File.WriteAllBytes(path, outputTexture.EncodeToPNG());
            }
            finally
            {
                if (outputTexture != null && !ReferenceEquals(outputTexture, sourceTexture))
                    UnityEngine.Object.Destroy(outputTexture);
            }
        }

        /// <summary>
        /// 负责在保持长宽比的前提下，把截图缩放到指定最大边长以内。
        /// </summary>
        private static Texture2D ResizeIfNeeded(Texture2D sourceTexture, int maxSize)
        {
            if (sourceTexture == null)
                return null;

            int sourceWidth = sourceTexture.width;
            int sourceHeight = sourceTexture.height;
            int maxDimension = Mathf.Max(sourceWidth, sourceHeight);
            if (maxDimension <= maxSize)
                return sourceTexture;

            float scale = maxSize / (float)maxDimension;
            int targetWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            int targetHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));
            Texture2D resizedTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);

            for (int y = 0; y < targetHeight; y++)
            {
                float sampleY = targetHeight <= 1 ? 0f : y / (float)(targetHeight - 1);
                for (int x = 0; x < targetWidth; x++)
                {
                    float sampleX = targetWidth <= 1 ? 0f : x / (float)(targetWidth - 1);
                    Color color = sourceTexture.GetPixelBilinear(sampleX, sampleY);
                    resizedTexture.SetPixel(x, y, color);
                }
            }

            resizedTexture.Apply();
            return resizedTexture;
        }

        /// <summary>
        /// 负责清空截图任务状态，允许后续蓝图再次登记真实封面生成。
        /// </summary>
        private static void ClearPendingCapture()
        {
            pendingCapture = null;
            captureQueued = false;
        }
    }
}
