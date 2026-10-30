using RimSimHotelExtension.Tool;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.Spa
{
    //防止可行走温泉覆盖其他设施，并在施工时显示实际池位。
    public sealed class PlaceWorker_HotelSpa : PlaceWorker
    {
        //地板、散落物和可埋设电线交由原版处理，地面建筑及其施工预约必须先移走。
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot,
            Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (!(checkingDef is ThingDef def)) return AcceptanceReport.WasAccepted;
            foreach (IntVec3 cell in GenAdj.OccupiedRect(loc, rot, def.size))
            {
                if (!cell.InBounds(map)) return new AcceptanceReport("OutOfBounds".Translate());
                foreach (Thing existing in map.thingGrid.ThingsListAt(cell))
                {
                    if (existing == thingToIgnore || existing == thing) continue;
                    ThingDef built = existing.def.entityDefToBuild as ThingDef ?? existing.def;
                    if (built.category != ThingCategory.Building) continue;
                    if (built.building?.isPowerConduit == true) continue;
                    return new AcceptanceReport(HotelText.T("RSH.Text.TheSpaCannotOverlapABuildingOrIts") + existing.LabelCap);
                }
            }
            return AcceptanceReport.WasAccepted;
        }

        //使用与 Job 相同的布局，无需提前划店区域或建造衣柜。
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            if (def != null) SpaLayout.DrawLayout(def, center, rot, ghostCol);
        }
    }
}
