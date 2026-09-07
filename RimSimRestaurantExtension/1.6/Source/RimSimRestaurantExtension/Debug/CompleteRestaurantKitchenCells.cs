using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //规划样板餐厅后厨格位，职责是让食材投放与货源划区共用同一份建筑避让结果。
    internal static class CompleteRestaurantKitchenCells
    {
        //筛选可放置食材的后厨空地，职责是保留壁挂柜等非主体建筑及设施交互格的商店归属。
        public static List<IntVec3> Find(Map map, CellRect inner, IntVec3 center, HashSet<IntVec3> reserved)
        {
            return inner.Cells
                .Where(cell => cell.z >= center.z + 2 && !reserved.Contains(cell))
                .Where(cell => cell.Standable(map) && !cell.GetThingList(map).Any(thing => thing is Building))
                .OrderByDescending(cell => cell.z)
                .ThenBy(cell => cell.x)
                .ToList();
        }
    }
}
