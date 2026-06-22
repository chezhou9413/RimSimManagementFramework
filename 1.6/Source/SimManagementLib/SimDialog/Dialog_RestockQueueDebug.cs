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
        private const float SummaryCardHeight = 58f;
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
            Widgets.Label(titleRect, "补货队列调试");

            Text.Font = GameFont.Small;
            GUI.color = MutedText;
            string summary = map == null
                ? "当前没有地图。"
                : "Map=" + map.uniqueID + " Tick=" + (Find.TickManager?.TicksGame ?? 0) + " 导出路径=" + (string.IsNullOrEmpty(lastExportPath) ? "未导出" : lastExportPath);
            Widgets.Label(new Rect(rect.x + 12f, titleRect.yMax + 6f, rect.width - 24f, Text.LineHeightOf(GameFont.Small) + 6f), summary);
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
                y += DrawSummaryCards(new Rect(0f, y, viewWidth, SummaryCardHeight));
                y += Gap;
                y += DrawDirtySection(new Rect(0f, y, viewWidth, 1f));
                y += Gap;
                y += DrawTaskSection(new Rect(0f, y, viewWidth, 1f), "Ready", snapshot?.ReadyTasks, ReadyText);
                y += Gap;
                DrawTaskSection(new Rect(0f, y, viewWidth, 1f), "Blocked", snapshot?.BlockedTasks, BlockedText);
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

            if (Widgets.ButtonText(refreshRect, "刷新"))
                RefreshSnapshot();
            if (Widgets.ButtonText(rebuildRect, "重建队列"))
            {
                map?.GetComponent<MapComponent_RestockTaskQueue>()?.ResetAndRebuildAll("调试面板重建队列");
                RefreshSnapshot();
            }
            if (Widgets.ButtonText(copyRect, "复制完整日志"))
                CopyReport();
            if (Widgets.ButtonText(exportRect, "导出日志文件"))
                ExportReport();
            if (Widgets.ButtonText(closeRect, "关闭"))
                Close();
        }

        //绘制概要卡片，职责是快速展示队列数量和最近处理信息。
        private float DrawSummaryCards(Rect rect)
        {
            int dirty = snapshot?.DirtyCount ?? 0;
            int ready = snapshot?.ReadyCount ?? 0;
            int blocked = snapshot?.BlockedCount ?? 0;
            float cardGap = 8f;
            float cardWidth = (rect.width - cardGap * 3f) / 4f;
            DrawSummaryCard(new Rect(rect.x, rect.y, cardWidth, rect.height), "Dirty", dirty.ToString(CultureInfo.InvariantCulture), DirtyText);
            DrawSummaryCard(new Rect(rect.x + (cardWidth + cardGap), rect.y, cardWidth, rect.height), "Ready", ready.ToString(CultureInfo.InvariantCulture), ReadyText);
            DrawSummaryCard(new Rect(rect.x + (cardWidth + cardGap) * 2f, rect.y, cardWidth, rect.height), "Blocked", blocked.ToString(CultureInfo.InvariantCulture), BlockedText);
            string tickText = "处理 " + (snapshot?.LastProcessTick.ToString(CultureInfo.InvariantCulture) ?? "-1") + "\n重建 " + (snapshot?.LastRebuildTick.ToString(CultureInfo.InvariantCulture) ?? "-1");
            DrawSummaryCard(new Rect(rect.x + (cardWidth + cardGap) * 3f, rect.y, cardWidth, rect.height), "Tick", tickText, MutedText);
            return rect.height;
        }

        //绘制单个概要卡片，职责是用稳定高度显示短文本。
        private static void DrawSummaryCard(Rect rect, string title, string value, Color valueColor)
        {
            Widgets.DrawBoxSolid(rect, RowBg);
            DrawBorder(rect);
            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, Text.LineHeightOf(GameFont.Tiny) + 2f), title);
            Text.Font = GameFont.Small;
            GUI.color = valueColor;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, rect.height - 30f), value);
        }

        //绘制 dirty 队列段落，职责是展示等待处理的货柜和商品键。
        private float DrawDirtySection(Rect rect)
        {
            List<RestockTaskKey> dirty = snapshot?.DirtyTasks;
            float y = rect.y;
            y += DrawSectionHeader(new Rect(rect.x, y, rect.width, 1f), "Dirty 等待重算", dirty?.Count ?? 0, DirtyText);
            int count = Math.Min(dirty?.Count ?? 0, MaxRowsPerSection);
            for (int i = 0; i < count; i++)
            {
                RestockTaskKey key = dirty[i];
                string text = "storage=" + key.StorageId + " def=" + (key.ThingDef?.defName ?? "null");
                y += DrawTextRow(new Rect(rect.x, y, rect.width, 1f), text, DirtyText);
            }
            if ((dirty?.Count ?? 0) > count)
                y += DrawTextRow(new Rect(rect.x, y, rect.width, 1f), "剩余省略: " + ((dirty?.Count ?? 0) - count), MutedText);
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
                y += DrawTextRow(new Rect(rect.x, y, rect.width, 1f), "剩余省略: " + ((tasks?.Count ?? 0) - count), MutedText);
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
            string text = task == null
                ? "null task"
                : "storage=" + task.StorageId
                  + " def=" + (task.ThingDef?.defName ?? "null")
                  + " need=" + task.NeededCount
                  + " supply=" + task.SupplyId
                  + " created=" + task.CreatedTick
                  + " checked=" + task.LastCheckedTick
                  + " retry=" + task.RetryTick
                  + " reason=" + task.StateReason;
            return DrawTextRow(rect, text, color);
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
            float height = SummaryCardHeight + Gap;
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
                height += Mathf.Ceil(Text.CalcHeight("storage=" + dirty[i].StorageId + " def=" + (dirty[i].ThingDef?.defName ?? "null"), width - 16f)) + 14f;
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
                string text = task == null ? "null task" : "storage=" + task.StorageId + " def=" + (task.ThingDef?.defName ?? "null") + " need=" + task.NeededCount + " supply=" + task.SupplyId + " created=" + task.CreatedTick + " checked=" + task.LastCheckedTick + " retry=" + task.RetryTick + " reason=" + task.StateReason;
                height += Mathf.Ceil(Text.CalcHeight(text, width - 16f)) + 14f;
            }
            if ((tasks?.Count ?? 0) > count)
                height += Text.LineHeightOf(GameFont.Tiny) + 14f;
            return height;
        }

        //复制完整补货报告，职责是方便玩家直接粘贴到日志网站。
        private void CopyReport()
        {
            string report = RestockDebugReportBuilder.Build(map);
            GUIUtility.systemCopyBuffer = report;
            Messages.Message("已复制完整补货日志。", MessageTypeDefOf.TaskCompletion, false);
        }

        //导出完整补货报告，职责是把排查日志写入配置目录。
        private void ExportReport()
        {
            string report = RestockDebugReportBuilder.Build(map);
            lastExportPath = ExportText(report);
            GUIUtility.systemCopyBuffer = lastExportPath;
            Messages.Message("已导出补货日志，并复制路径。", MessageTypeDefOf.TaskCompletion, false);
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
