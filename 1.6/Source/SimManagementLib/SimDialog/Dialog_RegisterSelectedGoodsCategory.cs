using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 显示框选商品的分类选择窗口，负责让玩家确认这些物品应归入哪个商品分类。
    /// </summary>
    public class Dialog_RegisterSelectedGoodsCategory : Window
    {
        private const float FooterHeight = 42f;
        private const float RowHeight = 34f;
        private readonly List<ThingDef> thingDefs;
        private readonly List<RuntimeGoodsCategory> categories;
        private Vector2 categoryScroll;
        private string selectedCategoryId;

        /// <summary>
        /// 使用待注册商品列表初始化窗口，并默认选择第一个可用分类。
        /// </summary>
        public Dialog_RegisterSelectedGoodsCategory(List<ThingDef> thingDefs)
        {
            this.thingDefs = thingDefs?.Where(def => def != null).ToList() ?? new List<ThingDef>();
            categories = QuickGoodsRegistrationUtility.GetAvailableCategories();
            selectedCategoryId = categories.FirstOrDefault()?.categoryId ?? string.Empty;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnClickedOutside = true;
        }

        /// <summary>
        /// 返回窗口初始尺寸，负责给分类列表和底部按钮预留空间。
        /// </summary>
        public override Vector2 InitialSize => new Vector2(620f, 560f);

        /// <summary>
        /// 绘制分类选择窗口，负责展示商品预览、分类列表和确认按钮。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width - 34f, Text.LineHeightOf(GameFont.Medium) + 4f);
                Widgets.Label(titleRect, SimTranslation.T("RSMF.QuickGoodsRegister.Title"));
                Text.Font = GameFont.Small;

                float y = titleRect.yMax + 8f;
                string summary = SimTranslation.T("RSMF.QuickGoodsRegister.Summary", thingDefs.Count.Named("count"));
                float summaryHeight = Mathf.Max(Text.LineHeightOf(GameFont.Small), Text.CalcHeight(summary, inRect.width));
                Widgets.Label(new Rect(inRect.x, y, inRect.width, summaryHeight), summary);
                y += summaryHeight + 6f;

                string preview = QuickGoodsRegistrationUtility.BuildThingPreview(thingDefs);
                if (!string.IsNullOrEmpty(preview))
                {
                    float previewHeight = Mathf.Min(72f, Mathf.Max(Text.LineHeightOf(GameFont.Small), Text.CalcHeight(preview, inRect.width)));
                    Widgets.Label(new Rect(inRect.x, y, inRect.width, previewHeight), preview);
                    y += previewHeight + 8f;
                }

                Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - FooterHeight - y - 8f);
                DrawCategoryList(listRect);
                DrawFooter(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight));
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
        /// 绘制可滚动分类列表，负责让玩家选择目标商品分类。
        /// </summary>
        private void DrawCategoryList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            if (categories.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, SimTranslation.T("RSMF.QuickGoodsRegister.NoCategories"));
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Rect outRect = rect.ContractedBy(6f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, categories.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref categoryScroll, viewRect);
            for (int i = 0; i < categories.Count; i++)
            {
                RuntimeGoodsCategory category = categories[i];
                Rect row = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                DrawCategoryRow(row, category, i % 2 == 1);
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制单个分类行，负责处理选中状态和来源提示。
        /// </summary>
        private void DrawCategoryRow(Rect rect, RuntimeGoodsCategory category, bool alternate)
        {
            if (alternate)
                Widgets.DrawAltRect(rect);

            bool selected = string.Equals(selectedCategoryId, category.categoryId);
            if (selected)
                Widgets.DrawHighlightSelected(rect);
            else if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);

            if (Widgets.ButtonInvisible(rect))
                selectedCategoryId = category.categoryId;

            Rect labelRect = rect.ContractedBy(8f, 4f);
            string source = category.IsBuiltInCategory
                ? SimTranslation.T("RSMF.QuickGoodsRegister.BuiltInCategory")
                : SimTranslation.T("RSMF.QuickGoodsRegister.CustomCategory");
            string text = $"{category.label}  ({source})";
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, text.Truncate(labelRect.width));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// 绘制底部确认和取消按钮，负责提交快捷注册操作。
        /// </summary>
        private void DrawFooter(Rect rect)
        {
            float buttonWidth = 140f;
            Rect cancelRect = new Rect(rect.x, rect.y + 6f, buttonWidth, 32f);
            Rect confirmRect = new Rect(rect.xMax - buttonWidth, rect.y + 6f, buttonWidth, 32f);

            if (SimUiStyle.DrawSecondaryButton(cancelRect, SimTranslation.T("RSMF.Common.Cancel")))
                Close();

            bool canConfirm = !thingDefs.NullOrEmpty() && !string.IsNullOrEmpty(selectedCategoryId);
            if (SimUiStyle.DrawPrimaryButton(confirmRect, SimTranslation.T("RSMF.QuickGoodsRegister.Confirm"), canConfirm))
                ConfirmRegistration();
        }

        /// <summary>
        /// 执行注册并关闭窗口，负责根据新增数量给出玩家反馈。
        /// </summary>
        private void ConfirmRegistration()
        {
            int added = QuickGoodsRegistrationUtility.RegisterItemsToCategory(selectedCategoryId, thingDefs);
            RuntimeGoodsCategory category = categories.FirstOrDefault(item => item.categoryId == selectedCategoryId);
            string label = category?.label ?? selectedCategoryId;
            if (added > 0)
            {
                Messages.Message(
                    SimTranslation.T("RSMF.QuickGoodsRegister.Success", added.Named("count"), label.Named("category")),
                    MessageTypeDefOf.PositiveEvent,
                    false);
            }
            else
            {
                Messages.Message(SimTranslation.T("RSMF.QuickGoodsRegister.AllExisting"), MessageTypeDefOf.NeutralEvent, false);
            }

            Close();
        }
    }
}
