using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //分片生成单种方向和连接的完整动画，职责是复用几何数据并在整个周期就绪后发布。
    [StaticConstructorOnStartup]
    internal sealed class ConveyorTextureBake
    {
        private const int Size = 192;
        private const int PixelCount = Size * Size;
        private const int ChunkSize = 64;
        private readonly Color32[] pixels = new Color32[PixelCount];
        private readonly Color[] baseColors = new Color[PixelCount];
        private readonly float[] blends = new float[PixelCount];
        private readonly Vector2[] coordinates = new Vector2[PixelCount];
        private readonly int links;
        private readonly Vector2 exit, entry, turnCenter;
        private readonly bool corner;
        private readonly float turnSign;
        private int pixel, phase = -1;
        internal readonly int Key;
        internal readonly Texture2D[] Frames = new Texture2D[ConveyorTextureCache.PhaseCount];
        internal Texture2D Mask { get; private set; }
        internal bool Complete => phase == Frames.Length;

        //建立当前形状的临时缓冲，职责是只为队首任务分配内存并复用已生成的蒙版。
        internal ConveyorTextureBake(int key, Texture2D mask)
        {
            Key = key;
            links = key / 4;
            var direction = new Rot4(key % 4);
            var outgoing = direction.FacingCell;
            exit = new Vector2(outgoing.x, outgoing.z);
            entry = -exit;
            for (int i = 0; i < 4; i++)
                if ((links & (1 << i)) != 0 && i != direction.AsInt)
                {
                    var incoming = GenAdj.CardinalDirections[i];
                    entry = new Vector2(incoming.x, incoming.z);
                    break;
                }
            corner = Mathf.Abs(Vector2.Dot(entry, exit)) < 0.5f;
            turnCenter = (entry + exit) * 0.5f;
            turnSign = Mathf.Sign(entry.y * exit.x - entry.x * exit.y);
            Mask = mask;
        }

        //处理固定大小的像素块，返回是否上传贴图，职责是让调度器在块边界检查时间且每帧最多上传一张。
        internal bool Step()
        {
            int end = Mathf.Min(pixel + ChunkSize, PixelCount);
            for (; pixel < end; pixel++)
            {
                if (phase < 0) PreparePixel(pixel);
                else PaintPixel(pixel);
            }
            if (pixel < PixelCount) return false;
            pixel = 0;
            bool uploaded = phase >= 0 || Mask == null;
            if (phase < 0)
            {
                if (Mask == null) Mask = Upload("蒙版");
            }
            else Frames[phase] = Upload("动画_" + phase);
            phase++;
            return uploaded;
        }

        //准备一个像素的底色、蒙版和路径坐标，职责是避免在每个动画相位重复求解弯道。
        private void PreparePixel(int index)
        {
            int x = index % Size, y = index / Size;
            float u = (links % 4 * 256 + 32 + x + 0.5f) / 1024f;
            float v = (links / 4 * 256 + 32 + y + 0.5f) / 1024f;
            baseColors[index] = ConveyorTextureSources.Bottom.Sample(u, v);
            Color area = ConveyorTextureSources.Region.Sample(u, v);
            blends[index] = area.r * area.a;
            if (Mask == null) pixels[index] = ConveyorTextureSources.Dye.Sample(u, v);
            if (blends[index] <= 0f) return;
            var local = new Vector2((x + 0.5f) / Size - 0.5f, (y + 0.5f) / Size - 0.5f);
            float along = Vector2.Dot(local, exit) + 0.5f;
            float across = Vector2.Dot(local, new Vector2(-exit.y, exit.x)) + 0.5f;
            if (corner)
            {
                Vector2 radial = local - turnCenter, start = -exit;
                float angle = Mathf.Atan2(start.x * radial.y - start.y * radial.x, Vector2.Dot(start, radial));
                along = Mathf.Clamp01(angle * turnSign / (Mathf.PI * 0.5f));
                across = Mathf.Clamp01(radial.magnitude);
            }
            coordinates[index] = new Vector2(along, Mathf.Lerp(0.34f, 0.66f, across));
        }

        //组合当前相位的像素，职责是保持三帧素材位移对齐后的平滑插值和底座透明度。
        private void PaintPixel(int index)
        {
            Color color = baseColors[index];
            if (blends[index] > 0f)
            {
                Vector2 uv = coordinates[index];
                float frame = phase * 3f / Frames.Length;
                int first = Mathf.FloorToInt(frame);
                float fraction = frame - first;
                const float shift = 1f / 9f;
                Color current = ConveyorTextureSources.Animation[first].Sample(Mathf.Repeat(uv.x - fraction * shift, 1f), uv.y);
                Color next = ConveyorTextureSources.Animation[(first + 1) % 3].Sample(Mathf.Repeat(uv.x + (1f - fraction) * shift, 1f), uv.y);
                Color belt = Color.Lerp(current, next, fraction);
                color = Color.Lerp(color, new Color(belt.r, belt.g, belt.b, color.a), blends[index]);
            }
            pixels[index] = color;
        }

        //上传已完成的一张贴图，职责是保留固定尺寸并释放 Unity 的可读像素副本。
        private Texture2D Upload(string label)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.name = "餐厅传送带_" + Key + "_" + label;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
