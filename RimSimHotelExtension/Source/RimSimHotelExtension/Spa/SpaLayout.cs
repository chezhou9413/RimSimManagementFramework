using System.Collections.Generic;
using RimSimHotelExtension.Settings;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.Spa
{
    //让池位、相向坐姿和施工预览共用原版旋转中心与占地计算。
    public static class SpaLayout
    {
        //预览和实际接待使用同一份中央容量设置。
        public static int CapacityFor(ThingDef def)
        {
            return def.GetModExtension<SpaProperties>().stone
                ? HotelTuningDef.Current.stoneSpaCapacity : HotelTuningDef.Current.woodSpaCapacity;
        }

        //北向木池两列对称，石池的左岸有座凳，因此三列向右让开岸边。
        private static IntVec3 NorthSlot(ThingDef def, int slot)
        {
            bool stone = def.GetModExtension<SpaProperties>().stone;
            int columns = stone ? 3 : 2;
            int column = slot % columns;
            int row = slot / columns;
            CellRect rect = GenAdj.OccupiedRect(IntVec3.Zero, Rot4.North, def.size);
            int x = stone ? rect.minX + 2 + column : column == 0 ? rect.minX + 1 : rect.maxX - 1;
            return new IntVec3(x, 0, rect.minZ + 1 + row);
        }

        //当前素材没有西向图，Graphic_Multi 会水平镜像东向，池位必须跟随同一镜像。
        public static IntVec3 SlotCell(ThingDef def, IntVec3 root, Rot4 rot, int slot)
        {
            IntVec3 north = NorthSlot(def, slot);
            if (rot != Rot4.West) return root + north.RotatedBy(rot);
            Vector3 eastCenter = GenThing.TrueCenter(IntVec3.Zero, Rot4.East, def.size, 0f);
            Vector3 westCenter = GenThing.TrueCenter(root, Rot4.West, def.size, 0f);
            Vector3 relative = north.RotatedBy(Rot4.East).ToVector3Shifted() - eastCenter;
            relative.x = -relative.x;
            return (westCenter + relative).ToIntVec3();
        }

        //面对同一列另一排的池位，避免用建筑根格造成所有顾客朝向同一点。
        public static IntVec3 FacingCell(ThingDef def, IntVec3 root, Rot4 rot, int slot)
        {
            int columns = def.GetModExtension<SpaProperties>().stone ? 3 : 2;
            int oppositeSlot = slot < columns ? slot + columns : slot - columns;
            return SlotCell(def, root, rot, oppositeSlot);
        }

        //列出实际固定站位，施工前与建成后显示同样的四格或六格。
        public static List<IntVec3> SlotCells(ThingDef def, IntVec3 root, Rot4 rot)
        {
            int count = CapacityFor(def);
            var cells = new List<IntVec3>(count);
            for (int i = 0; i < count; i++) cells.Add(SlotCell(def, root, rot, i));
            return cells;
        }

        //占地显示外轮廓，每个池位独立描边，不把相邻池位合并成一块区域。
        public static void DrawLayout(ThingDef def, IntVec3 root, Rot4 rot, Color outline)
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            CellRect rect = GenAdj.OccupiedRect(root, rot, def.size);
            rect.ClipInsideMap(map);
            GenDraw.DrawFieldEdges(new List<IntVec3>(rect), outline);
            var cellList = new List<IntVec3>(1);
            foreach (IntVec3 cell in SlotCells(def, root, rot))
            {
                if (!cell.InBounds(map)) continue;
                cellList.Clear();
                cellList.Add(cell);
                GenDraw.DrawFieldEdges(cellList, new Color(0.25f, 0.85f, 1f));
            }
        }
    }
}
