using RimWorld;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimZone
{
    /// <summary>
    /// 提供商店区域的划定工具，允许商店区覆盖建筑并保持与其他区划互斥。
    /// </summary>
    public class Designator_ZoneAdd_Shop : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "商店区域";

        /// <summary>
        /// 初始化商店区域划定工具的显示文本、图标和目标区划类型。
        /// </summary>
        public Designator_ZoneAdd_Shop()
        {
            this.zoneTypeToPlace = typeof(Zone_Shop);
            this.defaultLabel = "划定商店区";
            this.defaultDesc = "划定一个用于经营的商店区域。商店区域可以覆盖建筑，但必须在室内，且区域内至少放置货柜和收银台。";
            this.icon = ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Growing", true);
            this.hotKey = KeyBindingDefOf.Misc1;
        }

        /// <summary>
        /// 创建新的商店区域实例。
        /// </summary>
        protected override Zone MakeNewZone()
        {
            return new Zone_Shop(base.Map.zoneManager);
        }

        /// <summary>
        /// 判断指定格子是否可被划为商店区，商店区允许覆盖任意建筑但不覆盖其他类型区划。
        /// </summary>
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(base.Map))
                return false;
            if (c.Fogged(base.Map))
                return false;
            if (c.InNoZoneEdgeArea(base.Map))
                return "TooCloseToMapEdge".Translate();

            Zone zone = base.Map.zoneManager.ZoneAt(c);
            if (zone != null && !(zone is Zone_Shop))
                return false;

            return true;
        }
    }
}
