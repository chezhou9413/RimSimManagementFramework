using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //预览画布只显示图片与定位辅助线，鼠标拖动直接修改所选图层。
    public partial class Dialog_CustomSignEditor
    {
        //以独立裁剪区域绘制预览，超出画布的图片不会覆盖按钮或参数。
        private void DrawPreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            Rect inner = rect.ContractedBy(10f);
            float h = ControlHeight;
            DrawFaceTabs(new Rect(inner.x, inner.y, inner.width, h));
            Rot4 rotation = PreviewRotation();
            bool inherited = !CustomSignDrawUtility.HasDrawableLayer(CurrentFace) && rotation != Rot4.South
                && CustomSignDrawUtility.HasDrawableLayer(southDraft);
            string hint = SimTranslation.T(inherited ? "RSMF.CustomSign.InheritedPreview" : "RSMF.CustomSign.CanvasHelp");
            float hintHeight = Text.CalcHeight(hint, inner.width);
            Rect canvas = new Rect(inner.x, inner.y + h + 8f, inner.width,
                Mathf.Max(0f, inner.height - h - hintHeight - 20f));
            Rect area = new Rect(0f, 0f, canvas.width, canvas.height);
            GUI.BeginGroup(canvas);
            try
            {
                DrawCanvasGrid(area);
                float pixels = CustomSignPreviewUtility.PixelsPerUnit(area, SignThing, rotation);
                if (pixels > 0f)
                {
                    CustomSignPreviewUtility.DrawBase(area.center, pixels, SignThing, rotation);
                    SignFaceData face = inherited ? southDraft : CurrentFace;
                    bool mirror = rotation == Rot4.West && !inherited;
                    for (int i = 0; i < face.layers.Count; i++)
                    {
                        SignImageLayerData layer = face.layers[i];
                        if (!layer.enabled || layer.imageId.NullOrEmpty()) continue;
                        Rect image = CustomSignPreviewUtility.LayerRect(area.center, pixels, sign, layer, rotation, mirror);
                        CustomSignPreviewUtility.DrawLayer(image, layer, rotation, mirror, !inherited && i == selectedLayerIndex, Accent);
                    }
                    HandleCanvasInput(area, pixels, rotation, mirror, inherited);
                }
            }
            finally { GUI.EndGroup(); }
            SimUiStyle.DrawBorder(canvas, new Color(1f, 1f, 1f, 0.12f));
            GUI.color = MutedText;
            Widgets.Label(new Rect(inner.x, canvas.yMax + 8f, inner.width, hintHeight), hint);
            GUI.color = Color.white;
        }

        //透明图片使用棋盘背景，中心辅助线便于判断位置偏移。
        private static void DrawCanvasGrid(Rect area)
        {
            Widgets.DrawBoxSolid(area, new Color(0.12f, 0.14f, 0.17f));
            const float tile = 24f;
            for (int y = 0; y < Mathf.CeilToInt(area.height / tile); y++)
                for (int x = 0; x < Mathf.CeilToInt(area.width / tile); x++)
                    if ((x + y) % 2 == 0) Widgets.DrawBoxSolid(new Rect(x * tile, y * tile, tile, tile), new Color(1f, 1f, 1f, 0.025f));
            Widgets.DrawBoxSolid(new Rect(area.center.x, 0f, 1f, area.height), new Color(1f, 1f, 1f, 0.12f));
            Widgets.DrawBoxSolid(new Rect(0f, area.center.y, area.width, 1f), new Color(1f, 1f, 1f, 0.12f));
        }

        //先命中最上层，再捕获拖动到松开鼠标；经过其他控件时也不会抢走拖动。
        private void HandleCanvasInput(Rect area, float pixels, Rot4 rotation, bool mirror, bool inherited)
        {
            Event current = Event.current;
            int control = GUIUtility.GetControlID(0x5349474E, FocusType.Passive);
            if (current.type == EventType.MouseDown && current.button == 0 && area.Contains(current.mousePosition) && !inherited)
            {
                for (int i = CurrentFace.layers.Count - 1; i >= 0; i--)
                {
                    SignImageLayerData layer = CurrentFace.layers[i];
                    if (!layer.enabled || layer.imageId.NullOrEmpty()) continue;
                    Rect image = CustomSignPreviewUtility.LayerRect(area.center, pixels, sign, layer, rotation, mirror);
                    if (!CustomSignPreviewUtility.Contains(image, current.mousePosition, rotation, layer)) continue;
                    SelectLayer(i);
                    draggedLayer = layer;
                    originalOffset = new Vector2(layer.x, layer.y);
                    dragStart = current.mousePosition;
                    dragControl = control;
                    GUIUtility.hotControl = control;
                    GUI.FocusControl(null);
                    current.Use();
                    break;
                }
            }
            if (GUIUtility.hotControl != control || draggedLayer == null) return;
            if (current.type == EventType.MouseDrag)
            {
                Vector2 delta = CustomSignPreviewUtility.LocalDrag(current.mousePosition - dragStart, pixels, SignThing, rotation, mirror);
                draggedLayer.x = Mathf.Clamp(originalOffset.x + delta.x, -1f, 1f);
                draggedLayer.y = Mathf.Clamp(originalOffset.y + delta.y, -1f, 1f);
                numericBuffers.Clear();
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0)
            {
                GUIUtility.hotControl = 0;
                draggedLayer = null;
                current.Use();
            }
        }
    }
}
