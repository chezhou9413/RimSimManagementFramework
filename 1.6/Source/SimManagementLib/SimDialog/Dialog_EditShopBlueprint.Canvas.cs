using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责绘制店铺蓝图的虚拟空间画布，并处理建筑在画布中的选择和拖动。
    /// </summary>
    public sealed partial class Dialog_EditShopBlueprint
    {
        /// <summary>
        /// 绘制蓝图虚拟空间、元素图例和当前选中建筑状态。
        /// </summary>
        private void DrawBlueprintCanvas(Rect rect)
        {
            ShopBlueprintData data = record.Data;
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            float titleHeight = Text.LineHeightOf(GameFont.Small) + 8f;
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, titleHeight);
            DrawCanvasTitle(titleRect);

            Rect gridOuterRect = new Rect(rect.x + 10f, titleRect.yMax + 6f, rect.width - 20f, rect.height - titleHeight - 70f);
            Rect statusRect = new Rect(rect.x + 10f, gridOuterRect.yMax + 8f, rect.width - 20f, Text.LineHeightOf(GameFont.Tiny) + 8f);

            Rect gridRect = GetCanvasGridRect(gridOuterRect, data, out float cellSize);
            DrawCanvasBackground(gridOuterRect, gridRect, data, cellSize);
            HandleCanvasInput(gridRect, cellSize, data);
            DrawCanvasContents(gridRect, cellSize, data);
            DrawCanvasStatus(statusRect);
        }

        /// <summary>
        /// 绘制虚拟空间标题和操作提示。
        /// </summary>
        private static void DrawCanvasTitle(Rect rect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(rect, SimTranslation.T("RSMF.Blueprint.Edit.Canvas"));
            TooltipHandler.TipRegion(rect, SimTranslation.T("RSMF.Blueprint.Edit.CanvasTip"));
            ResetText();
        }

        /// <summary>
        /// 绘制蓝图虚拟空间的背景、边界和网格线。
        /// </summary>
        private static void DrawCanvasBackground(Rect outerRect, Rect gridRect, ShopBlueprintData data, float cellSize)
        {
            Widgets.DrawBoxSolid(outerRect, new Color(0f, 0f, 0f, 0.20f));
            SimUiStyle.DrawBorder(outerRect, new Color(1f, 1f, 1f, 0.08f));
            Widgets.DrawBoxSolid(gridRect, new Color(0.08f, 0.08f, 0.08f, 1f));
            SimUiStyle.DrawBorder(gridRect, new Color(1f, 1f, 1f, 0.16f));

            Color lineColor = new Color(1f, 1f, 1f, cellSize < 8f ? 0.045f : 0.075f);
            for (int x = 1; x < Math.Max(1, data.width); x++)
            {
                float px = gridRect.x + x * cellSize;
                Widgets.DrawBoxSolid(new Rect(px, gridRect.y, 1f, gridRect.height), lineColor);
            }

            for (int z = 1; z < Math.Max(1, data.height); z++)
            {
                float py = gridRect.y + z * cellSize;
                Widgets.DrawBoxSolid(new Rect(gridRect.x, py, gridRect.width, 1f), lineColor);
            }
        }

        /// <summary>
        /// 绘制蓝图中的地板、商店区格子和建筑占位。
        /// </summary>
        private void DrawCanvasContents(Rect gridRect, float cellSize, ShopBlueprintData data)
        {
            List<ShopBlueprintTerrainData> terrains = data.terrains ?? new List<ShopBlueprintTerrainData>();
            for (int i = 0; i < terrains.Count; i++)
            {
                ShopBlueprintTerrainData terrain = terrains[i];
                DrawCanvasCell(gridRect, cellSize, terrain.x, terrain.z, new Color(0.24f, 0.22f, 0.18f, 0.94f));
            }

            List<ShopBlueprintCellData> cells = data.zoneCells ?? new List<ShopBlueprintCellData>();
            for (int i = 0; i < cells.Count; i++)
            {
                ShopBlueprintCellData cell = cells[i];
                DrawCanvasCell(gridRect, cellSize, cell.x, cell.z, new Color(0.26f, 0.56f, 0.80f, 0.48f));
            }

            List<ShopBlueprintBuildingData> buildings = data.buildings ?? new List<ShopBlueprintBuildingData>();
            for (int i = 0; i < buildings.Count; i++)
            {
                ShopBlueprintBuildingData building = buildings[i];
                Rect buildingRect = ToBuildingRect(gridRect, cellSize, building);
                bool selected = selectedBuilding == building;
                Widgets.DrawBoxSolid(buildingRect.ContractedBy(1f), GetBuildingCanvasColor(building, selected));
                DrawBuildingThingIcon(buildingRect.ContractedBy(2f), building);
                SimUiStyle.DrawBorder(buildingRect, selected ? new Color(1f, 1f, 1f, 0.95f) : new Color(0f, 0f, 0f, 0.45f), selected ? 2f : 1f);

                if (selected && cellSize >= 10f)
                    DrawBuildingLabel(buildingRect.ContractedBy(3f), building);
            }
        }

        /// <summary>
        /// 根据当前鼠标事件处理建筑选择、拖动和空白处取消选择。
        /// </summary>
        private void HandleCanvasInput(Rect gridRect, float cellSize, ShopBlueprintData data)
        {
            Event evt = Event.current;
            if (evt == null)
                return;

            if (evt.type == EventType.MouseDown && evt.button == 0 && gridRect.Contains(evt.mousePosition))
            {
                ShopBlueprintBuildingData hit = HitTestBuilding(gridRect, cellSize, data, evt.mousePosition);
                selectedBuilding = hit;
                draggingBuilding = hit;
                if (hit != null)
                {
                    GetMouseBlueprintCell(gridRect, cellSize, evt.mousePosition, data, out dragStartMouseX, out dragStartMouseZ);
                    dragStartBuildingX = hit.x;
                    dragStartBuildingZ = hit.z;
                }
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDrag && evt.button == 0 && draggingBuilding != null)
            {
                GetMouseBlueprintCell(gridRect, cellSize, evt.mousePosition, data, out int mouseX, out int mouseZ);
                int deltaX = mouseX - dragStartMouseX;
                int deltaZ = mouseZ - dragStartMouseZ;
                MoveBuildingWithinBounds(draggingBuilding, data, dragStartBuildingX + deltaX, dragStartBuildingZ + deltaZ);
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && draggingBuilding != null)
            {
                draggingBuilding = null;
                evt.Use();
            }
        }

        /// <summary>
        /// 绘制当前选中建筑的状态说明。
        /// </summary>
        private void DrawCanvasStatus(Rect rect)
        {
            string status = selectedBuilding == null
                ? SimTranslation.T("RSMF.Blueprint.Edit.NoSelection")
                : SimTranslation.T("RSMF.Blueprint.Edit.SelectedBuilding",
                    (selectedBuilding.label ?? selectedBuilding.defName ?? "").Named("label"),
                    selectedBuilding.x.Named("x"),
                    selectedBuilding.z.Named("z"));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = selectedBuilding == null ? new Color(0.72f, 0.72f, 0.72f, 1f) : Color.white;
            Widgets.Label(rect, status);
            ResetText();
        }

        /// <summary>
        /// 根据可用区域和蓝图尺寸计算等比例缩放后的网格矩形。
        /// </summary>
        private static Rect GetCanvasGridRect(Rect outerRect, ShopBlueprintData data, out float cellSize)
        {
            int width = Math.Max(1, data.width);
            int height = Math.Max(1, data.height);
            cellSize = Mathf.Max(3f, Mathf.Min(outerRect.width / width, outerRect.height / height));
            float gridWidth = width * cellSize;
            float gridHeight = height * cellSize;
            return new Rect(
                outerRect.x + (outerRect.width - gridWidth) * 0.5f,
                outerRect.y + (outerRect.height - gridHeight) * 0.5f,
                gridWidth,
                gridHeight);
        }

        /// <summary>
        /// 将建筑蓝图坐标转换为虚拟画布中的矩形。
        /// </summary>
        private static Rect ToBuildingRect(Rect gridRect, float cellSize, ShopBlueprintBuildingData building)
        {
            int width = Math.Max(1, building.width);
            int height = Math.Max(1, building.height);
            return new Rect(
                gridRect.x + building.x * cellSize,
                gridRect.y + building.z * cellSize,
                width * cellSize,
                height * cellSize);
        }

        /// <summary>
        /// 绘制一个蓝图格子的色块。
        /// </summary>
        private static void DrawCanvasCell(Rect gridRect, float cellSize, int x, int z, Color color)
        {
            Rect cellRect = new Rect(gridRect.x + x * cellSize, gridRect.y + z * cellSize, cellSize, cellSize);
            Widgets.DrawBoxSolid(cellRect.ContractedBy(1f), color);
        }

        /// <summary>
        /// 读取鼠标所在蓝图格子，并允许拖动到画布外侧时贴边。
        /// </summary>
        private static void GetMouseBlueprintCell(Rect gridRect, float cellSize, Vector2 mouse, ShopBlueprintData data, out int x, out int z)
        {
            int maxX = Math.Max(0, data.width - 1);
            int maxZ = Math.Max(0, data.height - 1);
            x = Mathf.Clamp(Mathf.FloorToInt((mouse.x - gridRect.x) / cellSize), 0, maxX);
            z = Mathf.Clamp(Mathf.FloorToInt((mouse.y - gridRect.y) / cellSize), 0, maxZ);
        }

        /// <summary>
        /// 从上层到下层命中测试建筑，使重叠建筑优先选中后绘制的元素。
        /// </summary>
        private static ShopBlueprintBuildingData HitTestBuilding(Rect gridRect, float cellSize, ShopBlueprintData data, Vector2 mouse)
        {
            List<ShopBlueprintBuildingData> buildings = data.buildings ?? new List<ShopBlueprintBuildingData>();
            for (int i = buildings.Count - 1; i >= 0; i--)
            {
                ShopBlueprintBuildingData building = buildings[i];
                if (ToBuildingRect(gridRect, cellSize, building).Contains(mouse))
                    return building;
            }
            return null;
        }

        /// <summary>
        /// 移动建筑并保证建筑占地不会超出蓝图范围。
        /// </summary>
        private static void MoveBuildingWithinBounds(ShopBlueprintBuildingData building, ShopBlueprintData data, int targetX, int targetZ)
        {
            int width = Math.Max(1, building.width);
            int height = Math.Max(1, building.height);
            int maxX = Math.Max(0, Math.Max(1, data.width) - width);
            int maxZ = Math.Max(0, Math.Max(1, data.height) - height);
            building.x = Mathf.Clamp(targetX, 0, maxX);
            building.z = Mathf.Clamp(targetZ, 0, maxZ);
        }

        /// <summary>
        /// 根据经营组件类型返回建筑在虚拟画布中的显示颜色。
        /// </summary>
        private static Color GetBuildingCanvasColor(ShopBlueprintBuildingData building, bool selected)
        {
            Color color;
            if (building.goods != null) color = new Color(0.35f, 0.78f, 0.42f, 0.92f);
            else if (building.cash != null) color = new Color(0.95f, 0.76f, 0.28f, 0.92f);
            else if (building.sign != null) color = new Color(0.86f, 0.42f, 0.80f, 0.92f);
            else if (building.service != null) color = new Color(0.38f, 0.72f, 0.92f, 0.92f);
            else color = new Color(0.72f, 0.72f, 0.72f, 0.88f);
            return selected ? Color.Lerp(color, Color.white, 0.18f) : color;
        }

        /// <summary>
        /// 在足够大的建筑色块内绘制建筑名称。
        /// </summary>
        private static void DrawBuildingLabel(Rect rect, ShopBlueprintBuildingData building)
        {
            string label = building.label ?? building.defName ?? "";
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            GUI.color = Color.white;
            Widgets.Label(rect, label);
            ResetText();
        }
    }
}
