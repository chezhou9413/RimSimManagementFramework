using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //调度传送带动画缓存，职责是让绘制只查询或入队，将像素生成限制在每帧固定预算内。
    public static class ConveyorTextureCache
    {
        public const int PhaseCount = 24;
        private static readonly Dictionary<int, Texture2D[]> frames = new Dictionary<int, Texture2D[]>();
        private static readonly Dictionary<int, Texture2D> masks = new Dictionary<int, Texture2D>();
        private static readonly HashSet<int> requested = new HashSet<int>();
        private static readonly Queue<int> pending = new Queue<int>();
        private static ConveyorTextureBake active;
        private static int updatedFrame = -1;

        //取得完整周期的缓存材质，职责是在首次遇到形状时只登记任务，未完成则交由调用方绘制静态图集。
        public static Material Material(int links, Rot4 direction, int frame, Color primary, Color secondary)
        {
            int key = links * 4 + direction.AsInt;
            if (!frames.TryGetValue(key, out var textures))
            {
                if (requested.Add(key)) pending.Enqueue(key);
                return null;
            }
            return MaterialPool.MatFrom(new MaterialRequest(textures[frame], ShaderDatabase.CutoutComplex, primary)
            { maskTex = masks[links], colorTwo = secondary });
        }

        //在主线程逐帧推进缓存，职责是每帧共享约一毫秒像素预算，并在一次贴图上传后立即让出。
        public static void UpdatePending()
        {
            if (updatedFrame == RealTime.frameCount) return;
            updatedFrame = RealTime.frameCount;
            if (active == null && pending.Count == 0) return;
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
            if (active == null)
            {
                int key = pending.Dequeue();
                masks.TryGetValue(key / 4, out var mask);
                active = new ConveyorTextureBake(key, mask);
            }
            while (Stopwatch.GetTimestamp() < deadline)
            {
                bool uploaded = active.Step();
                if (active.Complete)
                {
                    frames.Add(active.Key, active.Frames);
                    masks[active.Key / 4] = active.Mask;
                    active = null;
                    return;
                }
                if (uploaded) return;
            }
        }
    }
}
