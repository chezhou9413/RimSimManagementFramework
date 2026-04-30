using RimWorld;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimZone
{
    public class Designator_ZoneAdd_Shop : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "商店区域";

        public Designator_ZoneAdd_Shop()
        {
            this.zoneTypeToPlace = typeof(Zone_Shop);
            this.defaultLabel = "划定商店区";
            this.defaultDesc = "划定一个用于经营的商店区域。商店区域必须在室内，且区域内至少放置货柜和收银台。";
            this.icon = ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Growing", true);
            this.hotKey = KeyBindingDefOf.Misc1;
        }

        protected override Zone MakeNewZone()
        {
            return new Zone_Shop(base.Map.zoneManager);
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            return base.CanDesignateCell(c);
        }
    }
}
