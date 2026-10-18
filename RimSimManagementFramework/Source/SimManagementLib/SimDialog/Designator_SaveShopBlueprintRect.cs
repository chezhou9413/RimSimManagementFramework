using RimWorld;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责让玩家在地图上框选任意矩形，并把框选范围保存为本地店铺蓝图。
    /// </summary>
    public sealed class Designator_SaveShopBlueprintRect : Designator_Cells
    {
        private readonly int maxSize;
        private readonly System.Action onSaved;
        private bool finished;

        /// <summary>
        /// 初始化框选保存工具的显示文本、图标和尺寸限制。
        /// </summary>
        public Designator_SaveShopBlueprintRect(int maxSize, System.Action onSaved)
        {
            this.maxSize = Mathf.Clamp(maxSize, 5, 200);
            this.onSaved = onSaved;
            defaultLabel = SimTranslation.T("RSMF.Blueprint.Designator.Label");
            defaultDesc = SimTranslation.T("RSMF.Blueprint.Designator.Desc", this.maxSize.Named("max"));
            icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true);
            useMouseIcon = true;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_ZoneAdd;
        }

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public override bool DragDrawMeasurements => true;

        /// <summary>
        /// 判断指定格子是否可被框选保存。
        /// </summary>
        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            return loc.InBounds(Map);
        }

        /// <summary>
        /// 将玩家拖选的格子集合转换为矩形范围并保存蓝图。
        /// </summary>
        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            List<IntVec3> selected = cells?.Where(c => c.InBounds(Map)).ToList() ?? new List<IntVec3>();
            if (selected.Count <= 0)
            {
                Messages.Message(SimTranslation.T("RSMF.Blueprint.Error.EmptyRect"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            CellRect bounds = CellRect.FromLimits(
                selected.Min(c => c.x),
                selected.Min(c => c.z),
                selected.Max(c => c.x),
                selected.Max(c => c.z));

            if (ShopBlueprintLibrary.TrySaveFromRect(Map, bounds, maxSize, out _, out string error))
            {
                Messages.Message(SimTranslation.T("RSMF.Blueprint.SaveSuccess"), MessageTypeDefOf.PositiveEvent, false);
                FinishSelection();
                Find.DesignatorManager.Deselect();
                return;
            }

            Messages.Message(error ?? SimTranslation.T("RSMF.Blueprint.Error.SaveFailedUnknown"), MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>
        /// 绘制鼠标旁的框选提示。
        /// </summary>
        public override void DrawMouseAttachments()
        {
            GenUI.DrawMouseAttachment(icon, SimTranslation.T("RSMF.Blueprint.Designator.MouseAttachment"));
        }

        /// <summary>
        /// 在玩家取消框选工具时通知管理窗口恢复显示。
        /// </summary>
        public override void Deselected()
        {
            base.Deselected();
            FinishSelection();
        }

        /// <summary>
        /// 结束框选流程并确保恢复回调只执行一次。
        /// </summary>
        private void FinishSelection()
        {
            if (finished)
                return;

            finished = true;
            onSaved?.Invoke();
        }
    }
}
