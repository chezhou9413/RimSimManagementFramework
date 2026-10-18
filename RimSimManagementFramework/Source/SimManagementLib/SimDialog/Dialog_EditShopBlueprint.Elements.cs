using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责绘制蓝图元素列表，并提供列表中的选择和删除操作。
    /// </summary>
    public sealed partial class Dialog_EditShopBlueprint
    {
        /// <summary>
        /// 绘制建筑元素删除区。
        /// </summary>
        private void DrawBuildingSection(ref float y, float width)
        {
            List<ShopBlueprintBuildingData> buildings = record.Data.buildings ?? new List<ShopBlueprintBuildingData>();
            DrawSectionHeader(new Rect(0f, y, width, 30f),
                SimTranslation.T("RSMF.Blueprint.Edit.Buildings", buildings.Count.Named("count")),
                SimTranslation.T("RSMF.Blueprint.Edit.BuildingsTip"));
            y += 34f;

            if (buildings.Count <= 0)
            {
                DrawEmptyRow(new Rect(0f, y, width, RowHeight));
                y += RowHeight + 8f;
                return;
            }

            for (int i = buildings.Count - 1; i >= 0; i--)
            {
                ShopBlueprintBuildingData building = buildings[i];
                Rect row = new Rect(0f, y, width, RowHeight);
                string label = SimTranslation.T("RSMF.Blueprint.Edit.BuildingRow",
                    (building.label ?? building.defName ?? "").Named("label"),
                    (building.defName ?? "").Named("def"),
                    building.x.Named("x"),
                    building.z.Named("z"));
                if (DrawElementRow(row, label, SimTranslation.T("RSMF.Blueprint.Edit.DeleteElement"), selectedBuilding == building))
                {
                    if (selectedBuilding == building)
                        selectedBuilding = null;
                    if (draggingBuilding == building)
                        draggingBuilding = null;
                    buildings.RemoveAt(i);
                }
                else if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - 86f, row.height), false))
                {
                    selectedBuilding = building;
                }
                y += RowHeight + 4f;
            }

            y += 8f;
        }

        /// <summary>
        /// 绘制地板元素删除区。
        /// </summary>
        private void DrawTerrainSection(ref float y, float width)
        {
            List<ShopBlueprintTerrainData> terrains = record.Data.terrains ?? new List<ShopBlueprintTerrainData>();
            DrawSectionHeader(new Rect(0f, y, width, 30f),
                SimTranslation.T("RSMF.Blueprint.Edit.Terrains", terrains.Count.Named("count")),
                SimTranslation.T("RSMF.Blueprint.Edit.TerrainsTip"));
            y += 34f;

            if (terrains.Count <= 0)
            {
                DrawEmptyRow(new Rect(0f, y, width, RowHeight));
                y += RowHeight + 8f;
                return;
            }

            for (int i = terrains.Count - 1; i >= 0; i--)
            {
                ShopBlueprintTerrainData terrain = terrains[i];
                Rect row = new Rect(0f, y, width, RowHeight);
                string label = SimTranslation.T("RSMF.Blueprint.Edit.TerrainRow",
                    (terrain.terrainDefName ?? "").Named("def"),
                    terrain.x.Named("x"),
                    terrain.z.Named("z"));
                if (DrawElementRow(row, label, SimTranslation.T("RSMF.Blueprint.Edit.DeleteElement")))
                    terrains.RemoveAt(i);
                y += RowHeight + 4f;
            }

            y += 8f;
        }

        /// <summary>
        /// 绘制商店区格子删除区。
        /// </summary>
        private void DrawZoneCellSection(ref float y, float width)
        {
            List<ShopBlueprintCellData> cells = record.Data.zoneCells ?? new List<ShopBlueprintCellData>();
            DrawSectionHeader(new Rect(0f, y, width, 30f),
                SimTranslation.T("RSMF.Blueprint.Edit.ZoneCells", cells.Count.Named("count")),
                SimTranslation.T("RSMF.Blueprint.Edit.ZoneCellsTip"));
            y += 34f;

            if (cells.Count <= 0)
            {
                DrawEmptyRow(new Rect(0f, y, width, RowHeight));
                y += RowHeight + 8f;
                return;
            }

            for (int i = cells.Count - 1; i >= 0; i--)
            {
                ShopBlueprintCellData cell = cells[i];
                Rect row = new Rect(0f, y, width, RowHeight);
                string label = SimTranslation.T("RSMF.Blueprint.Edit.ZoneCellRow",
                    cell.x.Named("x"),
                    cell.z.Named("z"));
                if (DrawElementRow(row, label, SimTranslation.T("RSMF.Blueprint.Edit.DeleteElement")))
                    cells.RemoveAt(i);
                y += RowHeight + 4f;
            }

            y += 8f;
        }

        /// <summary>
        /// 绘制元素分组标题和说明。
        /// </summary>
        private static void DrawSectionHeader(Rect rect, string title, string tip)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, rect.height), title);
            TooltipHandler.TipRegion(rect, tip);
            ResetText();
        }

        /// <summary>
        /// 绘制单个可删除元素行。
        /// </summary>
        private static bool DrawElementRow(Rect rect, string label, string deleteLabel, bool selected = false)
        {
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.25f, 0.65f, 0.85f, 0.14f) : new Color(1f, 1f, 1f, 0.035f));
            SimUiStyle.DrawBorder(rect, selected ? new Color(0.25f, 0.65f, 0.85f, 0.75f) : new Color(1f, 1f, 1f, 0.10f));

            Rect deleteRect = new Rect(rect.xMax - 78f, rect.y + 5f, 70f, 28f);
            Rect labelRect = new Rect(rect.x + 8f, rect.y + 4f, deleteRect.x - rect.x - 16f, rect.height - 8f);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(labelRect, label);
            ResetText();

            return SimUiStyle.DrawDangerButton(deleteRect, deleteLabel, true, GameFont.Tiny);
        }

        /// <summary>
        /// 绘制空列表提示行。
        /// </summary>
        private static void DrawEmptyRow(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(rect, SimTranslation.T("RSMF.Blueprint.Edit.EmptySection"));
            ResetText();
        }
    }
}
