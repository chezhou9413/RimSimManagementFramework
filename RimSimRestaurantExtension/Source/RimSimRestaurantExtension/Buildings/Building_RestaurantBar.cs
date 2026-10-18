using Verse;
using RimWorld;
namespace RimSimRestaurantExtension.Buildings
{
    //维护自定义吧台连接外观，职责是在摆放和移除时刷新相邻格图集。
    public sealed class Building_RestaurantBar : Building
    {
        //登记吧台后刷新邻接网格，职责是让已存在吧台立即显示新连接。
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things, regenAdjacentCells: true, regenAdjacentSections: false);
        }

        //移除吧台后刷新邻接网格，职责是清除其他吧台指向空格的连接。
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            IntVec3 cell = Position;
            base.DeSpawn(mode);
            map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Things, regenAdjacentCells: true, regenAdjacentSections: false);
        }
    }
}
