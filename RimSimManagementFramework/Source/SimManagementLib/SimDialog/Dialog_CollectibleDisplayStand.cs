using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 收藏品展台管理窗口，职责是绘制槽位网格并调度槽位详情控件。
    /// </summary>
    public partial class Dialog_CollectibleDisplayStand : Window
    {
        private const float PanelGap = 14f;
        private const float LeftPanelWidth = 470f;
        private const float HeaderHeight = 58f;
        private const float ScrollbarWidth = 18f;

        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color PanelAlt = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color Accent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color AccentSoft = new Color(0.25f, 0.65f, 0.85f, 0.18f);
        private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f, 1f);
        private static readonly Color DangerText = new Color(1f, 0.72f, 0.72f, 1f);

        private readonly Building_CollectibleDisplayStand stand;
        private Vector2 gridScroll;
        private int selectedSlotIndex;

        public override Vector2 InitialSize => new Vector2(980f, 720f);

        /// <summary>
        /// 初始化展台管理窗口，负责保存目标展台并配置窗口交互行为。
        /// </summary>
        public Dialog_CollectibleDisplayStand(Building_CollectibleDisplayStand stand)
        {
            this.stand = stand;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
            selectedSlotIndex = 0;
        }

        /// <summary>
        /// 绘制展台管理窗口主体，负责划分标题、网格和详情区域。
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

                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 10f, inRect.width, Mathf.Max(220f, inRect.height - HeaderHeight - 10f));
                Rect gridRect = new Rect(bodyRect.x, bodyRect.y, Mathf.Min(LeftPanelWidth, bodyRect.width * 0.54f), bodyRect.height);
                Rect detailsRect = new Rect(gridRect.xMax + PanelGap, bodyRect.y, Mathf.Max(260f, bodyRect.xMax - gridRect.xMax - PanelGap), bodyRect.height);

                DrawHeader(headerRect);
                DrawGridPanel(gridRect);
                DrawDetailsPanel(detailsRect);
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
        /// 绘制窗口标题区，负责展示展台名称和槽位概览。
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 4f, rect.width - 48f, Mathf.Max(30f, Text.LineHeightOf(GameFont.Medium) + 4f)),
                SimTranslation.T("RSMF.CollectibleDisplayStand.Window.Title"));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 16f, rect.y + 36f, rect.width - 32f, Mathf.Max(20f, Text.LineHeightOf(GameFont.Tiny) + 4f)),
                SimTranslation.T("RSMF.CollectibleDisplayStand.Window.Summary",
                    stand.Rows.Named("rows"),
                    stand.Columns.Named("columns"),
                    CountStoredSlots().Named("stored"),
                    stand.Slots.Count.Named("total")));
            ResetText();
        }

        /// <summary>
        /// 绘制左侧槽位网格面板。
        /// </summary>
        private void DrawGridPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect titleRect = new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, Mathf.Max(26f, Text.LineHeightOf(GameFont.Small) + 6f));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(titleRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Grid.Title"));

            Rect listRect = new Rect(rect.x + 12f, titleRect.yMax + 8f, rect.width - 24f, rect.height - titleRect.height - 30f);
            float gap = 6f;
            float square = Mathf.Floor((listRect.width - ScrollbarWidth - gap * (stand.Columns - 1)) / Mathf.Max(1, stand.Columns));
            square = Mathf.Clamp(square, 42f, 82f);
            float viewWidth = Mathf.Max(120f, stand.Columns * square + (stand.Columns - 1) * gap);
            float viewHeight = Mathf.Max(listRect.height + 1f, stand.Rows * square + (stand.Rows - 1) * gap);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            Widgets.BeginScrollView(listRect, ref gridScroll, viewRect);
            for (int i = 0; i < stand.Slots.Count; i++)
            {
                int row = i / stand.Columns;
                int column = i % stand.Columns;
                Rect cellRect = new Rect(column * (square + gap), row * (square + gap), square, square);
                DrawSlotCell(cellRect, stand.GetSlot(i));
            }
            Widgets.EndScrollView();
            ResetText();
        }

        /// <summary>
        /// 绘制右侧详情面板，并在槽位缺失时显示兜底信息。
        /// </summary>
        private void DrawDetailsPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            CollectibleDisplaySlotData slot = stand.GetSlot(selectedSlotIndex);
            if (slot == null)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = MutedText;
                Widgets.Label(rect, SimTranslation.T("RSMF.CollectibleDisplayStand.Error.InvalidSlot"));
                ResetText();
                return;
            }

            DrawSlotDetails(rect.ContractedBy(12f), slot);
        }

        /// <summary>
        /// 统计已放入收藏品的槽位数量。
        /// </summary>
        private int CountStoredSlots()
        {
            int count = 0;
            for (int i = 0; i < stand.Slots.Count; i++)
            {
                if (stand.GetSlot(i)?.HasStoredThing == true)
                    count++;
            }
            return count;
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
