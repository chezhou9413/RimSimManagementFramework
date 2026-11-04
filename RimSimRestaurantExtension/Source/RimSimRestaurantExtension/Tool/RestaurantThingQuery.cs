using System.Collections.Generic;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //复用仍在地图上的物品引用，只在未知编号和地图物品变化同时出现时重建索引。
    internal static class RestaurantThingQuery
    {
        private static readonly Dictionary<Map, Dictionary<int, Thing>> indices = new Dictionary<Map, Dictionary<int, Thing>>();
        private static readonly Dictionary<Map, int> revisions = new Dictionary<Map, int>();

        //清空地图缓存，职责是释放上一个游戏持有的地图对象。
        public static void Reset()
        {
            indices.Clear();
            revisions.Clear();
        }

        //取得编号对应的在图对象，职责是过滤已销毁或离图的缓存引用。
        public static Thing Find(Map map, int id)
        {
            if (map == null || id < 0) return null;
            if (indices.TryGetValue(map, out var index) && index.TryGetValue(id, out Thing cached))
                return cached.Spawned && !cached.Destroyed && cached.Map == map ? cached : null;

            int revision = map.listerThings.StateHashOfGroup(ThingRequestGroup.Everything);
            if (revisions.TryGetValue(map, out int previous) && previous == revision) return null;
            if (index == null)
            {
                index = new Dictionary<int, Thing>();
                indices[map] = index;
            }
            else index.Clear();
            foreach (Thing thing in map.listerThings.AllThings)
                index[thing.thingIDNumber] = thing;
            revisions[map] = revision;
            return index.TryGetValue(id, out Thing found) ? found : null;
        }
    }
}
