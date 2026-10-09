using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //图层列表和参数面板分别滚动，缩小窗口后仍能访问全部操作。
    public partial class Dialog_CustomSignEditor
    {
        private static float LayerRowHeight => Mathf.Max(56f, ShopUiVisualUtility.LineHeight(GameFont.Small) * 2f + 16f);

        //上方保留短图层列表，下方展示当前图层的编辑控件。
        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            Rect inner = rect.ContractedBy(10f);
            float h = ControlHeight;
            float line = Text.LineHeightOf(GameFont.Small) + 4f;
            ShopUiVisualUtility.DrawCellLabel(new Rect(inner.x, inner.y, inner.width, line),
                SimTranslation.T("RSMF.CustomSign.Layers") + "  " + CurrentFace.layers.Count + "/" + SignImageLibrary.MaxFaceLayerCount);
            float listHeight = Mathf.Min(150f, inner.height * 0.26f);
            Rect list = new Rect(inner.x, inner.y + line + 6f, inner.width, listHeight);
            DrawLayerList(list);
            float y = list.yMax + 6f;
            float half = (inner.width - 6f) / 2f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(inner.x, y, half, h), SimTranslation.T("RSMF.CustomSign.LayerForward"),
                SelectedLayer != null && selectedLayerIndex < CurrentFace.layers.Count - 1)) MoveLayer(1);
            if (SimUiStyle.DrawSecondaryButton(new Rect(inner.x + half + 6f, y, half, h), SimTranslation.T("RSMF.CustomSign.LayerBackward"),
                selectedLayerIndex > 0)) MoveLayer(-1);
            y += h + 10f;
            DrawParameters(new Rect(inner.x, y, inner.width, Mathf.Max(0f, inner.yMax - y)));
        }

        //从最上层开始列出缩略图，选中区域不与其他按钮重叠。
        private void DrawLayerList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.15f));
            if (CurrentFace.layers.Count == 0)
            {
                Widgets.Label(rect.ContractedBy(8f), SimTranslation.T("RSMF.CustomSign.EmptyLayers"));
                return;
            }
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, CurrentFace.layers.Count * LayerRowHeight));
            Widgets.BeginScrollView(rect, ref layerScroll, view);
            try
            {
                for (int i = CurrentFace.layers.Count - 1; i >= 0; i--)
                {
                    var row = new Rect(0f, (CurrentFace.layers.Count - 1 - i) * LayerRowHeight, view.width, LayerRowHeight - 4f);
                    SignImageLayerData layer = CurrentFace.layers[i];
                    bool selected = i == selectedLayerIndex;
                    Widgets.DrawBoxSolid(row, selected ? new Color(Accent.r, Accent.g, Accent.b, 0.16f) : new Color(1f, 1f, 1f, 0.025f));
                    if (selected) SimUiStyle.DrawBorder(row, Accent);
                    Rect icon = new Rect(row.x + 6f, row.center.y - 20f, 40f, 40f);
                    GUI.DrawTexture(icon, SignTextureCache.GetTexture(layer.imageId), ScaleMode.ScaleToFit, true);
                    float x = icon.xMax + 8f, width = row.xMax - x - 6f;
                    string label = layer.label.NullOrEmpty() ? layer.imageId : layer.label;
                    float line = ShopUiVisualUtility.LineHeight(GameFont.Small);
                    ShopUiVisualUtility.DrawCellLabel(new Rect(x, row.y + 4f, width, line), label.Truncate(width));
                    string state = !SignImageLibrary.ImageExists(layer.imageId) ? "ImageMissing" : layer.enabled ? "Enabled" : "Disabled";
                    ShopUiVisualUtility.DrawCellLabel(new Rect(x, row.y + 6f + line, width, line), SimTranslation.T("RSMF.CustomSign." + state), MutedText);
                    TooltipHandler.TipRegion(row, label);
                    if (Widgets.ButtonInvisible(row)) SelectLayer(i);
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        //常用大小与角度直接显示，精确位置按需展开，参数始终限制在滚动区。
        private void DrawParameters(Rect rect)
        {
            SignImageLayerData layer = SelectedLayer;
            if (layer == null)
            {
                Widgets.Label(rect.ContractedBy(6f), SimTranslation.T("RSMF.CustomSign.SelectLayerHint"));
                return;
            }
            float h = ControlHeight, stride = h + 6f;
            float width = rect.width - 16f;
            string help = SimTranslation.T("RSMF.CustomSign.OffsetHelp");
            float helpHeight = showPosition ? Text.CalcHeight(help, width) + 6f : 0f;
            Rect view = new Rect(0f, 0f, width, Mathf.Max(rect.height, stride * (showPosition ? 10f : 8f) + helpHeight));
            Widgets.BeginScrollView(rect, ref parameterScroll, view);
            try
            {
                float y = 0f;
                string title = layer.label.NullOrEmpty() ? SimTranslation.T("RSMF.CustomSign.ShowLayer") : layer.label;
                Rect visibility = new Rect(0f, y, width, h);
                Widgets.CheckboxLabeled(visibility, title.Truncate(width - 32f), ref layer.enabled);
                TooltipHandler.TipRegion(visibility, title + "\n" + SimTranslation.T("RSMF.CustomSign.ShowLayer"));
                y += stride;
                float half = (width - 6f) / 2f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(0f, y, half, h), SimTranslation.T("RSMF.CustomSign.Center")))
                { layer.x = layer.y = 0f; numericBuffers.Clear(); }
                if (SimUiStyle.DrawSecondaryButton(new Rect(half + 6f, y, half, h), SimTranslation.T("RSMF.CustomSign.ResetTransform")))
                { layer.x = layer.y = layer.angle = 0f; layer.scaleX = layer.scaleY = 1f; numericBuffers.Clear(); }
                y += stride;
                Widgets.CheckboxLabeled(new Rect(0f, y, width, h), SimTranslation.T("RSMF.CustomSign.LockAspect"), ref lockAspect);
                y += stride;
                float oldX = layer.scaleX, oldY = layer.scaleY;
                if (DrawFloatControl(new Rect(0f, y, width, h), "scaleX", SimTranslation.T("RSMF.CustomSign.ScaleX"), ref layer.scaleX, 0.05f, 4f) && lockAspect)
                {
                    float factor = Mathf.Clamp(layer.scaleX / oldX, 0.05f / oldY, 4f / oldY);
                    layer.scaleX = oldX * factor; layer.scaleY = oldY * factor;
                    numericBuffers.Remove("scaleX"); numericBuffers.Remove("scaleY");
                }
                y += stride;
                oldX = layer.scaleX; oldY = layer.scaleY;
                if (DrawFloatControl(new Rect(0f, y, width, h), "scaleY", SimTranslation.T("RSMF.CustomSign.ScaleY"), ref layer.scaleY, 0.05f, 4f) && lockAspect)
                {
                    float factor = Mathf.Clamp(layer.scaleY / oldY, 0.05f / oldX, 4f / oldX);
                    layer.scaleX = oldX * factor; layer.scaleY = oldY * factor;
                    numericBuffers.Remove("scaleX"); numericBuffers.Remove("scaleY");
                }
                y += stride;
                DrawFloatControl(new Rect(0f, y, width, h), "angle", SimTranslation.T("RSMF.CustomSign.Angle"), ref layer.angle, -360f, 360f);
                y += stride;
                Widgets.CheckboxLabeled(new Rect(0f, y, width, h), SimTranslation.T("RSMF.CustomSign.ExactPosition"), ref showPosition);
                y += stride;
                if (showPosition)
                {
                    DrawFloatControl(new Rect(0f, y, width, h), "x", SimTranslation.T("RSMF.CustomSign.OffsetX"), ref layer.x, -1f, 1f);
                    y += stride;
                    DrawFloatControl(new Rect(0f, y, width, h), "y", SimTranslation.T("RSMF.CustomSign.OffsetY"), ref layer.y, -1f, 1f);
                    y += stride;
                    GUI.color = MutedText;
                    Widgets.Label(new Rect(0f, y, width, helpHeight), help);
                    GUI.color = Color.white;
                    y += helpHeight;
                }
                if (SimUiStyle.DrawSecondaryButton(new Rect(0f, y, half, h), SimTranslation.T("RSMF.CustomSign.ReplaceImage"))) ReplaceImage(layer);
                if (SimUiStyle.DrawDangerButton(new Rect(half + 6f, y, half, h), SimTranslation.T("RSMF.CustomSign.Delete"))) DeleteLayer();
            }
            finally { Widgets.EndScrollView(); }
        }

        //同时提供滑条和精确数字输入，修改时报告是否需要联动另一轴。
        private bool DrawFloatControl(Rect rect, string key, string label, ref float value, float minimum, float maximum)
        {
            float before = value;
            if (!numericBuffers.TryGetValue(key, out string buffer)) buffer = value.ToString("0.###");
            float labelWidth = Mathf.Max(52f, Text.CalcSize(label).x + 4f);
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x, rect.y, labelWidth, rect.height), label, MutedText);
            Rect input = new Rect(rect.xMax - 64f, rect.y, 64f, rect.height);
            Rect slider = new Rect(rect.x + labelWidth + 4f, rect.y, Mathf.Max(1f, input.x - rect.x - labelWidth - 12f), rect.height);
            float next = Widgets.HorizontalSlider(slider, value, minimum, maximum);
            if (!Mathf.Approximately(next, value)) { value = next; buffer = next.ToString("0.###"); }
            Widgets.TextFieldNumeric(input, ref value, ref buffer, minimum, maximum);
            value = Mathf.Clamp(value, minimum, maximum);
            numericBuffers[key] = buffer;
            return !Mathf.Approximately(value, before);
        }
    }
}
