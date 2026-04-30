using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    internal static class SimUiStyle
    {
        private static readonly Color PrimaryFill = new Color(0.25f, 0.65f, 0.85f, 0.24f);
        private static readonly Color PrimaryHover = new Color(0.25f, 0.65f, 0.85f, 0.34f);
        private static readonly Color PrimaryBorder = new Color(0.25f, 0.65f, 0.85f, 0.95f);

        private static readonly Color SecondaryFill = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color SecondaryHover = new Color(1f, 1f, 1f, 0.11f);
        private static readonly Color SecondaryBorder = new Color(1f, 1f, 1f, 0.18f);

        private static readonly Color DangerFill = new Color(0.90f, 0.35f, 0.35f, 0.14f);
        private static readonly Color DangerHover = new Color(0.90f, 0.35f, 0.35f, 0.22f);
        private static readonly Color DangerBorder = new Color(0.90f, 0.35f, 0.35f, 0.45f);

        private static readonly Color TabSelectedFill = new Color(0.25f, 0.65f, 0.85f, 0.20f);
        private static readonly Color TabNormalFill = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color TabHoverFill = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color TabSelectedBorder = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color TabNormalBorder = new Color(1f, 1f, 1f, 0.14f);

        private static readonly Color DisabledFill = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color DisabledBorder = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color DisabledText = new Color(0.55f, 0.55f, 0.55f, 1f);

        public static bool DrawPrimaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return DrawButton(rect, label, PrimaryFill, PrimaryHover, PrimaryBorder, Color.white, enabled, font);
        }

        public static bool DrawSecondaryButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return DrawButton(rect, label, SecondaryFill, SecondaryHover, SecondaryBorder, Color.white, enabled, font);
        }

        public static bool DrawDangerButton(Rect rect, string label, bool enabled = true, GameFont font = GameFont.Small)
        {
            return DrawButton(rect, label, DangerFill, DangerHover, DangerBorder, new Color(1f, 0.80f, 0.80f, 1f), enabled, font);
        }

        public static bool DrawTabButton(Rect rect, string label, bool selected, Color normalTextColor)
        {
            bool mouseOver = Mouse.IsOver(rect);
            Color fill = selected ? TabSelectedFill : (mouseOver ? TabHoverFill : TabNormalFill);
            Color border = selected ? TabSelectedBorder : TabNormalBorder;

            Widgets.DrawBoxSolid(rect, fill);
            DrawBorder(rect, border, 1f);

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            GUI.color = selected ? Color.white : normalTextColor;
            Widgets.Label(rect, label);
            ResetText();

            return !selected && Widgets.ButtonInvisible(rect, false);
        }

        public static void DrawBorder(Rect rect, Color color, float thickness = 1f)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static bool DrawButton(
            Rect rect,
            string label,
            Color fillColor,
            Color hoverColor,
            Color borderColor,
            Color textColor,
            bool enabled,
            GameFont font)
        {
            bool mouseOver = Mouse.IsOver(rect);
            Color fill = !enabled ? DisabledFill : (mouseOver ? hoverColor : fillColor);
            Color border = enabled ? borderColor : DisabledBorder;
            Color text = enabled ? textColor : DisabledText;

            Widgets.DrawBoxSolid(rect, fill);
            DrawBorder(rect, border, 1f);

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = font;
            GUI.color = text;
            Widgets.Label(rect, label);
            ResetText();

            return enabled && Widgets.ButtonInvisible(rect, false);
        }

        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
