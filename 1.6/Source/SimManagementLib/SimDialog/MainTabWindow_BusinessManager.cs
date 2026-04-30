using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager : MainTabWindow
    {
        private sealed class PageDef
        {
            public string Label;
            public Action<Rect> DrawAction;
        }

        private sealed class ShopViewData
        {
            public Map Map;
            public Zone_Shop Zone;
        }

        private sealed class CustomerViewData
        {
            public Map Map;
            public Pawn Pawn;
            public Zone_Shop ShopZone;
            public LordJob_CustomerVisit Visit;
        }

        private readonly List<PageDef> pages = new List<PageDef>();
        private int curPageIndex;
        private Vector2 shopScrollPos;
        private Vector2 financeScrollPos;
        private Vector2 financeLogScrollPos;
        private Vector2 customerScrollPos;
        private Vector2 staffScrollPos;
        private int financeSubPageIndex;
        private int financeLogPageIndex;

        private static readonly Color CAccent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color CPanel = new Color(0f, 0f, 0f, 0.18f);
        private static readonly Color CPanelAlt = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color CDim = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color COk = new Color(0.35f, 0.80f, 0.45f, 1f);
        private static readonly Color CWarn = new Color(0.95f, 0.72f, 0.25f, 1f);

        public override Vector2 RequestedTabSize => new Vector2(1220f, 720f);

        public override void PreOpen()
        {
            base.PreOpen();
            EnsurePages();
        }

        public override void DoWindowContents(Rect inRect)
        {
            EnsurePages();

            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 42f);
            DrawPageTabs(tabRect);

            Rect bodyRect = new Rect(inRect.x, tabRect.yMax + 6f, inRect.width, inRect.height - tabRect.height - 6f);
            Widgets.DrawBoxSolid(bodyRect, CPanel);
            if (curPageIndex >= 0 && curPageIndex < pages.Count)
            {
                pages[curPageIndex].DrawAction?.Invoke(bodyRect.ContractedBy(10f));
            }
        }

        private void EnsurePages()
        {
            if (!pages.NullOrEmpty()) return;

            pages.Add(new PageDef { Label = "商店管理", DrawAction = DrawShopManagementPage });
            pages.Add(new PageDef { Label = "财务", DrawAction = DrawFinancePage });
            pages.Add(new PageDef { Label = "顾客", DrawAction = DrawCustomerPage });
            pages.Add(new PageDef { Label = "店员", DrawAction = DrawStaffPage });
        }

        private void DrawPageTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.2f));
            float x = rect.x + 8f;
            const float tabH = 30f;
            float y = rect.y + (rect.height - tabH) / 2f;

            for (int i = 0; i < pages.Count; i++)
            {
                PageDef page = pages[i];
                float w = Mathf.Max(110f, Text.CalcSize(page.Label).x + 30f);
                Rect tab = new Rect(x, y, w, tabH);

                bool selected = i == curPageIndex;
                if (SimUiStyle.DrawTabButton(tab, page.Label, selected, CDim))
                {
                    curPageIndex = i;
                    shopScrollPos = Vector2.zero;
                    financeScrollPos = Vector2.zero;
                    financeLogScrollPos = Vector2.zero;
                    customerScrollPos = Vector2.zero;
                    staffScrollPos = Vector2.zero;
                }

                x += w + 8f;
            }
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
