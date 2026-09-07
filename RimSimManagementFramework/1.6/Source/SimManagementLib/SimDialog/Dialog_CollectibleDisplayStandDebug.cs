using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 收藏品展台调试定位窗口，职责是在上帝模式下调整槽位渲染参数并导出 XML 默认配置。
    /// </summary>
    public class Dialog_CollectibleDisplayStandDebug : Window
    {
        private const float LeftWidth = 210f;
        private const float Gap = 14f;
        private const float HeaderHeight = 52f;

        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color Accent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color AccentSoft = new Color(0.25f, 0.65f, 0.85f, 0.18f);
        private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f, 1f);

        private readonly Building_CollectibleDisplayStand stand;
        private readonly Dictionary<string, string> buffers = new Dictionary<string, string>();
        private Vector2 slotScroll;
        private int selectedSlotIndex;

        public override Vector2 InitialSize => new Vector2(760f, 560f);

        /// <summary>
        /// 初始化展台调试窗口，负责保存目标展台并配置窗口行为。
        /// </summary>
        public Dialog_CollectibleDisplayStandDebug(Building_CollectibleDisplayStand stand)
        {
            this.stand = stand;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        /// <summary>
        /// 绘制调试窗口主体，负责划分槽位列表和参数编辑区。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Widgets.DrawBoxSolid(inRect, WindowBg);
                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 10f, inRect.width, inRect.height - HeaderHeight - 10f);
                Rect listRect = new Rect(bodyRect.x, bodyRect.y, LeftWidth, bodyRect.height);
                Rect editRect = new Rect(listRect.xMax + Gap, bodyRect.y, bodyRect.width - LeftWidth - Gap, bodyRect.height);

                DrawHeader(headerRect);
                DrawSlotList(listRect);
                DrawEditor(editRect);
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
        /// 绘制标题区，负责提醒该窗口只用于开发定位。
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 4f, rect.width - 28f, Mathf.Max(26f, Text.LineHeightOf(GameFont.Small) + 6f)),
                SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.Title"));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 30f, rect.width - 28f, Mathf.Max(18f, Text.LineHeightOf(GameFont.Tiny) + 4f)),
                SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.Subtitle"));
            ResetText();
        }

        /// <summary>
        /// 绘制左侧槽位列表，负责切换当前编辑槽位。
        /// </summary>
        private void DrawSlotList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect inner = rect.ContractedBy(10f);
            float rowHeight = Mathf.Max(34f, Text.LineHeightOf(GameFont.Small) + 12f);
            Rect viewRect = new Rect(0f, 0f, inner.width - 18f, Mathf.Max(inner.height + 1f, stand.Slots.Count * (rowHeight + 6f)));

            Widgets.BeginScrollView(inner, ref slotScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < stand.Slots.Count; i++)
            {
                Rect row = new Rect(0f, y, viewRect.width, rowHeight);
                bool selected = i == selectedSlotIndex;
                if (SimUiStyle.DrawTabButton(row, SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.Slot", (i + 1).Named("index")), selected, Color.white))
                    selectedSlotIndex = i;
                y += rowHeight + 6f;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制右侧参数编辑区。
        /// </summary>
        private void DrawEditor(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            CollectibleDisplaySlotData slot = stand.GetSlot(selectedSlotIndex);
            if (slot == null)
                return;

            Rect inner = rect.ContractedBy(12f);
            float y = inner.y;

            DrawPreview(new Rect(inner.x, y, inner.width, 82f), slot);
            y += 96f;

            DrawSliderAndInput(new Rect(inner.x, y, inner.width, 34f), slot, "offsetX", SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.OffsetX"), ref slot.offsetX, -2.5f, 2.5f);
            y += 40f;
            DrawSliderAndInput(new Rect(inner.x, y, inner.width, 34f), slot, "offsetZ", SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.OffsetZ"), ref slot.offsetZ, -2.5f, 2.5f);
            y += 40f;
            DrawSliderAndInput(new Rect(inner.x, y, inner.width, 34f), slot, "height", SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Height"), ref slot.height, 0f, 1.5f);
            y += 40f;
            DrawSliderAndInput(new Rect(inner.x, y, inner.width, 34f), slot, "scale", SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Scale"), ref slot.scale, 0.2f, 3f);
            y += 40f;
            DrawSliderAndInput(new Rect(inner.x, y, inner.width, 34f), slot, "rotation", SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Rotation"), ref slot.rotation, -180f, 180f);
            y += 50f;

            DrawButtons(new Rect(inner.x, y, inner.width, Mathf.Max(86f, inner.yMax - y)), slot);
            ResetText();
        }

        /// <summary>
        /// 绘制选中槽位预览信息。
        /// </summary>
        private void DrawPreview(Rect rect, CollectibleDisplaySlotData slot)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            SimUiStyle.DrawBorder(rect, AccentSoft);

            Rect iconRect = new Rect(rect.x + 10f, rect.y + 10f, 62f, 62f);
            Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.25f));
            if (slot.HasStoredThing)
                Widgets.ThingIcon(iconRect.ContractedBy(5f), slot.StoredThing);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(iconRect.xMax + 12f, rect.y + 10f, rect.width - iconRect.width - 32f, 28f),
                SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.Slot", (slot.index + 1).Named("index")));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(iconRect.xMax + 12f, rect.y + 42f, rect.width - iconRect.width - 32f, 30f), BuildValueLine(slot));
        }

        /// <summary>
        /// 绘制滑条和数值输入组合控件。
        /// </summary>
        private void DrawSliderAndInput(Rect rect, CollectibleDisplaySlotData slot, string key, string label, ref float value, float min, float max)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x, rect.y, 72f, Mathf.Max(rect.height, Text.LineHeightOf(GameFont.Tiny) + 4f)), label);

            float oldValue = value;
            Rect sliderRect = new Rect(rect.x + 80f, rect.y + 5f, Mathf.Max(90f, rect.width - 170f), 24f);
            value = Widgets.HorizontalSlider(sliderRect, value, min, max, false);

            string bufferKey = slot.index + ":" + key;
            if (!buffers.TryGetValue(bufferKey, out string buffer))
                buffers[bufferKey] = buffer = value.ToString("0.###", CultureInfo.InvariantCulture);

            Rect inputRect = new Rect(rect.xMax - 78f, rect.y + 4f, 78f, Mathf.Max(26f, Text.LineHeightOf(GameFont.Tiny) + 8f));
            Widgets.TextFieldNumeric(inputRect, ref value, ref buffer, min, max);
            buffers[bufferKey] = buffer;

            if (Mathf.Abs(oldValue - value) > 0.0001f)
                stand.RefreshDisplayMesh();
            ResetText();
        }

        /// <summary>
        /// 绘制调试操作按钮。
        /// </summary>
        private void DrawButtons(Rect rect, CollectibleDisplaySlotData slot)
        {
            float buttonHeight = Mathf.Max(30f, Text.LineHeightOf(GameFont.Small) + 10f);
            Rect resetRect = new Rect(rect.x, rect.y, 128f, buttonHeight);
            Rect copySlotRect = new Rect(resetRect.xMax + 8f, rect.y, 128f, buttonHeight);
            Rect copyAllRect = new Rect(copySlotRect.xMax + 8f, rect.y, 150f, buttonHeight);

            if (SimUiStyle.DrawSecondaryButton(resetRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.ResetSlot")))
            {
                stand.ResetSlotToConfiguredDefault(slot);
                buffers.Clear();
            }

            if (SimUiStyle.DrawSecondaryButton(copySlotRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.CopySlot")))
                CopyXml(BuildSlotXml(slot));

            if (SimUiStyle.DrawPrimaryButton(copyAllRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.CopyAll")))
                CopyXml(BuildAllSlotsXml());
        }

        /// <summary>
        /// 复制 XML 到系统剪贴板并提示玩家。
        /// </summary>
        private static void CopyXml(string xml)
        {
            GUIUtility.systemCopyBuffer = xml;
            Messages.Message(SimTranslation.T("RSMF.CollectibleDisplayStand.Debug.Copied"), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// 构建选中槽位的 XML 片段。
        /// </summary>
        private static string BuildSlotXml(CollectibleDisplaySlotData slot)
        {
            return "          <li><index>" + slot.index.ToString(CultureInfo.InvariantCulture)
                + "</index><offsetX>" + F(slot.offsetX)
                + "</offsetX><offsetZ>" + F(slot.offsetZ)
                + "</offsetZ><height>" + F(slot.height)
                + "</height><scale>" + F(slot.scale)
                + "</scale><rotation>" + F(slot.rotation)
                + "</rotation></li>";
        }

        /// <summary>
        /// 构建全部槽位的 XML 默认配置片段。
        /// </summary>
        private string BuildAllSlotsXml()
        {
            List<string> lines = new List<string> { "        <slotDefaults>" };
            for (int i = 0; i < stand.Slots.Count; i++)
            {
                CollectibleDisplaySlotData slot = stand.GetSlot(i);
                if (slot != null)
                    lines.Add(BuildSlotXml(slot));
            }
            lines.Add("        </slotDefaults>");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 构建参数摘要文本。
        /// </summary>
        private static string BuildValueLine(CollectibleDisplaySlotData slot)
        {
            return SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Values",
                F(slot.offsetX).Named("x"),
                F(slot.offsetZ).Named("z"),
                F(slot.scale).Named("scale"));
        }

        /// <summary>
        /// 格式化 XML 浮点数，职责是避免本地小数逗号进入 Def。
        /// </summary>
        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 恢复窗口内部通用绘制状态。
        /// </summary>
        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
