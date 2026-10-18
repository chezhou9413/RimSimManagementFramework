using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.SimMapComp
{
    /// <summary>
    /// 在地图 GUI 层绘制商店系统的自定义 Job 读条，负责把 Job 上报的进度稳定显示到 Pawn 头顶。
    /// </summary>
    public class MapComponent_ShopProgressOverlay : MapComponent
    {
        /// <summary>
        /// 创建当前地图的商店读条覆盖层组件。
        /// </summary>
        public MapComponent_ShopProgressOverlay(Map map) : base(map)
        {
        }

        /// <summary>
        /// 每帧绘制当前地图上的商店读条。
        /// </summary>
        public override void MapComponentOnGUI()
        {
            ShopProgressBarUtility.DrawForMap(map);
        }
    }
}
