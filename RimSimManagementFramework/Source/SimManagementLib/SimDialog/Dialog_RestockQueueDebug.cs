using SimManagementLib.Tool;
using RimWorld;
using SimManagementLib.Debug;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //补货队列调试窗口，职责是可视化当前地图的补货队列并导出排查日志。
    public sealed class Dialog_RestockQueueDebug : Window
    {
        private const float HeaderHeight = 72f;
        private const float FooterHeight = 46f;
        private const float Gap = 8f;
        private const int MaxRowsPerSection = 120;
        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color Border = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f, 1f);
        private static readonly Color ReadyText = new Color(0.58f, 0.92f, 0.70f, 1f);
        private static readonly Color BlockedText = new Color(1f, 0.72f, 0.48f, 1f);
        private static readonly Color DirtyText = new Color(0.68f, 0.82f, 1f, 1f);
        private readonly Map map;
        private Vector2 scrollPosition;
        private RestockQueueDebugSnapshot snapshot;
        private string lastExportPath = "";

        public override Vector2 InitialSize => new Vector2(980f, 720f);

        //创建补货队列调试窗口，职责是绑定当前地图并初始化窗口行为。
        public Dialog_RestockQueueDebug(Map map)
        {
            this.map = map;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
            RefreshSnapshot();
        }

        //绘制窗口内容，职责是组织头部、滚动队列列表和底部操作。
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
                Widgets.DrawBoxSolid(inRect, WindowBg);

                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
                Rect footerRect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);
                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + Gap, inRect.width, inRect.height - HeaderHeight - FooterHeight - Gap * 2f);

                DrawHeader(headerRect);
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

        //刷新队列快照，职责是从地图组件读取最新状态。
        private void RefreshSnapshot()
        {
            snapshot = map?.GetComponent<MapComponent_RestockTaskQueue>()?.CreateDebugSnapshot();
        }

        //绘制窗口头部，职责是展示地图和队列总体状态。
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            DrawBorder(rect);
            float closeSafeWidth = 48f;
            Rect titleRect = new Rect(rect.x + 12f, rect.y + 8f, rect.width - closeSafeWidth - 24f, Text.LineHeightOf(GameFont.Medium) + 4f);
            Text.Font = GameFont.Medium;
            GUI.color = Color.white;
            Widgets.Label(titleRect, SimTranslation.T("RSMF.RestockPanel.Title"));

            Text.Font = GameFont.Small;
            GUI.color = MutedText;
            string summary = map == null
                ? SimTranslation.T("RSMF.RestockPanel.NoMap")
                : SimTranslation.T("RSMF.RestockPanel.Summary", (map.uniqueID).Named("map"), (Find.TickManager?.TicksGame ?? 0).Named("tick"), (string.IsNullOrEmpty(lastExportPath) ? SimTranslation.T("RSMF.RestockPanel.NotExported") : lastExportPath).Named("path"));
            SimManagementLib.Api.ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 12f, titleRect.yMax + 6f, rect.width - 24f, Text.LineHeightOf(GameFont.Small) + 6f), summary, MutedText);
        }

        //绘制主体滚动区，职责是展示 dirty、ready 和 blocked 队列。
        private void DrawBody(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            DrawBorder(rect);
            Rect inner = rect.ContractedBy(10f);
            float viewWidth = Mathf.Max(100f, inner.width - 18f);
            float viewHeight = CalculateViewHeight(viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(inner.height + 1f, viewHeight));

            Widgets.BeginScrollView(inner, ref scrollPosition, viewRect);
            try
            {
                float y = 0f;
                y += DrawSummaryCards(new Rect(0f, y, viewWidth, SummaryCardHeight(viewWidth)));
                y += Gap;
                y += DrawDirtySection(new Rect(0f, y, viewWidth, 1f));
                y += Gap;
                y += DrawTaskSection(new Rect(0f, y, viewWidth, 1f), SimTranslation.T("RSMF.RestockPanel.Ready"), snapshot?.ReadyTasks, ReadyText);
                y += Gap;
                DrawTaskSection(new Rect(0f, y, viewWidth, 1f), SimTranslation.T("RSMF.RestockPanel.Blocked"), snapshot?.BlockedTasks, BlockedText);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        //绘制底部操作区，职责是提供刷新、重建、复制和导出按钮。
        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            DrawBorder(rect);
            float buttonHeight = Mathf.Max(34f, Text.LineHeightOf(GameFont.Small) + 12f);
            float y = rect.y + (rect.height - buttonHeight) / 2f;
            float buttonWidth = Mathf.Max(110f, (rect.width - Gap * 5f) / 5f);
            Rect refreshRect = new Rect(rect.x, y, buttonWidth, buttonHeight);
            Rect rebuildRect = new Rect(refreshRect.xMax + Gap, y, buttonWidth, buttonHeight);
            Rect copyRect = new Rect(rebuildRect.xMax + Gap, y, buttonWidth, buttonHeight);
            Rect exportRect = new Rect(copyRect.xMax + Gap, y, buttonWidth, buttonHeight);
            Rect closeRect = new Rect(exportRect.xMax + Gap, y, buttonWidth, buttonHeight);

            if (Widgets.ButtonText(refreshRect, SimTranslation.T("RSMF.RestockPanel.Refresh")))
                RefreshSnapshot();
            if (Widgets.ButtonText(rebuildRect, SimTranslation.T("RSMF.RestockPanel.Rebuild")))
            {
                map?.GetComponent<MapComponent_RestockTaskQueue>()?.ResetAndRebuildAll("调试面板重建队列");
                RefreshSnapshot();
            }
            if (Widgets.ButtonText(copyRect, SimTranslation.T("RSMF.RestockPanel.Copy")))
                CopyReport();
            if (Widgets.ButtonText(exportRect, SimTranslation.T("RSMF.RestockPanel.Export")))
                ExportReport();
            if (Widgets.ButtonText(closeRect, SimTranslation.T("RSMF.RestockPanel.Close")))
                Close();
        }

        //绘制概要卡片，职责是快速展示队列数量和最近处理信息。
        private float DrawSummaryCards(Rect rect)
        {
            float cardGap = 8f;
            float cardWidth = (rect.width - cardGap * 3f) / 4f;
            string[] values = SummaryValues();
            string[] titles = { SimTranslation.T("RSMF.RestockPanel.RequestsTitle"), SimTranslation.T("RSMF.RestockPanel.StateTitle"),
                SimTranslation.T("RSMF.RestockPanel.LeaseTitle"), SimTranslation.T("RSMF.RestockPanel.BudgetTitle") };
            Color[] colors = { DirtyText, ReadyText, BlockedText, MutedText };
            for (int i = 0; i < values.Length; i++)
                DrawSummaryCard(new Rect(rect.x + (cardWidth + cardGap) * i, rect.y, cardWidth, rect.height), titles[i], values[i], colors[i]);
            return rect.height;
        }

        //取得概要译文，职责是让卡片测量与实际绘制共享完整参数和换行。
        private string[] SummaryValues()
        {
            return new[]
            {
                SimTranslation.T("RSMF.RestockPanel.Requests", (snapshot?.BulkRequestCount ?? 0).Named("bulk"), (snapshot?.UniqueRequestCount ?? 0).Named("unique")),
                SimTranslation.T("RSMF.RestockPanel.State", (snapshot?.ReadyCount ?? 0).Named("ready"), (snapshot?.BlockedCount ?? 0).Named("blocked")),
                SimTranslation.T("RSMF.RestockPanel.Leases", (snapshot?.LeaseCount ?? 0).Named("count"), (snapshot?.OldestRequestAge ?? 0).Named("age")),
                SimTranslation.T("RSMF.RestockPanel.Limits", (snapshot?.DemandChecksUsed ?? 0).Named("demand"), (snapshot?.DispatchAttemptsUsed ?? 0).Named("dispatch"), (snapshot?.IdlePawnChecksUsed ?? 0).Named("staff"), (snapshot?.ReachQueriesUsed ?? 0).Named("reach"))
            };
        }

        //按当前语言测量概要卡片，职责是容纳多行译文并保持滚动内容高度一致。
        private float SummaryCardHeight(float width)
        {
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float contentWidth = Mathf.Max(1f, (width - 24f) / 4f - 16f);
            float valueHeight = Text.LineHeightOf(GameFont.Small);
            foreach (string value in SummaryValues())
                valueHeight = Mathf.Max(valueHeight, Text.CalcHeight(value, contentWidth));
            return Text.LineHeightOf(GameFont.Tiny) + valueHeight + 22f;
        }

        //绘制单个概要卡片，职责是保留标题行并完整显示按译文测量的数值。
        private static void DrawSummaryCard(Rect rect, string title, string value, Color valueColor)
        {
            Widgets.DrawBoxSolid(rect, RowBg);
            DrawBorder(rect);
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            SimManagementLib.Api.ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, Text.LineHeightOf(GameFont.Tiny) + 2f), title, MutedText, GameFont.Tiny);
            Text.Font = GameFont.Small;
            GUI.color = valueColor;
            float valueY = rect.y + Text.LineHeightOf(GameFont.Tiny) + 12f;
            Text.WordWrap = true;
            Widgets.Label(new Rect(rect.x + 8f, valueY, rect.width - 16f, rect.yMax - valueY - 6f), value);
        }

        //绘制 dirty 队列段落，职责是展示等待处理的货柜和商品键。
        private float DrawDirtySection(Rect rect)
        {
            List<RestockTaskKey> dirty = snapshot?.DirtyTasks;
            float y = rect.y;
            y += DrawSectionHeader(new Rect(rect.x, y, rect.width, 1f), SimTranslation.T("RSMF.RestockPanel.Dirty"), dirty?.Count ?? 0, DirtyText);
            int count = Math.Min(dirty?.Count ?? 0, MaxRowsPerSection);
            for (int i = 0; i < count; i++)
            {
                RestockTaskKey key = dirty[i];
                string text = FormatQueueKey(key);
                y += DrawTextRow(new Rect(rect.x, y, rect.width, 1f), text, DirtyText);
            }
            if ((dirty?.Count ?? 0) > count)
                y += DrawTextRow(new Rect(rect.x, y, rect.width, 1f), SimTranslation.T("RSMF.RestockPanel.Omitted", ((dirty?.Count ?? 0) - count).Named("count")), MutedText);
            return y - rect.y;
        }

        //绘制 ready 或 blocked 任务段落，职责是显示补货分配状态和失败原因。
        private float DrawTaskSection(Rect rect, string title, List<RestockTask> tasks, Color color)
        {
            float y = rect.y;
            y += DrawSectionHeader(new Rect(rect.x, y, rect.width, 1f), title, tasks?.Count ?? 0, color);
            int count = Math.Min(tasks?.Count ?? 0, MaxRowsPerSection);
            for (int i = 0; i < count; i++)
                y += DrawTaskRow(new Rect(rect.x, y, rect.width, 1f), tasks[i], color);
            if ((tasks?.Count ?? 0) > count)
                y += DrawTextRow(new Rect(rect.x, y, rect.width, 1f), SimTranslation.T("RSMF.RestockPanel.Omitted", ((tasks?.Count ?? 0) - count).Named("count")), MutedText);
            return y - rect.y;
        }

        //绘制段落标题，职责是显示段落名称和数量。
        private static float DrawSectionHeader(Rect rect, string title, int count, Color color)
        {
            float height = Text.LineHeightOf(GameFont.Small) + 10f;
            Rect header = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawBoxSolid(header, new Color(color.r, color.g, color.b, 0.12f));
            DrawBorder(header, new Color(color.r, color.g, color.b, 0.35f));
            Text.Font = GameFont.Small;
            GUI.color = color;
            Widgets.Label(new Rect(header.x + 8f, header.y + 5f, header.width - 16f, height - 6f), title + " (" + count + ")");
            return height + 4f;
        }

        //绘制补货任务行，职责是展示任务关键字段并按文本高度自适应。
        private static float DrawTaskRow(Rect rect, RestockTask task, Color color)
        {
            string text = FormatTask(task);
            return DrawTextRow(rect, text, color);
        }

        //生成补货行的完整译文，职责是让绘制和高度测量使用相同内容。
        private static string FormatTask(RestockTask task)
        {
            if (task == null) return SimTranslation.T("RSMF.RestockPanel.MissingTask");
            return SimTranslation.T("RSMF.RestockPanel.TaskDetails", (FormatQueueKey(task.Key)).Named("target"), (task.NeededCount).Named("needed"), (task.SupplyId).Named("supply"), (task.CreatedTick).Named("created"), (task.LastCheckedTick).Named("checked"), (task.RetryTick).Named("retry"), (task.StateReason).Named("reason"));
        }

        //绘制自适应文本行，职责是避免中文和长原因文本裁切。
        private static float DrawTextRow(Rect rect, string text, Color color)
        {
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            float height = Mathf.Ceil(Text.CalcHeight(text ?? "", rect.width - 16f)) + 10f;
            Rect row = new Rect(rect.x, rect.y, rect.width, height);
            Widgets.DrawBoxSolid(row, RowBg);
            DrawBorder(row);
            GUI.color = color;
            Widgets.Label(new Rect(row.x + 8f, row.y + 5f, row.width - 16f, height - 8f), text ?? "");
            return height + 4f;
        }

        //计算滚动内容高度，职责是让滚动条准确覆盖全部可变高度行。
        private float CalculateViewHeight(float width)
        {
            float height = SummaryCardHeight(width) + Gap;
            height += CalculateDirtyHeight(width) + Gap;
            height += CalculateTaskSectionHeight(width, snapshot?.ReadyTasks) + Gap;
            height += CalculateTaskSectionHeight(width, snapshot?.BlockedTasks);
            return height + 12f;
        }

        //计算 dirty 段落高度，职责是和实际绘制逻辑保持一致。
        private float CalculateDirtyHeight(float width)
        {
            float height = Text.LineHeightOf(GameFont.Small) + 14f;
            List<RestockTaskKey> dirty = snapshot?.DirtyTasks;
            int count = Math.Min(dirty?.Count ?? 0, MaxRowsPerSection);
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < count; i++)
                height += Mathf.Ceil(Text.CalcHeight(FormatQueueKey(dirty[i]), width - 16f)) + 14f;
            if ((dirty?.Count ?? 0) > count)
                height += Text.LineHeightOf(GameFont.Tiny) + 14f;
            return height;
        }

        //计算任务段落高度，职责是和实际绘制逻辑保持一致。
        private float CalculateTaskSectionHeight(float width, List<RestockTask> tasks)
        {
            float height = Text.LineHeightOf(GameFont.Small) + 14f;
            int count = Math.Min(tasks?.Count ?? 0, MaxRowsPerSection);
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < count; i++)
            {
                RestockTask task = tasks[i];
                string text = FormatTask(task);
                height += Mathf.Ceil(Text.CalcHeight(text, width - 16f)) + 14f;
            }
            if ((tasks?.Count ?? 0) > count)
                height += Text.LineHeightOf(GameFont.Tiny) + 14f;
            return height;
        }

        //格式化普通或专业补货键，职责是让面板明确显示槽位和精确来源编号。
        private static string FormatQueueKey(RestockTaskKey key)
        {
            if (key.Kind == RestockRequestKind.Unique)
                return SimTranslation.T("RSMF.RestockPanel.UniqueTarget", (key.StorageId).Named("storage"), (key.SlotIndex).Named("slot"), (key.SourceThingId).Named("source"));
            return SimTranslation.T("RSMF.RestockPanel.BulkTarget", (key.StorageId).Named("storage"), (key.ThingDef?.defName ?? "null").Named("def"));
        }

        //复制完整补货报告，职责是方便玩家直接粘贴到日志网站。
        private void CopyReport()
        {
            string report = RestockDebugReportBuilder.Build(map);
            GUIUtility.systemCopyBuffer = report;
            Messages.Message(SimTranslation.T("RSMF.RestockPanel.Copied"), MessageTypeDefOf.TaskCompletion, false);
        }

        //导出完整补货报告，职责是把排查日志写入配置目录。
        private void ExportReport()
        {
            string report = RestockDebugReportBuilder.Build(map);
            lastExportPath = ExportText(report);
            GUIUtility.systemCopyBuffer = lastExportPath;
            Messages.Message(SimTranslation.T("RSMF.RestockPanel.Exported"), MessageTypeDefOf.TaskCompletion, false);
        }

        //导出文本文件，职责是统一生成 UTF-8 无 BOM 日志。
        private static string ExportText(string text)
        {
            string dir = Path.Combine(GenFilePaths.ConfigFolderPath, "SimManagementLib", "RestockDebugExports");
            Directory.CreateDirectory(dir);
            string fileName = "restock-debug-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
            return path;
        }

        //绘制边框，职责是统一面板线条样式。
        private static void DrawBorder(Rect rect)
        {
            DrawBorder(rect, Border);
        }

        //绘制指定颜色边框，职责是给不同队列状态提供视觉区分。
        private static void DrawBorder(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }
    }
}
