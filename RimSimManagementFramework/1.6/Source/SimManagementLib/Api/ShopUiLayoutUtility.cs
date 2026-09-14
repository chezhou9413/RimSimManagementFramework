using UnityEngine;
using Verse;

namespace SimManagementLib.Api
{
    //提供商店 UI Worker 可复用的布局工具，负责减少中文裁切和 GUI 状态泄露。
    public static class ShopUiLayoutUtility
    {
        //绘制标题行，负责按字体真实高度预留文本空间。
        public static float DrawTitle(Rect rect, string title, string subtitle = null)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                float titleH = Text.LineHeightOf(GameFont.Medium) + 4f;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = Color.white;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, titleH), title ?? "");

                if (!string.IsNullOrEmpty(subtitle))
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.WordWrap = true;
                    float subH = Mathf.Max(Text.LineHeightOf(GameFont.Tiny), Text.CalcHeight(subtitle, rect.width));
                    GUI.color = new Color(0.78f, 0.78f, 0.78f, 1f);
                    Widgets.Label(new Rect(rect.x, rect.y + titleH + 2f, rect.width, subH), subtitle);
                    return titleH + subH + 8f;
                }

                return titleH + 4f;
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        //绘制空状态文本，负责居中显示并恢复 GUI 状态。
        public static void DrawEmptyState(Rect rect, string text)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = true;
                GUI.color = new Color(0.72f, 0.72f, 0.72f, 1f);
                float h = Mathf.Max(Text.LineHeightOf(GameFont.Small) + 6f, Text.CalcHeight(text ?? "", rect.width));
                Widgets.Label(new Rect(rect.x, rect.center.y - h / 2f, rect.width, h), text ?? "");
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        //绘制异常状态，负责遮住失败页面的残留内容并隔离外部 Worker 错误。
        public static void DrawErrorState(Rect rect, string title, string detail)
        {
            Color oldColor = GUI.color;
            try
            {
                GUI.color = Color.white;
                Widgets.DrawBoxSolid(rect, new Color(0.16f, 0.08f, 0.09f, 1f));
                DrawTitle(rect.ContractedBy(10f), title, detail);
            }
            finally { GUI.color = oldColor; }
        }

        //绘制按钮行背景，负责给外部页面提供一致的工具栏底色。
        public static Rect DrawButtonRow(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
            return rect.ContractedBy(6f);
        }
    }
}
