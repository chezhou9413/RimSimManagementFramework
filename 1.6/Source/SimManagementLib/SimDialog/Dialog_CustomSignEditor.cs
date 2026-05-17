using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责提供玩家编辑招牌三面图片图层的游戏内窗口。
    /// </summary>
    public class Dialog_CustomSignEditor : Window
    {
        private const float HeaderHeight = 76f;
        private const float FaceTabsWidth = 148f;
        private const float LayerPanelWidth = 340f;
        private const float SectionGap = 14f;
        private const float ScrollbarWidth = 16f;
        private const float CloseXReservedWidth = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 18f;

        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color Accent = new Color(0.18f, 0.69f, 0.87f, 1f);
        private static readonly Color SoftAccent = new Color(0.18f, 0.69f, 0.87f, 0.12f);
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f, 1f);

        private readonly ThingComp_CustomSign sign;
        private SignFaceData southDraft;
        private SignFaceData eastDraft;
        private SignFaceData northDraft;
        private SignFaceKind selectedFace = SignFaceKind.South;
        private int selectedLayerIndex = -1;
        private Vector2 layerScroll;
        private readonly Dictionary<string, string> numericBuffers = new Dictionary<string, string>();
        private SignFaceKind numericBufferFace = SignFaceKind.South;
        private int numericBufferLayerIndex = -999;

        public override Vector2 InitialSize => new Vector2(1280f, 820f);

        /// <summary>
        /// 负责初始化招牌编辑器并创建建筑数据草稿。
        /// </summary>
        public Dialog_CustomSignEditor(ThingComp_CustomSign sign)
        {
            this.sign = sign;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            closeOnClickedOutside = false;

            southDraft = sign.SouthFace.Clone();
            eastDraft = sign.EastFace.Clone();
            northDraft = sign.NorthFace.Clone();
            selectedFace = FaceForDefaultPlacingRotation();
            EnsureLayerSelection();
        }

        /// <summary>
        /// 负责绘制完整招牌编辑器，并恢复共享 IMGUI 状态。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                Widgets.DrawBoxSolid(inRect, WindowBg);

                float headerHeight = Mathf.Max(HeaderHeight, Text.LineHeightOf(GameFont.Medium) + Text.LineHeightOf(GameFont.Tiny) * 2f + 24f);
                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 8f, inRect.width, Mathf.Max(0f, inRect.height - headerHeight - 8f));
                Rect faceRect = new Rect(bodyRect.x, bodyRect.y, FaceTabsWidth, bodyRect.height);
                Rect layerRect = new Rect(bodyRect.xMax - LayerPanelWidth, bodyRect.y, LayerPanelWidth, bodyRect.height);
                Rect previewRect = new Rect(faceRect.xMax + SectionGap, bodyRect.y, Mathf.Max(220f, layerRect.x - faceRect.xMax - SectionGap * 2f), bodyRect.height);

                DrawHeader(headerRect);
                DrawFaceTabs(faceRect);
                DrawPreview(previewRect);
                DrawLayerPanel(layerRect);
                ApplyDraftToBuilding();
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            float actionWidth = 520f;
            float textWidth = Mathf.Max(220f, rect.width - actionWidth - CloseXReservedWidth - 44f);
            Text.Font = GameFont.Medium;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 20f, rect.y + 8f, textWidth, Text.LineHeightOf(GameFont.Medium) + 4f), SimTranslation.T("RSMF.CustomSign.EditorTitle"));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 20f, rect.y + 42f, textWidth, Text.LineHeightOf(GameFont.Tiny) * 2f + 4f), SimTranslation.T("RSMF.CustomSign.EditorDescription"));

            float right = rect.xMax - 16f - CloseXReservedWidth;
            float y = rect.y + 18f;

            right -= 116f;
            if (SimUiStyle.DrawPrimaryButton(new Rect(right, y, 116f, 34f), SimTranslation.T("RSMF.CustomSign.DoneClose")))
            {
                ApplyDraftToBuilding();
                Messages.Message(SimTranslation.T("RSMF.CustomSign.SaveSuccess"), MessageTypeDefOf.PositiveEvent, false);
                Close();
            }

            right -= 128f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, y, 116f, 34f), SimTranslation.T("RSMF.CustomSign.ImportPath")))
                Find.WindowStack.Add(new Dialog_SignImagePathImport(AddImportedImageLayer));

            right -= 128f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(right, y, 116f, 34f), SimTranslation.T("RSMF.CustomSign.AddGalleryImage")))
                Find.WindowStack.Add(new Dialog_SignImageBrowser(AddImageLayer));

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawFaceTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, Text.LineHeightOf(GameFont.Small) + 4f), SimTranslation.T("RSMF.CustomSign.EditFace"));

            float y = rect.y + 50f;
            if (IsSingleFixedGraphic)
            {
                SignFaceKind face = FaceForDefaultPlacingRotation();
                DrawFaceTab(new Rect(rect.x + 12f, y, rect.width - 24f, 38f), face, LabelForSingleFixedFace(face));
            }
            else
            {
                DrawFaceTab(new Rect(rect.x + 12f, y, rect.width - 24f, 38f), SignFaceKind.South, SimTranslation.T("RSMF.CustomSign.Face.South"));
                y += 46f;
                DrawFaceTab(new Rect(rect.x + 12f, y, rect.width - 24f, 38f), SignFaceKind.East, SimTranslation.T("RSMF.CustomSign.Face.East"));
                y += 46f;
                DrawFaceTab(new Rect(rect.x + 12f, y, rect.width - 24f, 38f), SignFaceKind.North, SimTranslation.T("RSMF.CustomSign.Face.North"));
            }

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            string help = IsSingleFixedGraphic
                ? SimTranslation.T("RSMF.CustomSign.SingleGraphicHelp")
                : SimTranslation.T("RSMF.CustomSign.WestMirrorHelp");
            Widgets.Label(new Rect(rect.x + 12f, rect.yMax - 86f, rect.width - 24f, 72f), help);
            GUI.color = Color.white;
        }

        private void DrawFaceTab(Rect rect, SignFaceKind face, string label)
        {
            bool selected = selectedFace == face;
            if (SimUiStyle.DrawTabButton(rect, label + "  " + GetFace(face).layers.Count + "/10", selected, MutedText))
            {
                selectedFace = face;
                selectedLayerIndex = -1;
                layerScroll = Vector2.zero;
                EnsureLayerSelection();
                ResetNumericBuffers();
            }
        }

        /// <summary>
        /// 负责绘制招牌实时预览区域和当前图层参数编辑区。
        /// </summary>
        private void DrawPreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, Text.LineHeightOf(GameFont.Small) + 4f), SimTranslation.T("RSMF.CustomSign.Preview"));

            Rect previewArea = new Rect(rect.x + 22f, rect.y + 54f, rect.width - 44f, Mathf.Max(260f, rect.height * 0.58f));
            Widgets.DrawBoxSolid(previewArea, new Color(1f, 1f, 1f, 0.03f));
            SimUiStyle.DrawBorder(previewArea, new Color(1f, 1f, 1f, 0.08f));

            Thing signThing = SignThing;
            Rot4 previewRot = PreviewRotation();
            Vector2 drawSize = signThing.def.graphicData?.drawSize ?? signThing.def.size.ToVector2();
            float previewWidth = RotatesQuarterTurn(previewRot) ? drawSize.y : drawSize.x;
            float previewHeight = RotatesQuarterTurn(previewRot) ? drawSize.x : drawSize.y;
            Rect signRect = FitRect(previewArea.ContractedBy(28f), Mathf.Max(0.1f, previewWidth), Mathf.Max(0.1f, previewHeight));
            DrawBuildingPreviewBase(signRect);
            DrawPreviewLayers(signRect);

            Rect paramsRect = new Rect(rect.x + 22f, previewArea.yMax + 14f, rect.width - 44f, Mathf.Max(100f, rect.yMax - previewArea.yMax - 30f));
            DrawSelectedLayerParams(paramsRect);
        }

        /// <summary>
        /// 负责把当前草稿图层按真实预览朝向绘制到招牌预览框。
        /// </summary>
        private void DrawPreviewLayers(Rect signRect)
        {
            CustomSignDrawUtility.DrawPreviewFaceLayers(
                signRect,
                PreviewRotation(),
                southDraft,
                eastDraft,
                northDraft,
                sign.LayerWidthRatio,
                sign.LayerHeightRatio,
                selectedLayerIndex,
                Accent);
        }

        /// <summary>
        /// 负责绘制招牌建筑底图，并按真实朝向旋转显示。
        /// </summary>
        private void DrawBuildingPreviewBase(Rect signRect)
        {
            Widgets.DrawBoxSolid(signRect, new Color(0f, 0f, 0f, 0.18f));
            Thing signThing = SignThing;
            Rot4 rot = PreviewRotation();
            Material material = signThing.Graphic.MatAt(rot, signThing);
            Texture texture = material != null ? material.mainTexture : null;
            if (texture != null)
                DrawRotatedPreviewTexture(signRect, texture, rot);
            else
                Widgets.DrawBoxSolid(signRect, new Color(1f, 1f, 1f, 0.80f));

            SimUiStyle.DrawBorder(signRect, new Color(1f, 1f, 1f, 0.18f), 2f);
        }

        private void DrawSelectedLayerParams(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.06f));

            SignImageLayerData layer = SelectedLayer;
            if (layer == null)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = MutedText;
                Widgets.Label(rect, SimTranslation.T("RSMF.CustomSign.SelectLayerHint"));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }
            EnsureNumericBuffersForSelection(layer);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, Text.LineHeightOf(GameFont.Small) + 4f), SimTranslation.T("RSMF.CustomSign.LayerParams"));

            float y = rect.y + 42f;
            float rowHeight = 34f;
            DrawFloatControl(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "x", "X", ref layer.x, -1f, 1f);
            y += rowHeight + 4f;
            DrawFloatControl(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "y", "Y", ref layer.y, -1f, 1f);
            y += rowHeight + 4f;
            DrawFloatControl(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "scaleX", SimTranslation.T("RSMF.CustomSign.ScaleX"), ref layer.scaleX, 0.05f, 4f);
            y += rowHeight + 4f;
            DrawFloatControl(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "scaleY", SimTranslation.T("RSMF.CustomSign.ScaleY"), ref layer.scaleY, 0.05f, 4f);
            y += rowHeight + 4f;
            DrawFloatControl(new Rect(rect.x + 12f, y, rect.width - 24f, rowHeight), "angle", SimTranslation.T("RSMF.CustomSign.Angle"), ref layer.angle, -360f, 360f);
        }

        private void DrawFloatControl(Rect rect, string key, string label, ref float value, float min, float max)
        {
            string bufferKey = selectedFace + "_" + selectedLayerIndex + "_" + key;
            if (!numericBuffers.TryGetValue(bufferKey, out string buffer))
            {
                buffer = value.ToString("0.###");
                numericBuffers[bufferKey] = buffer;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Rect labelRect = new Rect(rect.x, rect.y + 8f, 58f, Text.LineHeightOf(GameFont.Tiny) + 2f);
            Widgets.Label(labelRect, label);

            Rect inputRect = new Rect(rect.xMax - 82f, rect.y + 3f, 82f, 28f);
            Rect sliderRect = new Rect(labelRect.xMax + 8f, rect.y + 7f, Mathf.Max(40f, inputRect.x - labelRect.xMax - 18f), 24f);
            float sliderValue = Widgets.HorizontalSlider(sliderRect, value, min, max);
            if (Mathf.Abs(sliderValue - value) > 0.0001f)
            {
                value = sliderValue;
                buffer = value.ToString("0.###");
            }

            Widgets.TextFieldNumeric(inputRect, ref value, ref buffer, min, max);
            value = Mathf.Clamp(value, min, max);
            numericBuffers[bufferKey] = buffer;
            GUI.color = Color.white;
        }

        private void DrawLayerPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 12f, rect.width - 32f, Text.LineHeightOf(GameFont.Small) + 4f), SimTranslation.T("RSMF.CustomSign.Layers"));

            Rect buttonsRect = new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 32f);
            if (SimUiStyle.DrawSecondaryButton(new Rect(buttonsRect.x, buttonsRect.y, 96f, 30f), SimTranslation.T("RSMF.CustomSign.AddGallery")))
                Find.WindowStack.Add(new Dialog_SignImageBrowser(AddImageLayer));
            if (SimUiStyle.DrawSecondaryButton(new Rect(buttonsRect.x + 104f, buttonsRect.y, 96f, 30f), SimTranslation.T("RSMF.CustomSign.ImportPathShort")))
                Find.WindowStack.Add(new Dialog_SignImagePathImport(AddImportedImageLayer));
            if (SimUiStyle.DrawDangerButton(new Rect(buttonsRect.xMax - 82f, buttonsRect.y, 82f, 30f), SimTranslation.T("RSMF.CustomSign.Delete"), SelectedLayer != null))
                DeleteSelectedLayer();

            Rect listRect = new Rect(rect.x + 12f, buttonsRect.yMax + 10f, rect.width - 24f, rect.height - 100f);
            float viewHeight = Mathf.Max(listRect.height, CurrentFace.layers.Count * 70f);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), viewHeight);

            Widgets.BeginScrollView(listRect, ref layerScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < CurrentFace.layers.Count; i++)
            {
                DrawLayerRow(new Rect(0f, y, viewRect.width, 62f), i, CurrentFace.layers[i]);
                y += 70f;
            }
            Widgets.EndScrollView();
        }

        private void DrawLayerRow(Rect rect, int index, SignImageLayerData layer)
        {
            bool selected = selectedLayerIndex == index;
            Widgets.DrawBoxSolid(rect, selected ? SoftAccent : new Color(1f, 1f, 1f, Mouse.IsOver(rect) ? 0.05f : 0.02f));
            SimUiStyle.DrawBorder(rect, selected ? Accent : new Color(1f, 1f, 1f, 0.05f));

            Rect iconRect = new Rect(rect.x + 8f, rect.y + 8f, 46f, 46f);
            GUI.DrawTexture(iconRect, SignTextureCache.GetTexture(layer.imageId), ScaleMode.ScaleToFit, true);
            SimUiStyle.DrawBorder(iconRect, new Color(1f, 1f, 1f, 0.10f));

            string title = string.IsNullOrEmpty(layer.label) ? layer.imageId : layer.label;
            Text.Font = GameFont.Small;
            GUI.color = layer.enabled ? Color.white : MutedText;
            Widgets.Label(new Rect(iconRect.xMax + 8f, rect.y + 8f, rect.width - 176f, Text.LineHeightOf(GameFont.Small) + 2f), title.Truncate(rect.width - 184f));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            string state = SignImageLibrary.ImageExists(layer.imageId) ? SimTranslation.T("RSMF.CustomSign.Linked") : SimTranslation.T("RSMF.CustomSign.ImageMissing");
            Widgets.Label(new Rect(iconRect.xMax + 8f, rect.y + 34f, rect.width - 176f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomSign.LayerState", state.Named("state"), layer.drawOrder.Named("order")));

            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 108f, rect.y + 7f, 46f, 24f), SimTranslation.T("RSMF.CustomSign.Up"), index > 0, GameFont.Tiny))
                MoveLayer(index, -1);
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 56f, rect.y + 7f, 46f, 24f), SimTranslation.T("RSMF.CustomSign.Down"), index < CurrentFace.layers.Count - 1, GameFont.Tiny))
                MoveLayer(index, 1);
            if (SimUiStyle.DrawSecondaryButton(new Rect(rect.xMax - 108f, rect.y + 34f, 98f, 22f), layer.enabled ? SimTranslation.T("RSMF.CustomSign.Enabled") : SimTranslation.T("RSMF.CustomSign.Disabled"), true, GameFont.Tiny))
            {
                layer.enabled = !layer.enabled;
                ApplyDraftToBuilding();
            }

            if (Widgets.ButtonInvisible(rect))
            {
                selectedLayerIndex = index;
                ResetNumericBuffers();
            }

            GUI.color = Color.white;
        }

        private void AddImportedImageLayer(SignImageRecord record)
        {
            if (record != null)
                AddImageLayer(record.imageId);
        }

        private void AddImageLayer(string imageId)
        {
            if (string.IsNullOrEmpty(imageId))
                return;

            if (CurrentFace.layers.Count >= SignImageLibrary.MaxFaceLayerCount)
            {
                Messages.Message(SimTranslation.T("RSMF.CustomSign.MaxLayerReached"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            SignImageRecord record = SignImageLibrary.GetRecord(imageId);
            CurrentFace.layers.Add(new SignImageLayerData
            {
                imageId = imageId,
                label = record?.label ?? imageId,
                enabled = true,
                scaleX = 1f,
                scaleY = 1f,
                drawOrder = CurrentFace.layers.Count
            });
            selectedLayerIndex = CurrentFace.layers.Count - 1;
            ResetNumericBuffers();
            ReorderCurrentFace();
        }

        private void DeleteSelectedLayer()
        {
            if (SelectedLayer == null)
                return;

            CurrentFace.layers.RemoveAt(selectedLayerIndex);
            selectedLayerIndex = Mathf.Clamp(selectedLayerIndex, 0, CurrentFace.layers.Count - 1);
            ResetNumericBuffers();
            ReorderCurrentFace();
        }

        private void MoveLayer(int index, int direction)
        {
            int target = index + direction;
            if (target < 0 || target >= CurrentFace.layers.Count)
                return;

            SignImageLayerData layer = CurrentFace.layers[index];
            CurrentFace.layers[index] = CurrentFace.layers[target];
            CurrentFace.layers[target] = layer;
            selectedLayerIndex = target;
            ResetNumericBuffers();
            ReorderCurrentFace();
        }

        private void ApplyDraftToBuilding()
        {
            ReorderFace(southDraft);
            ReorderFace(eastDraft);
            ReorderFace(northDraft);
            sign.SetFaces(southDraft.Clone(), eastDraft.Clone(), northDraft.Clone());
        }

        private SignFaceData CurrentFace => GetFace(selectedFace);

        private SignImageLayerData SelectedLayer
        {
            get
            {
                if (selectedLayerIndex < 0 || selectedLayerIndex >= CurrentFace.layers.Count)
                    return null;
                return CurrentFace.layers[selectedLayerIndex];
            }
        }

        private SignFaceData GetFace(SignFaceKind face)
        {
            if (face == SignFaceKind.East) return eastDraft;
            if (face == SignFaceKind.North) return northDraft;
            return southDraft;
        }

        private void EnsureLayerSelection()
        {
            if (CurrentFace.layers.Count == 0)
                selectedLayerIndex = -1;
            else
                selectedLayerIndex = Mathf.Clamp(selectedLayerIndex, 0, CurrentFace.layers.Count - 1);
        }

        private void EnsureNumericBuffersForSelection(SignImageLayerData layer)
        {
            if (numericBufferFace == selectedFace && numericBufferLayerIndex == selectedLayerIndex)
                return;

            numericBuffers.Clear();
            numericBufferFace = selectedFace;
            numericBufferLayerIndex = selectedLayerIndex;
            numericBuffers[selectedFace + "_" + selectedLayerIndex + "_x"] = layer.x.ToString("0.###");
            numericBuffers[selectedFace + "_" + selectedLayerIndex + "_y"] = layer.y.ToString("0.###");
            numericBuffers[selectedFace + "_" + selectedLayerIndex + "_scaleX"] = layer.scaleX.ToString("0.###");
            numericBuffers[selectedFace + "_" + selectedLayerIndex + "_scaleY"] = layer.scaleY.ToString("0.###");
            numericBuffers[selectedFace + "_" + selectedLayerIndex + "_angle"] = layer.angle.ToString("0.###");
        }

        private void ResetNumericBuffers()
        {
            numericBuffers.Clear();
            numericBufferLayerIndex = -999;
            numericBufferFace = selectedFace;
        }

        private void ReorderCurrentFace()
        {
            ReorderFace(CurrentFace);
        }

        private static void ReorderFace(SignFaceData face)
        {
            for (int i = 0; i < face.layers.Count; i++)
                face.layers[i].drawOrder = i;
        }

        private SignFaceKind FaceForDefaultPlacingRotation()
        {
            if (sign == null)
                return SignFaceKind.South;

            Rot4 defaultRot = SignThing?.def?.defaultPlacingRot ?? Rot4.South;
            if (defaultRot == Rot4.North)
                return SignFaceKind.North;
            if (defaultRot == Rot4.East || defaultRot == Rot4.West)
                return SignFaceKind.East;
            return SignFaceKind.South;
        }

        /// <summary>
        /// 返回单贴图固定朝向招牌的编辑入口标签。
        /// </summary>
        private string LabelForSingleFixedFace(SignFaceKind face)
        {
            Rot4 defaultRot = SignThing?.def?.defaultPlacingRot ?? Rot4.South;
            if (defaultRot == Rot4.West)
                return SimTranslation.T("RSMF.CustomSign.Face.West");
            if (face == SignFaceKind.North)
                return SimTranslation.T("RSMF.CustomSign.Face.North");
            if (face == SignFaceKind.East)
                return SimTranslation.T("RSMF.CustomSign.Face.East");
            return SimTranslation.T("RSMF.CustomSign.Face.South");
        }

        /// <summary>
        /// 返回当前预览应使用的真实建筑朝向。
        /// </summary>
        private Rot4 PreviewRotation()
        {
            if (IsSingleFixedGraphic)
                return SignThing?.Rotation ?? SignThing?.def?.defaultPlacingRot ?? Rot4.South;

            if (selectedFace == SignFaceKind.North)
                return Rot4.North;
            if (selectedFace == SignFaceKind.East)
                return Rot4.East;
            return Rot4.South;
        }

        /// <summary>
        /// 负责按 RimWorld 地图绘制的朝向规则旋转预览底图。
        /// </summary>
        private static void DrawRotatedPreviewTexture(Rect signRect, Texture texture, Rot4 rot)
        {
            Rect textureRect = LogicalRectForRotation(signRect, rot);
            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(AngleForRotation(rot), signRect.center);
            GUI.DrawTexture(textureRect, texture, ScaleMode.ScaleToFit, true);
            GUI.matrix = oldMatrix;
        }

        /// <summary>
        /// 返回旋转前的逻辑绘制框，供预览底图按地图网格旋转后落回显示框。
        /// </summary>
        private static Rect LogicalRectForRotation(Rect displayRect, Rot4 rot)
        {
            if (!RotatesQuarterTurn(rot))
                return displayRect;

            return new Rect(
                displayRect.center.x - displayRect.height * 0.5f,
                displayRect.center.y - displayRect.width * 0.5f,
                displayRect.height,
                displayRect.width);
        }

        /// <summary>
        /// 判断指定朝向是否会让绘制平面产生九十度旋转。
        /// </summary>
        private static bool RotatesQuarterTurn(Rot4 rot)
        {
            return rot == Rot4.East || rot == Rot4.West;
        }

        /// <summary>
        /// 返回指定朝向在二维预览中需要使用的旋转角度。
        /// </summary>
        private static float AngleForRotation(Rot4 rot)
        {
            if (rot == Rot4.East) return 90f;
            if (rot == Rot4.West) return 270f;
            if (rot == Rot4.North) return 180f;
            return 0f;
        }

        private static Rect FitRect(Rect area, float aspectWidth, float aspectHeight)
        {
            float targetAspect = aspectWidth / aspectHeight;
            float areaAspect = area.width / Mathf.Max(1f, area.height);
            if (areaAspect > targetAspect)
            {
                float width = area.height * targetAspect;
                return new Rect(area.center.x - width * 0.5f, area.y, width, area.height);
            }

            float height = area.width / targetAspect;
            return new Rect(area.x, area.center.y - height * 0.5f, area.width, height);
        }

        /// <summary>
        /// 判断当前招牌是否是单贴图且固定朝向的 Def。
        /// </summary>
        private bool IsSingleFixedGraphic
        {
            get
            {
                Thing thing = SignThing;
                return thing?.def?.graphicData?.graphicClass == typeof(Graphic_Single) && !thing.def.rotatable;
            }
        }

        /// <summary>
        /// 返回当前编辑组件所在的建筑实例，供预览和 Def 尺寸读取使用。
        /// </summary>
        private Thing SignThing => sign?.parent;
    }
}
