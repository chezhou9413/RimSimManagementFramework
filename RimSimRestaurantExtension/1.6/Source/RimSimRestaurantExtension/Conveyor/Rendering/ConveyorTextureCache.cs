using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //缓存方向动画的组合贴图，职责是用现有区域蒙版限制传送带表面而不逐帧生成资源。
    [StaticConstructorOnStartup]
    public static class ConveyorTextureCache
    {
        private const string Root = "Things/Building/Restaurant/SushiConveyor/";
        public const int PhaseCount = 24;
        private static readonly Dictionary<int, Texture2D> frames = new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Texture2D> masks = new Dictionary<int, Texture2D>();
        private static Texture2D bottom, region, dye;
        private static readonly Texture2D[] animation = new Texture2D[3];

        //读取源图一次，职责是支持原版不可读纹理的运行期组合。
        static ConveyorTextureCache()
        {
            bottom = Readable(ContentFinder<Texture2D>.Get(Root + "RSR_SushiConveyor_BaseAtlas"));
            region = Readable(ContentFinder<Texture2D>.Get(Root + "Masks/RSR_SushiConveyor_BeltRegion"));
            dye = Readable(ContentFinder<Texture2D>.Get(Root + "RSR_SushiConveyor_Atlas_m"));
            for (int i = 0; i < 3; i++)
                animation[i] = Readable(ContentFinder<Texture2D>.Get(Root + "Animation/RSR_SushiConveyor_Belt_Frame0" + (i + 1)));
        }

        //取得缓存的染色材质，职责是共享纹理并允许建筑涂色。
        public static Material Material(int links, Rot4 direction, int frame, Color primary, Color secondary)
        {
            int shape = (links * 4 + direction.AsInt) * PhaseCount;
            int key = shape + frame;
            if (!frames.TryGetValue(key, out var texture))
            {
                //首次遇到形状时备齐整个周期，播放动画时不再逐帧创建纹理。
                for (int phase = 0; phase < PhaseCount; phase++)
                    frames.Add(shape + phase, Bake(links, direction, phase, false));
                texture = frames[key];
            }
            if (!masks.TryGetValue(links, out var mask))
            { mask = Bake(links, direction, 0, true); masks.Add(links, mask); }
            return MaterialPool.MatFrom(new MaterialRequest(texture, ShaderDatabase.CutoutComplex, primary)
            { maskTex = mask, colorTwo = secondary });
        }

        //裁切图集并沿直线或弯道映射过渡帧，职责是保留底座和区域边界。
        private static Texture2D Bake(int links, Rot4 direction, int frame, bool onlyMask)
        {
            const int size = 192;
            var pixels = new Color[size * size];
            var outDir = direction.FacingCell.ToVector3();
            Vector2 exit = new Vector2(outDir.x, outDir.z);
            Vector2 entry = -exit;
            for (int i = 0; i < 4; i++)
                if ((links & (1 << i)) != 0 && i != direction.AsInt)
                { var d = GenAdj.CardinalDirections[i]; entry = new Vector2(d.x, d.z); break; }
            bool corner = Mathf.Abs(Vector2.Dot(entry, exit)) < 0.5f;
            Vector2 turnCenter = (entry + exit) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (links % 4 * 256 + 32 + x + 0.5f) / 1024f;
                    float v = (links / 4 * 256 + 32 + y + 0.5f) / 1024f;
                    Color color = (onlyMask ? dye : bottom).GetPixelBilinear(u, v);
                    if (!onlyMask)
                    {
                        Color area = region.GetPixelBilinear(u, v);
                        float blend = area.r * area.a;
                        if (blend > 0f)
                        {
                            Vector2 local = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                            float along = Vector2.Dot(local, exit) + 0.5f;
                            float across = Vector2.Dot(local, new Vector2(-exit.y, exit.x)) + 0.5f;
                            if (corner)
                            {
                                Vector2 radial = local - turnCenter;
                                Vector2 start = -exit;
                                float angle = Mathf.Atan2(start.x * radial.y - start.y * radial.x, Vector2.Dot(start, radial));
                                float sign = Mathf.Sign(entry.y * exit.x - entry.x * exit.y);
                                along = Mathf.Clamp01(angle * sign / (Mathf.PI * 0.5f));
                                across = Mathf.Clamp01(radial.magnitude);
                            }
                            Color belt = SampleAnimation(along, Mathf.Lerp(0.34f, 0.66f, across), frame);
                            color = Color.Lerp(color, new Color(belt.r, belt.g, belt.b, color.a), blend);
                        }
                    }
                    pixels[y * size + x] = color;
                }
            var result = new Texture2D(size, size, TextureFormat.RGBA32, false);
            result.name = "餐厅传送带缓存";
            result.wrapMode = TextureWrapMode.Clamp;
            result.SetPixels(pixels);
            result.Apply(false, true);
            return result;
        }

        //对齐相邻素材的位移再插值，职责是让三帧中的带面向出口移动，避免直接淡化产生重影。
        private static Color SampleAnimation(float along, float across, int phase)
        {
            float frame = phase * 3f / PhaseCount;
            int first = Mathf.FloorToInt(frame);
            float fraction = frame - first;
            //原素材横向包含三个重复单元，每个关键帧前进一个单元的三分之一。
            const float shift = 1f / 9f;
            Color current = animation[first].GetPixelBilinear(Mathf.Repeat(along - fraction * shift, 1f), across);
            Color next = animation[(first + 1) % 3].GetPixelBilinear(Mathf.Repeat(along + (1f - fraction) * shift, 1f), across);
            return Color.Lerp(current, next, fraction);
        }

        //通过临时渲染目标取得可读副本，职责是恢复全局渲染目标并释放临时资源。
        private static Texture2D Readable(Texture2D source)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                result.Apply();
                return result;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
    }
}
