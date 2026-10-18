using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
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
        private readonly ShopBlueprintPlacementOptions placementOptions = new ShopBlueprintPlacementOptions();
        private Rot4 placingRot = Rot4.North;
        private bool finished;
        private bool waitingForPlayerChoice;
        private bool placementSucceededThisClick;

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
            return ShopBlueprintPlacementUtility.CanTargetCellForPlacement(Map, c, data, placingRot, placementOptions);
        }

        /// <summary>
        /// 在玩家点击地图格时创建整套店铺蓝图施工计划。
        /// </summary>
        public override void DesignateSingleCell(IntVec3 c)
        {
            placementSucceededThisClick = false;
            ShopBlueprintPlacementPrecheckResult precheck = ShopBlueprintPlacementPrecheckUtility.Check(data, placementOptions);
            if (precheck.HasReplaceableStuffs)
            {
                waitingForPlayerChoice = true;
                ShowMissingStuffMenu(c, precheck.MissingStuffs, 0);
                return;
            }

            if (precheck.HasMissingBuildings)
            {
                waitingForPlayerChoice = true;
                ShowPlacementIssueDialog(ShopBlueprintPlacementPrecheckUtility.BuildMissingBuildingsMessage(precheck), c);
                return;
            }

            if (precheck.HasUnreplaceableStuffs)
            {
                waitingForPlayerChoice = true;
                ShowPlacementIssueDialog(ShopBlueprintPlacementPrecheckUtility.BuildUnreplaceableStuffsMessage(precheck), c);
                return;
            }

            TryPlaceAfterPrecheck(c);
        }

        /// <summary>
        /// 在依赖检查通过后创建整套店铺蓝图施工计划。
        /// </summary>
        private void TryPlaceAfterPrecheck(IntVec3 c)
        {
            waitingForPlayerChoice = false;
            if (ShopBlueprintPlacementUtility.TryPlaceAt(Map, c, data, placingRot, placementOptions, out int plannedCount, out string error))
            {
                placementSucceededThisClick = true;
                Messages.Message(SimTranslation.T("RSMF.Blueprint.Place.Success", plannedCount.Named("count")), MessageTypeDefOf.PositiveEvent, false);
                FinishSelection();
                Find.DesignatorManager.Deselect();
                return;
            }

            waitingForPlayerChoice = true;
            ShowPlacementIssueDialog(error ?? SimTranslation.T("RSMF.Blueprint.Place.Error.Unknown"), c);
        }

        /// <summary>
        /// 强制创建当前可用的蓝图内容，并跳过缺失依赖或原版规则拒绝的单项。
        /// </summary>
        private void TryForcePlaceAvailable(IntVec3 c)
        {
            waitingForPlayerChoice = false;
            if (ShopBlueprintPlacementUtility.TryForcePlaceAvailableAt(Map, c, data, placingRot, placementOptions, out int plannedCount, out string error))
            {
                placementSucceededThisClick = true;
                Messages.Message(SimTranslation.T("RSMF.Blueprint.Place.ForceSuccess", plannedCount.Named("count")), MessageTypeDefOf.PositiveEvent, false);
                FinishSelection();
                Find.DesignatorManager.Deselect();
                return;
            }

            Messages.Message(error ?? SimTranslation.T("RSMF.Blueprint.Place.Error.Unknown"), MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>
        /// 弹出蓝图放置问题说明，并允许玩家只放置当前仍可放置的部分。
        /// </summary>
        private void ShowPlacementIssueDialog(string message, IntVec3 c)
        {
            Find.WindowStack.Add(new Dialog_MessageBox(
                message ?? SimTranslation.T("RSMF.Blueprint.Place.Error.Unknown"),
                SimTranslation.T("RSMF.Blueprint.Place.ForcePlaceAvailable"),
                delegate
                {
                    TryForcePlaceAvailable(c);
                },
                "GoBack".Translate(),
                delegate
                {
                    waitingForPlayerChoice = false;
                },
                null,
                false,
                null,
                delegate
                {
                    waitingForPlayerChoice = false;
                }));
        }

        /// <summary>
        /// 按顺序弹出缺失材料替换菜单。
        /// </summary>
        private void ShowMissingStuffMenu(IntVec3 c, List<ShopBlueprintMissingStuff> missingStuffs, int index)
        {
            if (missingStuffs == null || index >= missingStuffs.Count)
            {
                TryPlaceAfterPrecheck(c);
                return;
            }

            ShopBlueprintMissingStuff missing = missingStuffs[index];
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < missing.ReplacementOptions.Count; i++)
            {
                ThingDef replacementStuff = missing.ReplacementOptions[i];
                string label = SimTranslation.T(
                    "RSMF.Blueprint.Place.MissingStuff.Option",
                    missing.DisplayLabel.Named("missing"),
                    replacementStuff.LabelCap.Resolve().Named("replacement"));
                options.Add(new FloatMenuOption(label, delegate
                {
                    placementOptions.SetStuffReplacement(missing.BuildingDefName, missing.MissingStuffDefName, replacementStuff);
                    ShowMissingStuffMenu(c, missingStuffs, index + 1);
                }));
            }

            if (options.Count == 0)
            {
                ShowPlacementIssueDialog(SimTranslation.T("RSMF.Blueprint.Place.Error.MissingStuffNoReplacement", missing.DisplayLabel.Named("items")), c);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options, SimTranslation.T("RSMF.Blueprint.Place.MissingStuff.Title")));
        }

        /// <summary>
        /// 在真正创建蓝图后播放成功音效，依赖提示和材料菜单不播放成功音效。
        /// </summary>
        protected override void FinalizeDesignationSucceeded()
        {
            if (waitingForPlayerChoice || !placementSucceededThisClick)
                return;

            base.FinalizeDesignationSucceeded();
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

            ShopBlueprintPlacementUtility.DrawPlacementPreview(Map, cell, data, placingRot, CanPlaceWithoutPlayerChoice(cell));
        }

        /// <summary>
        /// 判断当前鼠标格是否已经满足严格放置条件，负责驱动蓝图预览颜色。
        /// </summary>
        private bool CanPlaceWithoutPlayerChoice(IntVec3 cell)
        {
            return ShopBlueprintPlacementUtility.CanPreviewAt(Map, cell, data, placingRot, placementOptions).Accepted;
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
