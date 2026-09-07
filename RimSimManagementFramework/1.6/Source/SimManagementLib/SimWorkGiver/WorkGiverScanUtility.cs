using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 提供 WorkGiver 扫描错峰和候选窗口轮转工具，负责降低同一 tick 内的大批量扫描峰值。
    /// </summary>
    internal static class WorkGiverScanUtility
    {
        /// <summary>
        /// 计算带稳定抖动的下一次刷新 tick，负责让不同地图和不同 WorkGiver 避免同 tick 集中刷新。
        /// </summary>
        public static int NextStaggeredTick(int now, int baseInterval, int mapId, int salt, int jitterTicks)
        {
            int interval = System.Math.Max(1, baseInterval);
            int jitter = StableOffset(mapId, salt, System.Math.Max(1, jitterTicks));
            return now + interval + jitter;
        }

        /// <summary>
        /// 从地图编号和业务盐值计算稳定相位，负责让错峰结果在同一存档中保持可预测。
        /// </summary>
        public static int StableOffset(int mapId, int salt, int modulo)
        {
            if (modulo <= 1)
                return 0;

            unchecked
            {
                int hash = mapId * 397 ^ salt * 31;
                hash ^= hash >> 16;
                return (hash & 0x7fffffff) % modulo;
            }
        }

        /// <summary>
        /// 从完整候选列表中构建一个轮转窗口，负责让每次 WorkGiver 扫描只检查一部分候选。
        /// </summary>
        public static void BuildThingWindow(List<Thing> source, List<Thing> target, ref int cursor, int preferredWindowSize)
        {
            target.Clear();
            if (source == null || source.Count <= 0)
            {
                cursor = 0;
                return;
            }

            int count = System.Math.Min(System.Math.Max(1, preferredWindowSize), source.Count);
            if (cursor < 0 || cursor >= source.Count)
                cursor = 0;

            for (int i = 0; i < count; i++)
            {
                int index = (cursor + i) % source.Count;
                target.Add(source[index]);
            }

            cursor = (cursor + count) % source.Count;
        }
    }
}
