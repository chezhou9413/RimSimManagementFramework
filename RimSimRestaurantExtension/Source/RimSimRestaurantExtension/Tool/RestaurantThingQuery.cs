using System.Collections.Generic;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //缓存地图物品编号索引，职责是将订单设施查询的全图扫描限制为每地图每 120 Tick 一次。
    internal static class RestaurantThingQuery
    {
        private static readonly Dictionary<Map, Dictionary<int, Thing>> indices = new Dictionary<Map, Dictionary<int, Thing>>();
        private static readonly Dictionary<Map, int> ticks = new Dictionary<Map, int>();

        //清空地图缓存，职责是释放上一个游戏持有的地图对象。
        public static void Reset()
        {
            indices.Clear();
            ticks.Clear();
        }

        //取得编号对应的在图对象，职责是过滤已销毁或离图的缓存引用。
        public static Thing Find(Map map, int id)
        {
            if (map == null || id < 0) return null;
            int now = Verse.Find.TickManager?.TicksGame ?? 0;
            if (!ticks.TryGetValue(map, out int tick) || now - tick >= 120)
            {
                var index = new Dictionary<int, Thing>();
                foreach (Thing thing in map.listerThings.AllThings) index[thing.thingIDNumber] = thing;
                indices[map] = index;
                ticks[map] = now;
            }
            if (indices[map].TryGetValue(id, out Thing found))
                return found.Spawned && !found.Destroyed ? found : null;
            foreach (Thing thing in map.listerThings.AllThings)
                if (thing.thingIDNumber == id) return indices[map][id] = thing;
            return null;
        }
    }
}
