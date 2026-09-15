using UnityEngine;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //保存源贴图的托管像素快照，职责是让分片生成避免逐像素调用 Unity 原生采样接口。
    internal sealed class ConveyorPixelBuffer
    {
        private readonly Color32[] pixels;
        private readonly int width, height;

        //一次读取源贴图，职责是释放临时可读纹理和渲染目标并保留紧凑像素数组。
        internal ConveyorPixelBuffer(Texture2D source)
        {
            width = source.width;
            height = source.height;
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels = readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (readable != null) Object.Destroy(readable);
            }
        }

        //按循环纹理坐标进行双线性采样，职责是保留动画边界的连续性。
        internal Color Sample(float u, float v)
        {
            float x = u * width - 0.5f, y = v * height - 0.5f;
            int left = Mathf.FloorToInt(x), bottom = Mathf.FloorToInt(y);
            float dx = x - left, dy = y - bottom;
            int x0 = (left % width + width) % width, x1 = (x0 + 1) % width;
            int y0 = (bottom % height + height) % height, y1 = (y0 + 1) % height;
            Color low = Color.LerpUnclamped(pixels[y0 * width + x0], pixels[y0 * width + x1], dx);
            Color high = Color.LerpUnclamped(pixels[y1 * width + x0], pixels[y1 * width + x1], dx);
            return Color.LerpUnclamped(low, high, dy);
        }
    }
}
