using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 经商管理页面管理窗口，负责集中调整顶部页签的显示状态和排列顺序。
    /// </summary>
    public partial class Dialog_BusinessPageManager : Window
    {
        /// <summary>
        /// 页面编辑草稿，负责在玩家点击应用前保存可撤销的页面配置。
        /// </summary>
        private sealed class PageDraft
        {
            public string DefName;
            public string Label;
            public string Description;
            public int RegisteredOrder;
            public bool Visible;
        }

        private const float HeaderHeight = 58f;
        private const float ToolbarHeight = 38f;
        private const float FooterHeight = 50f;
        private const float Gap = 10f;
        private const float RowHeight = 62f;
        private const float InspectorWidth = 280f;
        private const float CheckSize = 24f;

        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.18f);
        private static readonly Color RowAltBg = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color SelectedBg = new Color(0.25f, 0.65f, 0.85f, 0.16f);
        private static readonly Color DimText = new Color(0.70f, 0.72f, 0.76f, 1f);
        private static readonly Color WarnText = new Color(0.95f, 0.72f, 0.25f, 1f);

        private readonly List<PageDraft> drafts = new List<PageDraft>();
        private readonly Action<List<string>, HashSet<string>> applyAction;
        private Vector2 listScroll;
        private int selectedIndex;

        public override Vector2 InitialSize => new Vector2(920f, 700f);

        /// <summary>
        /// 初始化页面管理窗口，负责从当前注册页面复制可编辑草稿。
        /// </summary>
        public Dialog_BusinessPageManager(IReadOnlyList<ShopUiPageDef> pages, Action<List<string>, HashSet<string>> applyAction)
        {
            this.applyAction = applyAction;
            closeOnCancel = true;
            doCloseX = true;
            absorbInputAroundWindow = false;
            BuildDrafts(pages);
        }

        /// <summary>
        /// 绘制页面管理窗口内容，负责维持稳定分区并恢复 IMGUI 全局状态。
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
                Rect toolbarRect = new Rect(inRect.x, headerRect.yMax + Gap, inRect.width, ToolbarHeight);
                Rect footerRect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);
                Rect bodyRect = new Rect(inRect.x, toolbarRect.yMax + Gap, inRect.width, footerRect.y - toolbarRect.yMax - Gap * 2f);

                DrawHeader(headerRect);
                DrawToolbar(toolbarRect);
                DrawBody(bodyRect);
                DrawFooter(footerRect);
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
        /// 从注册页面构建草稿列表，负责过滤无效 DefName 并继承当前隐藏状态。
        /// </summary>
        private void BuildDrafts(IReadOnlyList<ShopUiPageDef> pages)
        {
            HashSet<string> hidden = new HashSet<string>(SimManagementLibMod.Settings?.businessManagerHiddenPages ?? new List<string>());
            HashSet<string> seen = new HashSet<string>();
            if (pages == null)
                return;

            for (int i = 0; i < pages.Count; i++)
            {
                ShopUiPageDef page = pages[i];
                if (page == null || string.IsNullOrWhiteSpace(page.defName) || !seen.Add(page.defName))
                    continue;

                drafts.Add(new PageDraft
                {
                    DefName = page.defName,
                    Label = page.DisplayLabel,
                    Description = page.DisplayDescription,
                    RegisteredOrder = page.order,
                    Visible = !hidden.Contains(page.defName)
                });
            }

            if (VisibleCount == 0 && drafts.Count > 0)
                drafts[0].Visible = true;
        }

        /// <summary>
        /// 恢复默认页面配置，负责按注册顺序排列并显示所有页面。
        /// </summary>
        private void RestoreDefault()
        {
            drafts.Sort((left, right) =>
            {
                int orderCompare = left.RegisteredOrder.CompareTo(right.RegisteredOrder);
                return orderCompare != 0 ? orderCompare : string.Compare(left.DefName, right.DefName, StringComparison.Ordinal);
            });
            for (int i = 0; i < drafts.Count; i++)
                drafts[i].Visible = true;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, drafts.Count - 1));
        }

        /// <summary>
        /// 移动页面草稿位置，负责维持选中行跟随移动后的页面。
        /// </summary>
        private void MoveDraft(int from, int to)
        {
            if (from < 0 || from >= drafts.Count || to < 0 || to >= drafts.Count || from == to)
                return;

            PageDraft draft = drafts[from];
            drafts.RemoveAt(from);
            drafts.Insert(to, draft);
            selectedIndex = to;
        }

        /// <summary>
        /// 应用草稿配置并关闭窗口，负责把顺序和隐藏状态交回主窗口持久化。
        /// </summary>
        private void ApplyAndClose()
        {
            List<string> order = drafts.Select(draft => draft.DefName).ToList();
            HashSet<string> hidden = new HashSet<string>(drafts.Where(draft => !draft.Visible).Select(draft => draft.DefName));
            applyAction?.Invoke(order, hidden);
            Close();
        }

        private int VisibleCount => drafts.Count(draft => draft.Visible);
    }
}
