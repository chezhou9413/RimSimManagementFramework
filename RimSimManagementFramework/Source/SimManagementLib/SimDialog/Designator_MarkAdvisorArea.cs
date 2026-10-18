using System;
using System.Collections.Generic;
using RimWorld;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //用原版矩形拖选与尺寸预览标记聊天范围，选择过程不修改地图。
    internal sealed class Designator_MarkAdvisorArea : Designator_Cells
    {
        private readonly Map selectionMap;
        private readonly Action<CellRect?> onFinished;
        private CellRect? selected;
        private bool finished;

        //固定进入工具时的地图，防止拖选过程中切图后混用坐标。
        internal Designator_MarkAdvisorArea(Map map, Action<CellRect?> onFinished)
        {
            selectionMap = map;
            this.onFinished = onFinished;
            defaultLabel = SimTranslation.T("RSMF.Business.Advisor.MarkArea");
            defaultDesc = SimTranslation.T("RSMF.Business.Advisor.MarkAreaHint");
            icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy");
            useMouseIcon = true;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
        }

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;
        public override bool DragDrawMeasurements => true;

        //只允许当前地图内的格子，标记本身不要求地块可建造。
        public override AcceptanceReport CanDesignateCell(IntVec3 loc) => Map == selectionMap && loc.InBounds(selectionMap);

        //切换地图时结束本次选择，不把另一张地图的格子当作原标记。
        public override bool CanRemainSelected() => Map == selectionMap;

        //单击也可标记一格，便于精确指定货架或建筑位置。
        public override void DesignateSingleCell(IntVec3 cell)
        {
            if (!CanDesignateCell(cell).Accepted) return;
            selected = new CellRect(cell.x, cell.z, 1, 1);
            Find.DesignatorManager.Deselect();
        }

        //只计算选区边界，不收集建筑、库存或扫描区域之外的格子。
        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxZ = -1;
            foreach (IntVec3 cell in cells)
            {
                if (!CanDesignateCell(cell).Accepted) continue;
                minX = Math.Min(minX, cell.x); minZ = Math.Min(minZ, cell.z);
                maxX = Math.Max(maxX, cell.x); maxZ = Math.Max(maxZ, cell.z);
            }
            if (maxX < 0) return;
            selected = CellRect.FromLimits(minX, minZ, maxX, maxZ);
            Find.DesignatorManager.Deselect();
        }

        //鼠标旁始终提示完成和取消方式。
        public override void DrawMouseAttachments() => GenUI.DrawMouseAttachment(icon, SimTranslation.T("RSMF.Business.Advisor.MarkAreaMouseHint"));

        //右键、Esc 和选区完成共用返回入口，取消时不替换已有附件。
        public override void Deselected()
        {
            base.Deselected();
            if (finished) return;
            finished = true;
            onFinished(selected);
        }
    }
}
