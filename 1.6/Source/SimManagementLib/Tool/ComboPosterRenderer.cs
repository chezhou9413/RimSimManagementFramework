using SimManagementLib.Pojo;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    public static class ComboPosterRenderer
    {
        // 主题色
        private static readonly Color BgDark = new Color(0.08f, 0.06f, 0.04f, 1f);
        private static readonly Color AccentGold = new Color(0.95f, 0.78f, 0.20f, 1f);
        private static readonly Color AccentRed = new Color(0.75f, 0.12f, 0.10f, 1f);
        private static readonly Color TextDark = new Color(0.12f, 0.08f, 0.04f, 1f);
        private static readonly Color TextLight = new Color(0.97f, 0.93f, 0.80f, 1f);
        private static readonly Color OverlayDark = new Color(0f, 0f, 0f, 0.55f);

        public static void DrawComboPoster(Rect rect, ComboData combo)
        {
            // 背景
            DrawFilledRect(rect, BgDark);
            DrawDiagonalStripes(rect, new Color(1f, 1f, 1f, 0.025f), 12f);

            // 双层描边
            DrawBorder(rect, AccentGold, 2f);
            DrawBorder(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f),
                new Color(1f, 1f, 1f, 0.12f), 1f);

            if (combo == null || combo.items.NullOrEmpty())
            {
                DrawEmptyState(rect);
                return;
            }

            var sorted = combo.items.OrderByDescending(x => x.count).ToList();

            // ── 顶部金色标题条 ──
            float headerH = Mathf.Max(26f, rect.height * 0.13f);
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerH);
            DrawFilledRect(headerRect, AccentGold);
            DrawCornerAccents(headerRect, TextDark, 7f);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = TextDark;
            Widgets.Label(headerRect, $"★ 套餐 · 共{sorted.Sum(x => x.count)}件 ★");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // ── 底部名称条 ──
            float bottomH = Mathf.Max(34f, rect.height * 0.17f);
            Rect bottomBar = new Rect(rect.x, rect.y + rect.height - bottomH, rect.width, bottomH);
            DrawFilledRect(bottomBar, OverlayDark);
            // 左侧金色装饰线
            DrawFilledRect(new Rect(bottomBar.x + 6f,
                                    bottomBar.y + bottomBar.height * 0.2f,
                                    3f,
                                    bottomBar.height * 0.6f), AccentGold);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = TextLight;
            // ★ 修复3：套餐名超长时截断并加 Tooltip
            float nameLabelX = bottomBar.x + 14f;
            float nameLabelW = bottomBar.width - 18f;
            string nameDisplay = combo.comboName.Truncate(nameLabelW);
            Widgets.Label(new Rect(nameLabelX, bottomBar.y, nameLabelW, bottomH), nameDisplay);
            if (nameDisplay != combo.comboName)
                TooltipHandler.TipRegion(bottomBar, combo.comboName);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // ── 红色腰带（商品名一览）──
            float redBeltH = Mathf.Max(18f, rect.height * 0.09f);
            Rect redBelt = new Rect(rect.x, bottomBar.y - redBeltH, rect.width, redBeltH);
            DrawFilledRect(redBelt, AccentRed);

            string beltText = string.Join("  +  ", sorted.Take(3).Select(x => x.def.label));
            if (sorted.Count > 3) beltText += " …";
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = TextLight;
            Widgets.Label(redBelt, beltText.Truncate(redBelt.width - 8f));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // ── 图标区域 ──
            float iconAreaY = rect.y + headerH + 4f;
            float iconAreaH = redBelt.y - iconAreaY - 4f;
            Rect iconArea = new Rect(rect.x + 4f, iconAreaY, rect.width - 8f, iconAreaH);

            DrawIconLayout(iconArea, sorted);
        }

        // ── 图标排布逻辑 ─────────────────────────────────────────
        private static void DrawIconLayout(Rect area, List<ComboItem> sorted)
        {
            int count = sorted.Count;

            if (count == 1)
            {
                Rect r = CenterRect(area, area.width * 0.62f, area.height * 0.80f);
                DrawIconWithShadow(r, sorted[0].def);
                DrawCountBadge(r, sorted[0].count);
            }
            else if (count == 2)
            {
                float mainW = area.width * 0.48f;
                float subW = area.width * 0.36f;
                Rect mainR = new Rect(area.x + area.width * 0.05f,
                                       area.y + (area.height - mainW) / 2f,
                                       mainW, mainW);
                Rect subR = new Rect(area.x + area.width * 0.52f,
                                       area.y + (area.height - subW) / 2f + area.height * 0.08f,
                                       subW, subW);
                DrawIconWithShadow(subR, sorted[1].def); DrawCountBadge(subR, sorted[1].count);
                DrawIconWithShadow(mainR, sorted[0].def); DrawCountBadge(mainR, sorted[0].count);
            }
            else if (count == 3)
            {
                float mainW = area.width * 0.44f;
                float subW = area.width * 0.30f;
                Rect mainR = CenterRect(
                    new Rect(area.x, area.y + area.height * 0.22f, area.width, area.height * 0.78f),
                    mainW, mainW);
                Rect subL = new Rect(area.x + area.width * 0.03f,
                                       area.y + area.height * 0.04f, subW, subW);
                Rect subR2 = new Rect(area.x + area.width * 0.67f,
                                       area.y + area.height * 0.04f, subW, subW);
                DrawIconWithShadow(subL, sorted[1].def); DrawCountBadge(subL, sorted[1].count);
                DrawIconWithShadow(subR2, sorted[2].def); DrawCountBadge(subR2, sorted[2].count);
                DrawIconWithShadow(mainR, sorted[0].def); DrawCountBadge(mainR, sorted[0].count);
            }
            else
            {
                // ★ 修复2：四品布局——主品左侧垂直居中，右侧动态等分槽位
                float mainW = area.width * 0.48f;
                float subW = area.width * 0.26f;
                float gap = area.width * 0.03f;

                // 主品：左侧垂直居中
                float mainSize = Mathf.Min(mainW, area.height * 0.88f);
                Rect mainR = new Rect(
                    area.x + gap,
                    area.y + (area.height - mainSize) / 2f,
                    mainSize, mainSize);
                DrawIconWithShadow(mainR, sorted[0].def);
                DrawCountBadge(mainR, sorted[0].count);

                // 右侧：最多显示3个副品，动态等分高度
                float rightX = area.x + mainW + gap * 2f;
                float rightW = area.xMax - rightX - gap;
                int subCount = Mathf.Min(sorted.Count - 1, 3);
                // ★ 关键：用等分槽位，每个图标在槽内居中，保证不越界
                float slotH = area.height / subCount;
                float subSize = Mathf.Min(subW, Mathf.Min(rightW, slotH * 0.80f));

                for (int i = 0; i < subCount; i++)
                {
                    float slotY = area.y + i * slotH;
                    Rect sr = new Rect(
                        rightX + (rightW - subSize) / 2f,
                        slotY + (slotH - subSize) / 2f,
                        subSize, subSize);
                    DrawIconWithShadow(sr, sorted[i + 1].def);
                    DrawCountBadge(sr, sorted[i + 1].count);
                }

                // 超过4种：右下角 +N 徽章
                if (sorted.Count > 4)
                {
                    float badgeH = 18f;
                    Rect more = new Rect(rightX, area.yMax - badgeH, rightW, badgeH);
                    DrawFilledRect(more, new Color(0f, 0f, 0f, 0.6f));
                    DrawBorder(more, AccentGold, 1f);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = AccentGold;
                    Widgets.Label(more, $"+{sorted.Count - 4}种");
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;
                }
            }
        }

        // ── 带投影的图标 ─────────────────────────────────────────
        private static void DrawIconWithShadow(Rect r, ThingDef def)
        {
            Rect shadow = new Rect(r.x + 2f, r.y + 2f, r.width, r.height);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            Widgets.ThingIcon(shadow, def);
            GUI.color = Color.white;
            Widgets.ThingIcon(r, def);
        }

        // ── 数量徽章 ─────────────────────────────────────────────
        private static void DrawCountBadge(Rect iconRect, int count)
        {
            if (count <= 1) return;
            float sz = Mathf.Clamp(iconRect.width * 0.32f, 14f, 22f);
            Rect badge = new Rect(iconRect.xMax - sz * 0.7f,
                                   iconRect.yMax - sz * 0.7f,
                                   sz, sz);
            DrawFilledRect(badge, AccentRed);
            DrawBorder(badge, AccentGold, 1f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(badge, $"×{count}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // ── 空状态 ───────────────────────────────────────────────
        private static void DrawEmptyState(Rect rect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.6f, 0.55f, 0.4f, 1f);
            Widgets.Label(rect, "暂无商品");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // ── 工具方法 ─────────────────────────────────────────────
        private static void DrawFilledRect(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, BaseContent.WhiteTex);
            GUI.color = Color.white;
        }

        private static void DrawBorder(Rect r, Color c, float t)
        {
            DrawFilledRect(new Rect(r.x, r.y, r.width, t), c);
            DrawFilledRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            DrawFilledRect(new Rect(r.x, r.y, t, r.height), c);
            DrawFilledRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private static void DrawCornerAccents(Rect r, Color c, float size)
        {
            DrawFilledRect(new Rect(r.x, r.y, size, 2f), c);
            DrawFilledRect(new Rect(r.x, r.y, 2f, size), c);
            DrawFilledRect(new Rect(r.xMax - size, r.y, size, 2f), c);
            DrawFilledRect(new Rect(r.xMax - 2f, r.y, 2f, size), c);
        }

        private static void DrawDiagonalStripes(Rect r, Color c, float spacing)
        {
            GUI.color = c;
            for (float y = r.y; y < r.yMax; y += spacing)
                GUI.DrawTexture(new Rect(r.x, y, r.width, 1f), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }

        private static Rect CenterRect(Rect area, float w, float h) =>
            new Rect(area.x + (area.width - w) * 0.5f,
                     area.y + (area.height - h) * 0.5f,
                     w, h);
    }
}