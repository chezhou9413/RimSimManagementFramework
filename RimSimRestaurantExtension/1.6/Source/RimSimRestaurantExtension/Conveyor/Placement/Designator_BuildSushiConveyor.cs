using System.Collections.Generic;
using RimSimRestaurantExtension.Conveyor.Rendering;
using RimSimRestaurantExtension.Conveyor.Transport;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Placement
{
    //提供单层传送带拖拽工具，职责是统一方向预览、蓝图铺设与旧节点改向。
    public sealed class Designator_BuildSushiConveyor : Designator_Build
    {
        private IntVec3 origin = IntVec3.Invalid;
        private bool axisLocked;
        private readonly List<IntVec3> previewCells = new List<IntVec3>();
        private readonly Dictionary<IntVec3, Rot4> previewPlan = new Dictionary<IntVec3, Rot4>();
        private readonly HashSet<IntVec3> previewAffected = new HashSet<IntVec3>();
        public override DrawStyleCategoryDef DrawStyleCategory => DefDatabase<DrawStyleCategoryDef>.GetNamed("RSR_SushiPathCategory");

        //绑定寿司建筑定义，职责是复用原版建造消耗和施工流程。
        public Designator_BuildSushiConveyor() : base(DefDatabase<ThingDef>.GetNamed("RSR_SushiConveyor")) { }

        //记录拖拽起点并支持旋转快捷键切换拐角。
        public override void SelectedProcessInput(Event ev)
        {
            if (ev.type == EventType.MouseDown && ev.button == 0)
            { origin = Verse.UI.MouseCell(); axisLocked = false; }
            if (Find.DesignatorManager.Dragger.Dragging
                && (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent || KeyBindingDefOf.Designator_RotateRight.KeyDownEvent))
            { DrawStyle_SushiPath.horizontalFirst = !DrawStyle_SushiPath.horizontalFirst; axisLocked = true; ev.Use(); return; }
            base.SelectedProcessInput(ev);
        }

        //锁定首次移动轴向，并绘制与提交相同的路径图集。
        public override void SelectedUpdate()
        {
            var dragger = Find.DesignatorManager.Dragger;
            if (dragger.Dragging && !axisLocked && origin.IsValid)
            {
                var delta = Verse.UI.MouseCell() - origin;
                if (delta.x != 0 || delta.z != 0)
                { DrawStyle_SushiPath.horizontalFirst = Mathf.Abs(delta.x) >= Mathf.Abs(delta.z); axisLocked = true; }
            }
            base.SelectedUpdate();
            if (!dragger.Dragging) return;
            CollectPrefix(dragger.CellBuffer, previewCells);
            ConveyorPlacementPlanner.FillPlan(Map, previewCells, placingRot, previewPlan);
            bool valid = ConveyorLinks.Valid(Map, previewPlan, previewAffected);
            foreach (var cell in previewPlan.Keys)
                ConveyorRenderer.Preview(Map, cell, previewPlan, valid ? new Color(0.4f, 1f, 0.5f, 0.65f) : new Color(1f, 0.2f, 0.2f, 0.65f));
        }

        //允许穿过已有传送带，职责是让连续拖拽可以改向而不重复消耗材料。
        public override AcceptanceReport CanDesignateCell(IntVec3 cell) =>
            ConveyorLinks.At(Map, cell) != null ? AcceptanceReport.WasAccepted : base.CanDesignateCell(cell);

        //按有序连续前缀提交方向，职责是拒绝产生合流的整次操作。
        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            var path = Prefix(cells);
            var plan = ConveyorPlacementPlanner.Plan(Map, path, placingRot);
            if (!ConveyorLinks.Valid(Map, plan))
            { Messages.Message("普通传送带不支持多个上游入口。", MessageTypeDefOf.RejectInput, false); return; }
            var old = placingRot;
            foreach (var pair in plan)
            {
                var existing = ConveyorLinks.At(Map, pair.Key);
                if (existing != null)
                {
                    existing.Rotation = pair.Value;
                    Map.mapDrawer.MapMeshDirty(existing.Position, MapMeshFlagDefOf.Things);
                }
                else { placingRot = pair.Value; base.DesignateSingleCell(pair.Key); }
            }
            placingRot = old;
            Map.GetComponent<MapComponent_SushiConveyor>().Dirty();
            origin = IntVec3.Invalid;
        }

        //单格铺设同样经过端点与多入口校验。
        public override void DesignateSingleCell(IntVec3 cell) => DesignateMultiCell(new[] { cell });

        //隐藏默认单格图集预览，职责是只显示当前方向的有效图块。
        protected override void DrawGhost(Color color)
        {
            if (Find.DesignatorManager.Dragger.Dragging) return;
            var cell = Verse.UI.MouseCell();
            previewCells.Clear();
            previewCells.Add(cell);
            ConveyorPlacementPlanner.FillPlan(Map, previewCells, placingRot, previewPlan);
            ConveyorRenderer.Preview(Map, cell, previewPlan, color);
        }

        //释放拖拽状态，职责是防止下一次铺设继承旧起点。
        public override void Deselected() { origin = IntVec3.Invalid; axisLocked = false; base.Deselected(); }

        //收集可铺设连续前缀，职责是遇到阻挡就截断而不越过障碍。
        private List<IntVec3> Prefix(IEnumerable<IntVec3> cells)
        {
            var result = new List<IntVec3>();
            CollectPrefix(cells, result);
            return result;
        }

        //填充调用方路径缓冲，职责是实时检查施工阻挡并保留连续可铺设前缀。
        private void CollectPrefix(IEnumerable<IntVec3> cells, List<IntVec3> result)
        {
            result.Clear();
            foreach (var cell in cells)
            {
                if (!CanDesignateCell(cell).Accepted) break;
                if (result.Count > 0 && cell == result[result.Count - 1]) continue;
                if (result.Count > 0 && (cell - result[result.Count - 1]).LengthManhattan != 1) break;
                result.Add(cell);
            }
        }
    }
}
