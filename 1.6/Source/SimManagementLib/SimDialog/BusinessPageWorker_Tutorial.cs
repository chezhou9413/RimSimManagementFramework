using SimManagementLib.Api;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //绘制经商管理教程页，负责读取 XML 教程并提供分页、滚动正文和居中图片展示。
    public class BusinessPageWorker_Tutorial : BusinessManagerPageWorker
    {
        private const float HeaderHeight = 54f;
        private const float FooterHeight = 38f;
        private const float Gap = 8f;
        private const float Padding = 12f;
        private int pageIndex;

        //页面打开时重置教程分页和滚动，负责让玩家每次进入教程都从首页开始阅读。
        public override void OnOpen(ShopUiContext context)
        {
            pageIndex = 0;
            if (context != null)
                context.ScrollPosition = Vector2.zero;
        }

        //绘制教程页主体，负责处理空列表、页码边界和滚动正文区域。
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            List<BusinessTutorialDef> tutorials = GetTutorials();
            if (tutorials.Count == 0)
            {
                ShopUiLayoutUtility.DrawEmptyState(rect, SimTranslation.TOrFallback("RSMF.Business.Tutorial.Empty", "没有配置教程。"));
                return;
            }

            pageIndex = Mathf.Clamp(pageIndex, 0, tutorials.Count - 1);
            BusinessTutorialDef tutorial = tutorials[pageIndex];

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            Rect footerRect = new Rect(rect.x, rect.yMax - FooterHeight, rect.width, FooterHeight);
            Rect bodyRect = new Rect(rect.x, headerRect.yMax + Gap, rect.width, Mathf.Max(1f, rect.height - HeaderHeight - FooterHeight - Gap * 2f));

            DrawHeader(headerRect, tutorial);
            DrawBody(bodyRect, tutorial, context);
            DrawPager(footerRect, context, tutorials.Count);
        }

        //读取教程 Def 列表，负责按 XML 顺序字段和 defName 得到稳定分页顺序。
        private static List<BusinessTutorialDef> GetTutorials()
        {
            return DefDatabase<BusinessTutorialDef>.AllDefsListForReading
                .Where(def => def != null)
                .OrderBy(def => def.order)
                .ThenBy(def => def.defName)
                .ToList();
        }

        //绘制教程页头，负责展示当前教程标题。
        private static void DrawHeader(Rect rect, BusinessTutorialDef tutorial)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = Color.white;
                Widgets.Label(new Rect(rect.x + Padding, rect.y, rect.width - Padding * 2f, rect.height), tutorial.DisplayTitle);
            }
            finally
            {
                RestoreText(oldFont, oldAnchor, oldWordWrap, oldColor);
            }
        }

        //绘制教程正文，负责测量文本高度、居中图片并把内容放入滚动区。
        private static void DrawBody(Rect rect, BusinessTutorialDef tutorial, BusinessManagerUiContext context)
        {
            float viewWidth = Mathf.Max(1f, rect.width - 18f);
            float contentWidth = Mathf.Max(1f, viewWidth - Padding * 2f);
            float viewHeight = CalculateBodyHeight(tutorial, contentWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height + 1f, viewHeight));

            Widgets.BeginScrollView(rect, ref context.ScrollPosition, viewRect);
            float y = Padding;
            y = DrawTextBlock(new Rect(Padding, y, contentWidth, 1f), tutorial.DisplayTextBeforeImage);
            y = DrawImageBlock(new Rect(Padding, y + Gap, contentWidth, 1f), tutorial);
            DrawTextBlock(new Rect(Padding, y + Gap, contentWidth, 1f), tutorial.DisplayTextAfterImage);
            Widgets.EndScrollView();
        }

        //计算正文滚动区高度，负责确保中文多行文本和图片不会被截断。
        private static float CalculateBodyHeight(BusinessTutorialDef tutorial, float width)
        {
            float height = Padding;
            height += MeasureTextHeight(tutorial.DisplayTextBeforeImage, width);
            height += Gap;
            height += MeasureImageHeight(tutorial);
            height += Gap;
            height += MeasureTextHeight(tutorial.DisplayTextAfterImage, width);
            height += Padding;
            return height;
        }

        //绘制文本块，负责按实际文本高度占位并返回下一个 y 坐标。
        private static float DrawTextBlock(Rect rect, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return rect.y;

            float height = MeasureTextHeight(text, rect.width);
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = new Color(0.88f, 0.88f, 0.88f, 1f);
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, height), text);
            }
            finally
            {
                RestoreText(oldFont, oldAnchor, oldWordWrap, oldColor);
            }

            return rect.y + height;
        }

        //测量文本块高度，负责给正文滚动区和实际绘制共用同一套尺寸。
        private static float MeasureTextHeight(string text, float width)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0f;

            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                return Mathf.Max(Text.LineHeightOf(GameFont.Small), Text.CalcHeight(text, width));
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

        //绘制图片块，负责从 XML 路径读取纹理并按比例居中显示。
        private static float DrawImageBlock(Rect rect, BusinessTutorialDef tutorial)
        {
            float height = MeasureImageHeight(tutorial);
            if (height <= 0f)
                return rect.y;

            Rect imageFrame = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawBoxSolid(imageFrame, new Color(0f, 0f, 0f, 0.18f));
            SimUiStyle.DrawBorder(imageFrame, new Color(1f, 1f, 1f, 0.10f));

            Texture2D texture = ContentFinder<Texture2D>.Get(tutorial.imagePath, false);
            if (texture != null)
            {
                Rect imageRect = FitRectCentered(texture, imageFrame.ContractedBy(8f));
                GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, true);
                return rect.y + height;
            }

            DrawMissingImage(imageFrame);
            return rect.y + height;
        }

        //计算图片块高度，负责限制 XML 配置过大时不挤压分页栏。
        private static float MeasureImageHeight(BusinessTutorialDef tutorial)
        {
            if (tutorial == null || string.IsNullOrWhiteSpace(tutorial.imagePath))
                return 0f;

            return Mathf.Clamp(tutorial.imageMaxHeight, 80f, 320f);
        }

        //计算居中等比图片区域，负责避免直接拉伸原图。
        private static Rect FitRectCentered(Texture2D texture, Rect frame)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
                return frame;

            float textureAspect = texture.width / (float)texture.height;
            float frameAspect = frame.width / Mathf.Max(1f, frame.height);
            if (textureAspect >= frameAspect)
            {
                float height = frame.width / textureAspect;
                return new Rect(frame.x, frame.y + (frame.height - height) / 2f, frame.width, height);
            }

            float width = frame.height * textureAspect;
            return new Rect(frame.x + (frame.width - width) / 2f, frame.y, width, frame.height);
        }

        //绘制缺图提示，负责在 XML 图片路径失效时保留可读页面。
        private static void DrawMissingImage(Rect rect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = true;
                GUI.color = new Color(0.72f, 0.72f, 0.72f, 1f);
                Widgets.Label(rect.ContractedBy(8f), SimTranslation.TOrFallback("RSMF.Business.Tutorial.ImageMissing", "图片未找到"));
            }
            finally
            {
                RestoreText(oldFont, oldAnchor, oldWordWrap, oldColor);
            }
        }

        //绘制底部分页栏，负责切换教程页并显示当前页码。
        private void DrawPager(Rect rect, BusinessManagerUiContext context, int pageCount)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            float buttonHeight = Mathf.Max(Text.LineHeightOf(GameFont.Tiny) + 8f, 28f);
            float y = rect.y + (rect.height - buttonHeight) / 2f;
            Rect prevRect = new Rect(rect.x + 8f, y, 92f, buttonHeight);
            Rect nextRect = new Rect(prevRect.xMax + 8f, y, 92f, buttonHeight);
            Rect pageRect = new Rect(nextRect.xMax + 8f, y, rect.width - 216f, buttonHeight);

            if (SimUiStyle.DrawSecondaryButton(prevRect, SimTranslation.TOrFallback("RSMF.Common.PreviousPage", "上一页"), pageIndex > 0, GameFont.Tiny))
            {
                pageIndex--;
                context.ScrollPosition = Vector2.zero;
            }

            if (SimUiStyle.DrawSecondaryButton(nextRect, SimTranslation.TOrFallback("RSMF.Common.NextPage", "下一页"), pageIndex < pageCount - 1, GameFont.Tiny))
            {
                pageIndex++;
                context.ScrollPosition = Vector2.zero;
            }

            DrawPageNumber(pageRect, pageCount);
        }

        //绘制页码文本，负责在底部分页栏中显示当前页和总页数。
        private void DrawPageNumber(Rect rect, int pageCount)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Text.WordWrap = false;
                GUI.color = new Color(0.72f, 0.72f, 0.72f, 1f);
                Widgets.Label(rect, (pageIndex + 1) + "/" + pageCount);
            }
            finally
            {
                RestoreText(oldFont, oldAnchor, oldWordWrap, oldColor);
            }
        }

        //恢复 IMGUI 文本状态，负责避免教程页污染其他页面绘制。
        private static void RestoreText(GameFont font, TextAnchor anchor, bool wordWrap, Color color)
        {
            Text.Font = font;
            Text.Anchor = anchor;
            Text.WordWrap = wordWrap;
            GUI.color = color;
        }
    }
}
