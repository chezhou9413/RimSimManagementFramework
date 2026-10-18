using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 管理商店相关 Job 的自绘读条状态，负责接收 Job 上报、绘制地图覆盖层并清理过期数据。
    /// </summary>
    public static class ShopProgressBarUtility
    {
        private const int ExpireTicks = 45;
        private const float BarWidth = 48f;
        private const float BarHeight = 6f;
        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.58f);
        private static readonly Color BorderColor = new Color(1f, 1f, 1f, 0.72f);
        private static readonly Color DefaultFillColor = new Color(0.55f, 0.85f, 1f, 0.95f);
        private static readonly Dictionary<int, ProgressEntry> ActiveBars = new Dictionary<int, ProgressEntry>();
        private static readonly List<int> RemoveBuffer = new List<int>();

        /// <summary>
        /// 为指定 Pawn 上报当前读条进度，地图覆盖层会在下一次 GUI 绘制时显示它。
        /// </summary>
        public static void Report(Pawn pawn, float progress)
        {
            Report(pawn, progress, DefaultFillColor);
        }

        /// <summary>
        /// 为指定 Pawn 上报带自定义颜色的读条进度，进度会被限制在 0 到 1 之间。
        /// </summary>
        public static void Report(Pawn pawn, float progress, Color fillColor)
        {
            if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map == null) return;

            ActiveBars[pawn.thingIDNumber] = new ProgressEntry
            {
                pawn = pawn,
                map = pawn.Map,
                progress = Mathf.Clamp01(progress),
                fillColor = fillColor,
                lastTick = Find.TickManager.TicksGame
            };
        }

        /// <summary>
        /// 清除指定 Pawn 的读条，通常在 Toil 结束或 Job 中断时调用。
        /// </summary>
        public static void Clear(Pawn pawn)
        {
            if (pawn == null) return;
            ActiveBars.Remove(pawn.thingIDNumber);
        }

        /// <summary>
        /// 绘制当前地图上的全部有效读条，并清除过期、离图或失效的读条状态。
        /// </summary>
        public static void DrawForMap(Map map)
        {
            if (map == null || ActiveBars.Count == 0) return;

            RemoveBuffer.Clear();
            int now = Find.TickManager.TicksGame;
            foreach (KeyValuePair<int, ProgressEntry> pair in ActiveBars)
            {
                ProgressEntry entry = pair.Value;
                if (!IsEntryDrawable(entry, map, now))
                {
                    RemoveBuffer.Add(pair.Key);
                    continue;
                }

                DrawEntry(entry);
            }

            for (int i = 0; i < RemoveBuffer.Count; i++)
                ActiveBars.Remove(RemoveBuffer[i]);
            RemoveBuffer.Clear();
        }

        /// <summary>
        /// 判断读条是否仍属于当前地图且仍在有效刷新时间内。
        /// </summary>
        private static bool IsEntryDrawable(ProgressEntry entry, Map map, int now)
        {
            Pawn pawn = entry.pawn;
            if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map != map) return false;
            if (entry.map != map) return false;
            if (now - entry.lastTick > ExpireTicks) return false;
            return true;
        }

        /// <summary>
        /// 在 Pawn 头顶绘制一条紧凑读条，并在结束后恢复全局 GUI 状态。
        /// </summary>
        private static void DrawEntry(ProgressEntry entry)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;

            try
            {
                Vector2 pos = GenMapUI.LabelDrawPosFor(entry.pawn, -0.88f);
                Rect outer = new Rect(pos.x - BarWidth / 2f, pos.y - 14f, BarWidth, BarHeight);
                Rect inner = outer.ContractedBy(1f);
                Rect fill = new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(entry.progress), inner.height);

                GUI.color = Color.white;
                Widgets.DrawBoxSolid(outer, BackgroundColor);
                Widgets.DrawBoxSolid(fill, entry.fillColor);
                Widgets.DrawBoxSolid(new Rect(outer.x, outer.y, outer.width, 1f), BorderColor);
                Widgets.DrawBoxSolid(new Rect(outer.x, outer.yMax - 1f, outer.width, 1f), BorderColor);
                Widgets.DrawBoxSolid(new Rect(outer.x, outer.y, 1f, outer.height), BorderColor);
                Widgets.DrawBoxSolid(new Rect(outer.xMax - 1f, outer.y, 1f, outer.height), BorderColor);
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
            }
        }

        /// <summary>
        /// 保存单个 Pawn 当前读条的绘制数据。
        /// </summary>
        private struct ProgressEntry
        {
            public Pawn pawn;
            public Map map;
            public float progress;
            public Color fillColor;
            public int lastTick;
        }
    }
}
