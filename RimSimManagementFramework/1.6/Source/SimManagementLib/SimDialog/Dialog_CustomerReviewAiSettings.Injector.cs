using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_CustomerReviewAiSettings
    {
        private const float InjectorFormatHeight = 48f;
        private const float InjectorFormatGap = 56f;
        private const float InjectorTopHeight = 250f;
        private const float InjectorColumnsHeight = 620f;
        private const float InjectorCustomListHeight = 240f;
        private const float InjectorSectionGap = 12f;
        private const float InjectorBottomPadding = 72f;
        private const float NestedListBottomPadding = 96f;
        private const float NestedScrollWheelSpeed = 28f;

        private Vector2 builtInNodeScroll;
        private Vector2 enabledNodeScroll;
        private Vector2 customNodeScroll;
        private string newCustomNodeLabel = "";
        private string newCustomNodeBody = "";
        private string draggingPromptNodeId = "";

        /// <summary>
        /// 绘制提示词注入器页，负责管理根提示词之外的内置节点、自定义节点和注入优先级。
        /// </summary>
        private void DrawInjectorPage(Rect rect, SimManagementLibSettings settings)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                float y = 0f;
                DrawPromptFormatSelector(new Rect(0f, y, rect.width - 10f, InjectorFormatHeight), settings);
                y += InjectorFormatGap;

                Rect topRect = new Rect(0f, y, rect.width - 10f, InjectorTopHeight);
                float topLeftW = Mathf.Max(360f, topRect.width * 0.44f);
                DrawRootPromptInfo(new Rect(topRect.x, topRect.y, topLeftW, topRect.height), settings);
                DrawCreateCustomNodePanel(new Rect(topRect.x + topLeftW + 10f, topRect.y, topRect.width - topLeftW - 10f, topRect.height), settings);
                y += topRect.height + InjectorSectionGap;

                Rect columnsRect = new Rect(0f, y, rect.width - 10f, InjectorColumnsHeight);
                float leftW = Mathf.Max(340f, columnsRect.width * 0.44f);
                DrawNodeLibrary(new Rect(columnsRect.x, columnsRect.y, leftW, columnsRect.height), settings);
                DrawEnabledNodeOrder(new Rect(columnsRect.x + leftW + 10f, columnsRect.y, columnsRect.width - leftW - 10f, columnsRect.height), settings);
                y += columnsRect.height + InjectorSectionGap;

                DrawCustomNodeListPanel(new Rect(0f, y, rect.width - 10f, InjectorCustomListHeight), settings);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        /// <summary>
        /// 绘制提示词输入结构选择器，负责在普通文本和 XML 风格之间切换。
        /// </summary>
        private void DrawPromptFormatSelector(Rect rect, SimManagementLibSettings settings)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.16f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, 120f, rect.height), SimTranslation.T("RSMF.ReviewSettings.Injector.InputStructure"));
            ResetText();

            float buttonW = 116f;
            Rect xmlRect = new Rect(rect.x + 130f, rect.y + 8f, buttonW, 32f);
            Rect plainRect = new Rect(xmlRect.xMax + 8f, xmlRect.y, buttonW, 32f);
            bool xmlSelected = settings.reviewPromptInputFormat == CustomerReviewPromptInjector.PromptInputFormatXml;
            if (SimUiStyle.DrawTabButton(xmlRect, SimTranslation.T("RSMF.ReviewSettings.Injector.XmlFormat"), xmlSelected, new Color(0.72f, 0.72f, 0.72f, 1f)))
                settings.reviewPromptInputFormat = CustomerReviewPromptInjector.PromptInputFormatXml;
            if (SimUiStyle.DrawTabButton(plainRect, SimTranslation.T("RSMF.ReviewSettings.Injector.PlainTextFormat"), !xmlSelected, new Color(0.72f, 0.72f, 0.72f, 1f)))
                settings.reviewPromptInputFormat = CustomerReviewPromptInjector.PromptInputFormatPlain;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            string hint = xmlSelected ? SimTranslation.T("RSMF.ReviewSettings.Injector.XmlHint") : SimTranslation.T("RSMF.ReviewSettings.Injector.PlainHint");
            Widgets.Label(new Rect(plainRect.xMax + 10f, rect.y, Mathf.Max(0f, rect.xMax - plainRect.xMax - 18f), rect.height), hint);
            ResetText();
        }

        /// <summary>
        /// 计算注入器页面需要的滚动内容高度，负责给底部列表留出不会被裁切的空间。
        /// </summary>
        private static float CalcInjectorContentHeight()
        {
            return InjectorFormatGap
                + InjectorTopHeight
                + InjectorSectionGap
                + InjectorColumnsHeight
                + InjectorSectionGap
                + InjectorCustomListHeight
                + InjectorBottomPadding;
        }

        /// <summary>
        /// 在外层页面滚动前处理内层列表滚轮，负责避免鼠标悬停在列表上时滚轮被外层页面抢走。
        /// </summary>
        private void HandleInjectorNestedScrollWheel(Rect outRect, float viewWidth, SimManagementLibSettings settings)
        {
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.ScrollWheel)
                return;

            Rect screenContentRect = new Rect(outRect.x - scrollPos.x, outRect.y - scrollPos.y, viewWidth, CalcInjectorContentHeight());
            Rect topRect = new Rect(0f, InjectorFormatGap, viewWidth - 10f, InjectorTopHeight);
            float columnsY = topRect.yMax + InjectorSectionGap;
            Rect columnsRect = new Rect(0f, columnsY, viewWidth - 10f, InjectorColumnsHeight);
            float leftW = Mathf.Max(340f, columnsRect.width * 0.44f);
            Rect builtInListRect = new Rect(columnsRect.x + 8f, columnsRect.y + 44f, leftW - 16f, columnsRect.height - 90f);
            Rect enabledListRect = new Rect(columnsRect.x + leftW + 18f, columnsRect.y + 44f, columnsRect.width - leftW - 26f, columnsRect.height - 52f);
            Rect customPanelRect = new Rect(0f, columnsRect.yMax + InjectorSectionGap, viewWidth - 10f, InjectorCustomListHeight);
            Rect customListRect = new Rect(customPanelRect.x + 8f, customPanelRect.y + 44f, customPanelRect.width - 16f, customPanelRect.height - 52f);

            if (TryScrollNestedList(screenContentRect, builtInListRect, CustomerReviewPromptInjector.AllBuiltInNodes.Count * 58f + NestedListBottomPadding, ref builtInNodeScroll))
                ev.Use();
            else if (TryScrollNestedList(screenContentRect, enabledListRect, CustomerReviewPromptInjector.GetOrderedEnabledAllNodeIds(settings).Count * 46f + NestedListBottomPadding, ref enabledNodeScroll))
                ev.Use();
            else
            {
                List<CustomerReviewCustomPromptNode> customNodes = CustomerReviewPromptInjector.ParseCustomNodes(settings.reviewPromptCustomNodes);
                if (TryScrollNestedList(screenContentRect, customListRect, customNodes.Count * 150f + NestedListBottomPadding, ref customNodeScroll))
                    ev.Use();
            }
        }

        /// <summary>
        /// 处理单个内层列表的滚轮，负责按当前鼠标位置滚动指定列表并夹紧滚动范围。
        /// </summary>
        private static bool TryScrollNestedList(Rect screenContentRect, Rect localListRect, float contentHeight, ref Vector2 scroll)
        {
            Rect screenListRect = new Rect(screenContentRect.x + localListRect.x, screenContentRect.y + localListRect.y, localListRect.width, localListRect.height);
            if (!screenListRect.Contains(Event.current.mousePosition))
                return false;

            float maxY = Mathf.Max(0f, contentHeight - localListRect.height);
            if (maxY <= 0f)
                return false;

            scroll.y = Mathf.Clamp(scroll.y + Event.current.delta.y * NestedScrollWheelSpeed, 0f, maxY);
            return true;
        }

        /// <summary>
        /// 绘制根提示词说明，负责提示根节点固定在最前方且通过提示词页编辑。
        /// </summary>
        private void DrawRootPromptInfo(Rect rect, SimManagementLibSettings settings)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.20f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, Mathf.Max(24f, Text.LineHeightOf(GameFont.Small) + 4f)), SimTranslation.T("RSMF.ReviewSettings.Injector.RootPromptNode"));
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Text.WordWrap = true;
            string formatText = settings.reviewPromptInputFormat == CustomerReviewPromptInjector.PromptInputFormatXml
                ? SimTranslation.T("RSMF.ReviewSettings.Injector.RootXmlInfo")
                : SimTranslation.T("RSMF.ReviewSettings.Injector.RootPlainInfo");
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 34f, rect.width - 20f, Mathf.Max(64f, Text.LineHeightOf(GameFont.Tiny) * 4f + 8f)), formatText);
            Rect previewRect = new Rect(rect.x + 10f, rect.yMax - 40f, 140f, 30f);
            if (SimUiStyle.DrawPrimaryButton(previewRect, SimTranslation.T("RSMF.ReviewSettings.Injector.PreviewPrompt"), true, GameFont.Tiny))
                Find.WindowStack.Add(new Dialog_CustomerReviewPromptPreview());
            ResetText();
        }

        /// <summary>
        /// 绘制内置节点库，负责让玩家选择哪些原版或框架资料进入提示词。
        /// </summary>
        private void DrawNodeLibrary(Rect rect, SimManagementLibSettings settings)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            DrawSectionTitle(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 28f), SimTranslation.T("RSMF.ReviewSettings.Injector.AvailableNodes"));

            Rect listRect = new Rect(rect.x + 8f, rect.y + 44f, rect.width - 16f, rect.height - 90f);
            IReadOnlyList<CustomerReviewPromptNodeDef> nodes = CustomerReviewPromptInjector.AllBuiltInNodes;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, Mathf.Max(listRect.height + 1f, nodes.Count * 58f + NestedListBottomPadding));
            Widgets.BeginScrollView(listRect, ref builtInNodeScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                CustomerReviewPromptNodeDef node = nodes[i];
                Rect row = new Rect(0f, y, viewRect.width, 52f);
                DrawBuiltInNodeToggle(row, settings, node);
                y += 58f;
            }
            Widgets.EndScrollView();

            Rect resetRect = new Rect(rect.x + 8f, rect.yMax - 38f, 122f, 30f);
            if (SimUiStyle.DrawSecondaryButton(resetRect, SimTranslation.T("RSMF.ReviewSettings.Injector.ResetNodes"), true, GameFont.Tiny))
            {
                CustomerReviewPromptInjector.Reset(settings);
            }
        }

        /// <summary>
        /// 绘制单个内置节点开关，负责启用或禁用节点。
        /// </summary>
        private void DrawBuiltInNodeToggle(Rect rect, SimManagementLibSettings settings, CustomerReviewPromptNodeDef node)
        {
            bool enabled = CustomerReviewPromptInjector.IsBuiltInNodeEnabled(settings, node.id);
            Widgets.DrawBoxSolid(rect, enabled ? new Color(0.25f, 0.65f, 0.85f, 0.14f) : new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
            Widgets.Checkbox(rect.x + 8f, rect.y + 14f, ref enabled, 24f);
            CustomerReviewPromptInjector.SetBuiltInNodeEnabled(settings, node.id, enabled);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 40f, rect.y + 7f, rect.width - 48f, Mathf.Max(20f, Text.LineHeightOf(GameFont.Tiny) + 2f)), node.label);
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Text.WordWrap = true;
            Widgets.Label(new Rect(rect.x + 40f, rect.y + 27f, rect.width - 48f, Mathf.Max(20f, Text.LineHeightOf(GameFont.Tiny) + 2f)), node.description);
            ResetText();

            Event ev = Event.current;
            if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
            {
                draggingPromptNodeId = node.id;
            }
        }

        /// <summary>
        /// 绘制已启用节点优先级列表，负责支持上移、下移和鼠标拖拽排序。
        /// </summary>
        private void DrawEnabledNodeOrder(Rect rect, SimManagementLibSettings settings)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            DrawSectionTitle(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 28f), SimTranslation.T("RSMF.ReviewSettings.Injector.EnabledNodeOrder"));

            List<string> order = CustomerReviewPromptInjector.GetOrderedEnabledAllNodeIds(settings);
            Rect listRect = new Rect(rect.x + 8f, rect.y + 44f, rect.width - 16f, rect.height - 52f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, Mathf.Max(listRect.height + 1f, order.Count * 46f + NestedListBottomPadding));
            Widgets.BeginScrollView(listRect, ref enabledNodeScroll, viewRect);
            float y = 0f;
            for (int i = 0; i < order.Count; i++)
            {
                Rect row = new Rect(0f, y, viewRect.width, 40f);
                DrawEnabledNodeRow(row, settings, order[i], i, order.Count);
                y += 46f;
            }
            HandleNodeDropOnList(new Rect(0f, y, viewRect.width, Mathf.Max(0f, viewRect.height - y)), settings, order.Count);

            if (order.Count == 0)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
                Widgets.Label(new Rect(0f, 0f, viewRect.width, 80f), SimTranslation.T("RSMF.ReviewSettings.Injector.NoEnabledNodes"));
                ResetText();
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制已启用节点行，负责提供排序按钮和拖拽落点。
        /// </summary>
        private void DrawEnabledNodeRow(Rect rect, SimManagementLibSettings settings, string id, int index, int count)
        {
            bool dragging = draggingPromptNodeId == id;
            Widgets.DrawBoxSolid(rect, dragging ? new Color(0.25f, 0.65f, 0.85f, 0.22f) : new Color(1f, 1f, 1f, 0.035f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 8f, rect.y, 38f, rect.height), (index + 1).ToString());
            Widgets.Label(new Rect(rect.x + 48f, rect.y, rect.width - 202f, rect.height), GetNodeLabel(settings, id));
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(rect.xMax - 176f, rect.y, 38f, rect.height), SimTranslation.T("RSMF.ReviewSettings.Injector.Drag"));
            ResetText();

            Rect upRect = new Rect(rect.xMax - 132f, rect.y + 5f, 34f, 30f);
            Rect downRect = new Rect(upRect.xMax + 6f, upRect.y, 40f, 30f);
            Rect removeRect = new Rect(downRect.xMax + 6f, upRect.y, 52f, 30f);
            if (SimUiStyle.DrawSecondaryButton(upRect, "↑", index > 0, GameFont.Tiny))
                CustomerReviewPromptInjector.MoveBuiltInNode(settings, id, index - 1);
            if (SimUiStyle.DrawSecondaryButton(downRect, "↓", index < count - 1, GameFont.Tiny))
                CustomerReviewPromptInjector.MoveBuiltInNode(settings, id, index + 1);
            if (SimUiStyle.DrawSecondaryButton(removeRect, SimTranslation.T("RSMF.ReviewSettings.Injector.Disable"), true, GameFont.Tiny))
                DisableNode(settings, id);

            HandleNodeDrag(rect, settings, id, index);
        }

        /// <summary>
        /// 处理节点拖拽排序，负责在鼠标释放时把拖动节点移动到目标行。
        /// </summary>
        private void HandleNodeDrag(Rect rect, SimManagementLibSettings settings, string id, int index)
        {
            Event ev = Event.current;
            if (ev == null) return;

            if (ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
            {
                draggingPromptNodeId = id;
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                if (!string.IsNullOrEmpty(draggingPromptNodeId) && rect.Contains(ev.mousePosition))
                {
                    EnableNode(settings, draggingPromptNodeId);
                    CustomerReviewPromptInjector.MoveBuiltInNode(settings, draggingPromptNodeId, index);
                }
                draggingPromptNodeId = "";
            }
        }

        /// <summary>
        /// 处理拖到启用列表空白处的节点，负责把新节点启用并放到列表末尾。
        /// </summary>
        private void HandleNodeDropOnList(Rect rect, SimManagementLibSettings settings, int fallbackIndex)
        {
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.MouseUp || ev.button != 0)
                return;
            if (string.IsNullOrEmpty(draggingPromptNodeId) || !rect.Contains(ev.mousePosition))
                return;

            EnableNode(settings, draggingPromptNodeId);
            CustomerReviewPromptInjector.MoveBuiltInNode(settings, draggingPromptNodeId, fallbackIndex);
            draggingPromptNodeId = "";
        }

        /// <summary>
        /// 绘制上方新增自定义节点面板，负责让玩家不用滚到底部即可添加节点。
        /// </summary>
        private void DrawCreateCustomNodePanel(Rect rect, SimManagementLibSettings settings)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            DrawSectionTitle(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 28f), SimTranslation.T("RSMF.ReviewSettings.Injector.NewCustomNode"));

            Rect formRect = new Rect(rect.x + 8f, rect.y + 44f, rect.width - 16f, rect.height - 52f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(formRect.x, formRect.y, formRect.width, 22f), SimTranslation.T("RSMF.ReviewSettings.Injector.NewNodeName"));
            GUI.color = Color.white;
            newCustomNodeLabel = Widgets.TextField(new Rect(formRect.x, formRect.y + 24f, formRect.width, 30f), newCustomNodeLabel ?? "");
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            Widgets.Label(new Rect(formRect.x, formRect.y + 62f, formRect.width, 22f), SimTranslation.T("RSMF.ReviewSettings.Injector.NewNodeBody"));
            GUI.color = Color.white;
            Text.WordWrap = true;
            Rect addRect = new Rect(formRect.x, formRect.yMax - 30f, 120f, 30f);
            Rect bodyRect = new Rect(formRect.x, formRect.y + 86f, formRect.width, Mathf.Max(40f, addRect.y - formRect.y - 94f));
            newCustomNodeBody = Widgets.TextArea(bodyRect, newCustomNodeBody ?? "");
            if (SimUiStyle.DrawPrimaryButton(addRect, SimTranslation.T("RSMF.ReviewSettings.Injector.AddNode"), !string.IsNullOrWhiteSpace(newCustomNodeBody), GameFont.Tiny))
            {
                List<CustomerReviewCustomPromptNode> nodes = CustomerReviewPromptInjector.ParseCustomNodes(settings.reviewPromptCustomNodes);
                nodes.Add(new CustomerReviewCustomPromptNode
                {
                    id = "custom_" + Guid.NewGuid().ToString("N"),
                    label = string.IsNullOrWhiteSpace(newCustomNodeLabel) ? SimTranslation.T("RSMF.ReviewSettings.Injector.CustomNode") : newCustomNodeLabel.Trim(),
                    body = newCustomNodeBody,
                    enabled = true
                });
                settings.reviewPromptCustomNodes = CustomerReviewPromptInjector.SerializeCustomNodes(nodes);
                newCustomNodeLabel = "";
                newCustomNodeBody = "";
            }
            ResetText();
        }

        /// <summary>
        /// 绘制已有自定义节点列表面板，负责编辑、删除和拖拽已有自定义节点。
        /// </summary>
        private void DrawCustomNodeListPanel(Rect rect, SimManagementLibSettings settings)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.18f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            DrawSectionTitle(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 28f), SimTranslation.T("RSMF.ReviewSettings.Injector.ExistingCustomNodes"));
            DrawCustomNodeList(new Rect(rect.x + 8f, rect.y + 44f, rect.width - 16f, rect.height - 52f), settings);
        }

        /// <summary>
        /// 绘制自定义节点列表，负责编辑已有节点和保存变更。
        /// </summary>
        private void DrawCustomNodeList(Rect rect, SimManagementLibSettings settings)
        {
            List<CustomerReviewCustomPromptNode> nodes = CustomerReviewPromptInjector.ParseCustomNodes(settings.reviewPromptCustomNodes);
            float rowH = 150f;
            Rect viewRect = new Rect(0f, 0f, rect.width - 18f, Mathf.Max(rect.height + 1f, nodes.Count * rowH + NestedListBottomPadding));
            Widgets.BeginScrollView(rect, ref customNodeScroll, viewRect);
            bool changed = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                Rect row = new Rect(0f, i * rowH, viewRect.width, rowH - 8f);
                changed |= DrawCustomNodeRow(row, settings, nodes, i);
            }
            Widgets.EndScrollView();
            if (changed)
                settings.reviewPromptCustomNodes = CustomerReviewPromptInjector.SerializeCustomNodes(nodes);
        }

        /// <summary>
        /// 绘制单个自定义节点，负责编辑启用状态、名称、正文和删除操作。
        /// </summary>
        private bool DrawCustomNodeRow(Rect rect, SimManagementLibSettings settings, List<CustomerReviewCustomPromptNode> nodes, int index)
        {
            CustomerReviewCustomPromptNode node = nodes[index];
            bool changed = false;
            Widgets.DrawBoxSolid(rect, node.enabled ? new Color(0.25f, 0.65f, 0.85f, 0.10f) : new Color(1f, 1f, 1f, 0.025f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            bool enabled = node.enabled;
            Rect checkRect = new Rect(rect.x + 8f, rect.y + 8f, 24f, 24f);
            Rect deleteRect = new Rect(rect.xMax - 96f, rect.y + 6f, 88f, 28f);
            Rect labelRect = new Rect(checkRect.xMax + 8f, rect.y + 6f, Mathf.Max(80f, deleteRect.x - checkRect.xMax - 16f), 28f);
            Widgets.Checkbox(checkRect.x, checkRect.y, ref enabled, checkRect.width);
            if (enabled != node.enabled)
            {
                node.enabled = enabled;
                changed = true;
            }

            string label = Widgets.TextField(labelRect, node.label ?? "");
            if (label != node.label)
            {
                node.label = label;
                changed = true;
            }

            Rect bodyRect = new Rect(rect.x + 8f, rect.y + 40f, rect.width - 16f, rect.height - 48f);
            string body = Widgets.TextArea(bodyRect, node.body ?? "");
            if (body != node.body)
            {
                node.body = body;
                changed = true;
            }

            if (SimUiStyle.DrawDangerButton(deleteRect, SimTranslation.T("RSMF.Common.Delete"), true, GameFont.Tiny))
            {
                nodes.RemoveAt(index);
                changed = true;
            }

            Event ev = Event.current;
            if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
            {
                draggingPromptNodeId = node.id;
            }
            return changed;
        }

        /// <summary>
        /// 绘制分区标题，负责统一注入器页面标题样式。
        /// </summary>
        private void DrawSectionTitle(Rect rect, string title)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(rect, title);
            ResetText();
        }

        /// <summary>
        /// 获取节点显示名称，负责让排序列表同时支持内置和自定义节点。
        /// </summary>
        private string GetNodeLabel(SimManagementLibSettings settings, string id)
        {
            CustomerReviewPromptNodeDef builtIn = CustomerReviewPromptInjector.AllBuiltInNodes.FirstOrDefault(n => n.id == id);
            if (builtIn != null) return builtIn.label;
            CustomerReviewCustomPromptNode custom = CustomerReviewPromptInjector.ParseCustomNodes(settings.reviewPromptCustomNodes).FirstOrDefault(n => n.id == id);
            return custom != null && !string.IsNullOrWhiteSpace(custom.label) ? custom.label : id;
        }

        /// <summary>
        /// 停用节点，负责区分内置节点和自定义节点的保存位置。
        /// </summary>
        private void DisableNode(SimManagementLibSettings settings, string id)
        {
            if (CustomerReviewPromptInjector.AllBuiltInNodes.Any(n => n.id == id))
            {
                CustomerReviewPromptInjector.SetBuiltInNodeEnabled(settings, id, false);
                return;
            }

            List<CustomerReviewCustomPromptNode> nodes = CustomerReviewPromptInjector.ParseCustomNodes(settings.reviewPromptCustomNodes);
            CustomerReviewCustomPromptNode custom = nodes.FirstOrDefault(n => n.id == id);
            if (custom != null)
            {
                custom.enabled = false;
                settings.reviewPromptCustomNodes = CustomerReviewPromptInjector.SerializeCustomNodes(nodes);
            }
        }

        /// <summary>
        /// 启用节点，负责让从左侧或自定义列表拖入优先级区域的节点进入注入队列。
        /// </summary>
        private void EnableNode(SimManagementLibSettings settings, string id)
        {
            if (CustomerReviewPromptInjector.AllBuiltInNodes.Any(n => n.id == id))
            {
                CustomerReviewPromptInjector.SetBuiltInNodeEnabled(settings, id, true);
                return;
            }

            List<CustomerReviewCustomPromptNode> nodes = CustomerReviewPromptInjector.ParseCustomNodes(settings.reviewPromptCustomNodes);
            CustomerReviewCustomPromptNode custom = nodes.FirstOrDefault(n => n.id == id);
            if (custom != null)
            {
                custom.enabled = true;
                settings.reviewPromptCustomNodes = CustomerReviewPromptInjector.SerializeCustomNodes(nodes);
            }
        }
    }
}
