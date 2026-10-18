using SimManagementLib.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //绘制页面管理窗口，职责是呈现可翻译的页签配置和排列操作。
    public partial class Dialog_BusinessPageManager
    {
        //绘制标题和摘要，负责让玩家快速确认当前管理对象。
        private void DrawHeader(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 46f, Text.LineHeightOf(GameFont.Medium) + 4f), SimTranslation.T("RSMF.PageManager.Title"));

            Text.Font = GameFont.Tiny;
            GUI.color = DimText;
            Rect summaryRect = new Rect(rect.x, rect.y + Text.LineHeightOf(GameFont.Medium) + 8f, rect.width - 46f, Text.LineHeightOf(GameFont.Tiny) + 4f);
            ShopUiVisualUtility.DrawCellLabel(summaryRect, SimTranslation.T("RSMF.PageManager.Summary", (VisibleCount).Named("visible"), (drafts.Count).Named("total")), DimText, GameFont.Tiny);
        }
        //绘制顶部操作栏，负责提供批量显示和恢复默认配置入口。
        private void DrawToolbar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            float buttonWidth = 118f;
            Rect showAllRect = new Rect(rect.x + 8f, rect.y + 5f, buttonWidth, rect.height - 10f);
            Rect resetRect = new Rect(showAllRect.xMax + 8f, showAllRect.y, buttonWidth, showAllRect.height);

            if (SimUiStyle.DrawSecondaryButton(showAllRect, SimTranslation.T("RSMF.PageManager.ShowAll"), true, GameFont.Tiny))
            {
                for (int i = 0; i < drafts.Count; i++)
                    drafts[i].Visible = true;
            }

            if (SimUiStyle.DrawSecondaryButton(resetRect, SimTranslation.T("RSMF.PageManager.Reset"), true, GameFont.Tiny))
                RestoreDefault();

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = DimText;
            ShopUiVisualUtility.DrawCellLabel(new Rect(resetRect.xMax + 8f, rect.y, rect.xMax - resetRect.xMax - 16f, rect.height), SimTranslation.T("RSMF.PageManager.ResetHint"), DimText, GameFont.Tiny);
            Text.Anchor = TextAnchor.UpperLeft;
        }
        //绘制主体区域，负责把页面列表和详情面板分开避免控件互相挤压。
        private void DrawBody(Rect rect)
        {
            Rect listRect = new Rect(rect.x, rect.y, rect.width - InspectorWidth - Gap, rect.height);
            Rect inspectorRect = new Rect(listRect.xMax + Gap, rect.y, InspectorWidth, rect.height);

            DrawPageList(listRect);
            DrawInspector(inspectorRect);
        }
        //绘制页面列表，负责显示每个页面的勾选状态、顺序和行级操作。
        private void DrawPageList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect headerRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 28f);
            DrawListHeader(headerRect);

            Rect outRect = new Rect(rect.x + 8f, headerRect.yMax + 4f, rect.width - 16f, rect.height - headerRect.height - 20f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, Mathf.Max(outRect.height + 1f, drafts.Count * (RowHeight + 6f) + 8f));

            Widgets.BeginScrollView(outRect, ref listScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < drafts.Count; i++)
            {
                Rect row = new Rect(0f, y, viewRect.width, RowHeight);
                DrawPageRow(row, i);
                y += RowHeight + 6f;
            }
            Widgets.EndScrollView();
        }
        //绘制列表表头，负责给列含义提供固定对齐参考。
        private void DrawListHeader(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = DimText;
            Widgets.Label(new Rect(rect.x, rect.y, 70f, rect.height), SimTranslation.T("RSMF.PageManager.Visible"));
            Widgets.Label(new Rect(rect.x + 72f, rect.y, rect.width - 220f, rect.height), SimTranslation.T("RSMF.PageManager.Page"));
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.xMax - 146f, rect.y, 136f, rect.height), SimTranslation.T("RSMF.PageManager.Order"));
            Text.Anchor = TextAnchor.UpperLeft;
        }
        //绘制单个页面行，负责处理勾选、选中和上下移动操作。
        private void DrawPageRow(Rect row, int index)
        {
            PageDraft draft = drafts[index];
            if (index % 2 == 1)
                Widgets.DrawBoxSolid(row, RowAltBg);
            if (index == selectedIndex)
                Widgets.DrawBoxSolid(row, SelectedBg);
            SimUiStyle.DrawBorder(row, new Color(1f, 1f, 1f, 0.06f));

            Rect checkRect = new Rect(row.x + 12f, row.y + (row.height - CheckSize) / 2f, CheckSize, CheckSize);
            bool visible = draft.Visible;
            Widgets.Checkbox(checkRect.x, checkRect.y, ref visible, CheckSize, VisibleCount <= 1 && draft.Visible, true);
            draft.Visible = visible;

            Rect labelRect = new Rect(checkRect.xMax + 14f, row.y + 8f, row.width - 224f, Text.LineHeightOf(GameFont.Small) + 4f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = draft.Visible ? Color.white : DimText;
            Widgets.Label(labelRect, (draft.Label ?? draft.DefName).Truncate(labelRect.width));

            Rect defRect = new Rect(labelRect.x, labelRect.yMax + 4f, labelRect.width, Text.LineHeightOf(GameFont.Tiny) + 4f);
            Text.Font = GameFont.Tiny;
            GUI.color = DimText;
            Widgets.Label(defRect, draft.DefName.Truncate(defRect.width));

            Rect upRect = new Rect(row.xMax - 112f, row.y + 15f, 50f, 32f);
            Rect downRect = new Rect(upRect.xMax + 6f, upRect.y, 50f, upRect.height);
            if (SimUiStyle.DrawSecondaryButton(upRect, SimTranslation.T("RSMF.PageManager.Up"), index > 0, GameFont.Tiny))
                MoveDraft(index, index - 1);
            if (SimUiStyle.DrawSecondaryButton(downRect, SimTranslation.T("RSMF.PageManager.Down"), index < drafts.Count - 1, GameFont.Tiny))
                MoveDraft(index, index + 1);

            if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - 124f, row.height), false))
                selectedIndex = index;
            if (!string.IsNullOrEmpty(draft.Description))
                TooltipHandler.TipRegion(row, draft.Description);
        }
        //绘制详情面板，负责展示当前页面信息和常用单页操作。
        private void DrawInspector(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
            Rect inner = rect.ContractedBy(12f);

            if (drafts.Count == 0)
            {
                DrawWrappedText(inner, SimTranslation.T("RSMF.PageManager.Empty"), GameFont.Small, DimText);
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, drafts.Count - 1);
            PageDraft draft = drafts[selectedIndex];
            float y = inner.y;
            y = DrawWrappedText(new Rect(inner.x, y, inner.width, 60f), draft.Label ?? draft.DefName, GameFont.Small, Color.white) + 8f;
            y = DrawWrappedText(new Rect(inner.x, y, inner.width, 44f), draft.DefName, GameFont.Tiny, DimText) + 12f;

            Rect toggleRect = new Rect(inner.x, y, inner.width, 34f);
            string toggleLabel = draft.Visible ? SimTranslation.T("RSMF.PageManager.HideTab") : SimTranslation.T("RSMF.PageManager.ShowTab");
            if (SimUiStyle.DrawSecondaryButton(toggleRect, toggleLabel, draft.Visible ? VisibleCount > 1 : true, GameFont.Tiny))
                draft.Visible = !draft.Visible;
            y = toggleRect.yMax + 8f;

            Rect upRect = new Rect(inner.x, y, (inner.width - 8f) / 2f, 34f);
            Rect downRect = new Rect(upRect.xMax + 8f, y, upRect.width, upRect.height);
            if (SimUiStyle.DrawSecondaryButton(upRect, SimTranslation.T("RSMF.PageManager.Up"), selectedIndex > 0, GameFont.Tiny))
                MoveDraft(selectedIndex, selectedIndex - 1);
            if (SimUiStyle.DrawSecondaryButton(downRect, SimTranslation.T("RSMF.PageManager.Down"), selectedIndex < drafts.Count - 1, GameFont.Tiny))
                MoveDraft(selectedIndex, selectedIndex + 1);
            y = upRect.yMax + 14f;

            string description = string.IsNullOrEmpty(draft.Description) ? SimTranslation.T("RSMF.PageManager.NoDescription") : draft.Description;
            DrawWrappedText(new Rect(inner.x, y, inner.width, inner.yMax - y), description, GameFont.Tiny, DimText);
        }
        //绘制底部按钮栏，负责提供应用、取消和非法状态提示。
        private void DrawFooter(Rect rect)
        {
            SimUiStyle.DrawBorder(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            bool canApply = VisibleCount > 0;
            Rect applyRect = new Rect(rect.xMax - 120f, rect.y + 8f, 112f, 34f);
            Rect cancelRect = new Rect(applyRect.x - 120f, applyRect.y, 112f, applyRect.height);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = canApply ? DimText : WarnText;
            string status = canApply ? SimTranslation.T("RSMF.PageManager.ApplyHint") : SimTranslation.T("RSMF.PageManager.OnePageRequired");
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 4f, rect.y, cancelRect.x - rect.x - 12f, rect.height), status, canApply ? DimText : WarnText, GameFont.Tiny);

            if (SimUiStyle.DrawSecondaryButton(cancelRect, SimTranslation.T("RSMF.Common.Cancel"), true, GameFont.Tiny))
                Close();
            if (SimUiStyle.DrawPrimaryButton(applyRect, SimTranslation.T("RSMF.PageManager.Apply"), canApply, GameFont.Tiny))
                ApplyAndClose();
        }
        //绘制可换行文本，负责根据字体测量高度并返回文本底部坐标。
        private static float DrawWrappedText(Rect rect, string text, GameFont font, Color color)
        {
            Text.Font = font;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = color;
            float height = Mathf.Min(rect.height, Text.CalcHeight(text ?? "", rect.width));
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, height), text ?? "");
            return rect.y + height;
        }
    }
}
