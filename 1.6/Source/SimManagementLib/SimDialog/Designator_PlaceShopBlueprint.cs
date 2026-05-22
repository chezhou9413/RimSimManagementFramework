using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责让玩家在地图上选择店铺蓝图的左下角锚点，并放置整套施工计划和经营配置。
    /// </summary>
    public sealed class Designator_PlaceShopBlueprint : Designator
    {
        private readonly ShopBlueprintData data;
        private readonly System.Action onFinished;
        private Rot4 placingRot = Rot4.North;
        private bool finished;

        /// <summary>
        /// 初始化店铺蓝图放置工具的显示文本和回调。
        /// </summary>
        public Designator_PlaceShopBlueprint(ShopBlueprintData data, System.Action onFinished)
        {
            this.data = data;
            this.onFinished = onFinished;
            defaultLabel = SimTranslation.T("RSMF.Blueprint.Place.Designator.Label");
            defaultDesc = SimTranslation.T("RSMF.Blueprint.Place.Designator.Desc");
            icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", false) ?? BaseContent.BadTex;
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_PlaceBuilding;
        }

        /// <summary>
        /// 判断当前鼠标格是否能作为蓝图左下角放置锚点。
        /// </summary>
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            return ShopBlueprintPlacementUtility.CanPlaceAt(Map, c, data, placingRot);
        }

        /// <summary>
        /// 在玩家点击地图格时创建整套店铺蓝图施工计划。
        /// </summary>
        public override void DesignateSingleCell(IntVec3 c)
        {
            if (ShopBlueprintPlacementUtility.TryPlaceAt(Map, c, data, placingRot, out int plannedCount, out string error))
            {
                Messages.Message(SimTranslation.T("RSMF.Blueprint.Place.Success", plannedCount.Named("count")), MessageTypeDefOf.PositiveEvent, false);
                FinishSelection();
                Find.DesignatorManager.Deselect();
                return;
            }

            Messages.Message(error ?? SimTranslation.T("RSMF.Blueprint.Place.Error.Unknown"), MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>
        /// 绘制蓝图放置范围预览。
        /// </summary>
        public override void SelectedUpdate()
        {
            base.SelectedUpdate();
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(Map))
                return;

            ShopBlueprintPlacementUtility.DrawPlacementPreview(Map, cell, data, placingRot, CanDesignateCell(cell).Accepted);
        }

        /// <summary>
        /// 绘制蓝图整体旋转按钮。
        /// </summary>
        public override void DoExtraGuiControls(float leftX, float bottomY)
        {
            DesignatorUtility.GUIDoRotationControls(leftX, bottomY, placingRot, delegate (Rot4 rot)
            {
                placingRot = rot;
            });
        }

        /// <summary>
        /// 处理蓝图整体旋转快捷键。
        /// </summary>
        public override void SelectedProcessInput(Event ev)
        {
            base.SelectedProcessInput(ev);
            if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
                RotateBlueprint(RotationDirection.Clockwise);
            if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
                RotateBlueprint(RotationDirection.Counterclockwise);
        }

        /// <summary>
        /// 绘制鼠标旁的蓝图尺寸提示。
        /// </summary>
        public override void DrawMouseAttachments()
        {
            int width = placingRot.IsHorizontal ? data.height : data.width;
            int height = placingRot.IsHorizontal ? data.width : data.height;
            GenUI.DrawMouseAttachment(icon, SimTranslation.T("RSMF.Blueprint.Place.Designator.MouseAttachment",
                width.Named("width"),
                height.Named("height")));
        }

        /// <summary>
        /// 在玩家取消放置工具时通知管理窗口恢复显示。
        /// </summary>
        public override void Deselected()
        {
            base.Deselected();
            FinishSelection();
        }

        /// <summary>
        /// 结束放置流程并确保恢复回调只执行一次。
        /// </summary>
        private void FinishSelection()
        {
            if (finished)
                return;

            finished = true;
            onFinished?.Invoke();
        }

        /// <summary>
        /// 按指定方向旋转蓝图整体放置方向。
        /// </summary>
        private void RotateBlueprint(RotationDirection direction)
        {
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            placingRot.Rotate(direction);
        }
    }
}
