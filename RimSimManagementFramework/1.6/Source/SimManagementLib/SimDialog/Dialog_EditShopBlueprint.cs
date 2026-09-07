using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责编辑本地店铺蓝图的基础信息，并允许删除蓝图中的建筑、地板和商店区格子。
    /// </summary>
    public sealed partial class Dialog_EditShopBlueprint : Window
    {
        private const float FooterHeight = 42f;
        private const float HeaderHeight = 176f;
        private const float CanvasHeight = 360f;
        private const float RowHeight = 38f;

        private readonly ShopBlueprintLocalRecord record;
        private readonly Action onSaved;
        private Vector2 scrollPos;
        private string labelBuffer;
        private string descriptionBuffer;
        private readonly Dictionary<string, ThingDef> thingDefCache = new Dictionary<string, ThingDef>();
        private readonly Dictionary<string, ThingStyleDef> thingStyleDefCache = new Dictionary<string, ThingStyleDef>();
        private ShopBlueprintBuildingData selectedBuilding;
        private ShopBlueprintBuildingData draggingBuilding;
        private int dragStartMouseX;
        private int dragStartMouseZ;
        private int dragStartBuildingX;
        private int dragStartBuildingZ;

        /// <summary>
        /// 初始化蓝图编辑窗口，并创建可编辑文本缓冲。
        /// </summary>
        public Dialog_EditShopBlueprint(ShopBlueprintLocalRecord record, Action onSaved)
        {
            this.record = record;
            this.onSaved = onSaved;
            labelBuffer = record?.Data?.label ?? "";
            descriptionBuffer = record?.Data?.description ?? "";
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
        }

        public override Vector2 InitialSize => new Vector2(900f, 680f);

        /// <summary>
        /// 绘制蓝图编辑窗口内容。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                if (record?.Data == null)
                {
                    Widgets.Label(inRect, SimTranslation.T("RSMF.Blueprint.Error.RecordMissing"));
                    return;
                }

                Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
                DrawTitle(titleRect);

                Rect footerRect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);
                Rect bodyRect = new Rect(inRect.x, titleRect.yMax + 8f, inRect.width, footerRect.y - titleRect.yMax - 14f);
                DrawBody(bodyRect);
                DrawFooter(footerRect);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        /// <summary>
        /// 绘制窗口标题。
        /// </summary>
        private void DrawTitle(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(rect, SimTranslation.T("RSMF.Blueprint.Edit.Title"));
            ResetText();
        }

        /// <summary>
        /// 绘制可滚动的蓝图编辑主体。
        /// </summary>
        private void DrawBody(Rect rect)
        {
            float viewWidth = rect.width - 18f;
            float viewHeight = HeaderHeight
                + CanvasHeight
                + GetSectionHeight(BlueprintEditSection.Buildings)
                + GetSectionHeight(BlueprintEditSection.Terrains)
                + GetSectionHeight(BlueprintEditSection.ZoneCells)
                + 54f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, viewHeight));

            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
            float y = 0f;
            DrawBasicFields(new Rect(0f, y, viewWidth, HeaderHeight - 10f));
            y += HeaderHeight;

            DrawBlueprintCanvas(new Rect(0f, y, viewWidth, CanvasHeight - 12f));
            y += CanvasHeight;

            DrawBuildingSection(ref y, viewWidth);
            DrawTerrainSection(ref y, viewWidth);
            DrawZoneCellSection(ref y, viewWidth);
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制蓝图名称和介绍编辑区。
        /// </summary>
        private void DrawBasicFields(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            float labelH = Text.LineHeightOf(GameFont.Tiny) + 4f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, labelH), SimTranslation.T("RSMF.Blueprint.Edit.Name"));
            ResetText();

            labelBuffer = Widgets.TextField(new Rect(rect.x + 10f, rect.y + 8f + labelH, rect.width - 20f, 30f), labelBuffer ?? "");

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 50f + labelH, rect.width - 20f, labelH), SimTranslation.T("RSMF.Blueprint.Edit.Description"));
            ResetText();

            Rect textArea = new Rect(rect.x + 10f, rect.y + 50f + labelH * 2f, rect.width - 20f, 72f);
            Widgets.DrawBoxSolid(textArea, new Color(0f, 0f, 0f, 0.22f));
            descriptionBuffer = Widgets.TextArea(textArea.ContractedBy(4f), descriptionBuffer ?? "");
        }

        /// <summary>
        /// 绘制保存和取消按钮。
        /// </summary>
        private void DrawFooter(Rect rect)
        {
            Rect cancelRect = new Rect(rect.xMax - 224f, rect.y + 6f, 104f, 32f);
            Rect saveRect = new Rect(rect.xMax - 112f, rect.y + 6f, 112f, 32f);

            if (SimUiStyle.DrawSecondaryButton(cancelRect, SimTranslation.T("RSMF.Common.Cancel"), true, GameFont.Tiny))
                Close();
            if (SimUiStyle.DrawPrimaryButton(saveRect, SimTranslation.T("RSMF.Blueprint.Edit.Save"), true, GameFont.Tiny))
                SaveAndClose();
        }

        /// <summary>
        /// 保存编辑后的蓝图并关闭窗口。
        /// </summary>
        private void SaveAndClose()
        {
            ShopBlueprintData data = record.Data;
            data.label = string.IsNullOrWhiteSpace(labelBuffer)
                ? SimTranslation.T("RSMF.Common.UnnamedShop")
                : labelBuffer.Trim();
            data.description = descriptionBuffer ?? "";
            data.buildings = data.buildings ?? new List<ShopBlueprintBuildingData>();
            data.terrains = data.terrains ?? new List<ShopBlueprintTerrainData>();
            data.zoneCells = data.zoneCells ?? new List<ShopBlueprintCellData>();

            if (!ShopBlueprintLibrary.TryUpdateRecord(record, data, out string error))
            {
                Messages.Message(error ?? SimTranslation.T("RSMF.Blueprint.Error.SaveFailedUnknown"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message(SimTranslation.T("RSMF.Blueprint.Edit.SaveSuccess"), MessageTypeDefOf.PositiveEvent, false);
            onSaved?.Invoke();
            Close();
        }

        /// <summary>
        /// 根据分组类型计算滚动内容高度。
        /// </summary>
        private float GetSectionHeight(BlueprintEditSection section)
        {
            int count;
            switch (section)
            {
                case BlueprintEditSection.Buildings:
                    count = record?.Data?.buildings?.Count ?? 0;
                    break;
                case BlueprintEditSection.Terrains:
                    count = record?.Data?.terrains?.Count ?? 0;
                    break;
                default:
                    count = record?.Data?.zoneCells?.Count ?? 0;
                    break;
            }

            return 34f + Math.Max(1, count) * (RowHeight + 4f) + 8f;
        }

        /// <summary>
        /// 恢复 IMGUI 文本状态。
        /// </summary>
        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 标识蓝图编辑窗口中的元素分组。
        /// </summary>
        private enum BlueprintEditSection
        {
            Buildings,
            Terrains,
            ZoneCells
        }
    }
}
