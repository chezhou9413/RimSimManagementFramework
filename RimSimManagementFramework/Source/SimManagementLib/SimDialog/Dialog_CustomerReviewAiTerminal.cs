using RimWorld;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 绘制顾客评价 AI 调试终端窗口，负责查看本局内存中的请求、源数据、模型输出和导出操作。
    /// </summary>
    public class Dialog_CustomerReviewAiTerminal : Window
    {
        private const float CloseXReservedWidth = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 18f;
        private static readonly Color WindowBg = new Color(0.10f, 0.11f, 0.13f, 1f);
        private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color PanelAlt = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color DimText = new Color(0.72f, 0.76f, 0.82f, 1f);
        private static readonly Color SuccessText = new Color(0.42f, 0.86f, 0.50f, 1f);
        private static readonly Color FailText = new Color(1f, 0.48f, 0.42f, 1f);
        private static readonly Color PendingText = new Color(0.95f, 0.75f, 0.32f, 1f);

        private Vector2 listScroll;
        private Vector2 detailScroll;
        private int filterIndex;
        private int detailTabIndex;
        private int selectedRecordId;

        public override Vector2 InitialSize => new Vector2(1100f, 760f);

        /// <summary>
        /// 初始化 AI 调试终端窗口的基础交互行为。
        /// </summary>
        public Dialog_CustomerReviewAiTerminal()
        {
            doCloseX = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            draggable = true;
            resizeable = true;
        }

        /// <summary>
        /// 绘制调试终端窗口主体，负责分配标题、筛选、列表、详情和底部操作区域。
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
                List<CustomerReviewAiDebugRecord> records = CustomerReviewAiDebugLog.GetRecords();
                Rect titleRect = new Rect(inRect.x, inRect.y, Mathf.Max(0f, inRect.width - CloseXReservedWidth), Mathf.Max(36f, Text.LineHeightOf(GameFont.Medium) + 10f));
                DrawTitle(titleRect, records.Count);

                Rect filterRect = new Rect(inRect.x, titleRect.yMax + 8f, inRect.width, Mathf.Max(34f, Text.LineHeightOf(GameFont.Small) + 12f));
                DrawFilters(filterRect);

                Rect bottomRect = new Rect(inRect.x, inRect.yMax - 42f, inRect.width, 42f);
                Rect bodyRect = new Rect(inRect.x, filterRect.yMax + 8f, inRect.width, Mathf.Max(160f, bottomRect.y - filterRect.yMax - 10f));
                DrawBody(bodyRect, records);
                DrawBottomButtons(bottomRect, records);
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
        /// 绘制窗口标题，负责显示当前本局调试记录数量。
        /// </summary>
        private void DrawTitle(Rect rect, int count)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(rect, SimTranslation.T("RSMF.AiTerminal.Title"));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = DimText;
            Widgets.Label(rect, SimTranslation.T("RSMF.AiTerminal.RecordCount", count.Named("count")));
            ResetText();
        }

        /// <summary>
        /// 绘制状态筛选按钮，负责在全部、成功、失败和请求中之间切换。
        /// </summary>
        private void DrawFilters(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            string[] labels =
            {
                SimTranslation.T("RSMF.AiTerminal.Filter.All"),
                SimTranslation.T("RSMF.AiTerminal.Filter.Success"),
                SimTranslation.T("RSMF.AiTerminal.Filter.Failed"),
                SimTranslation.T("RSMF.AiTerminal.Filter.Pending")
            };
            float x = rect.x + 8f;
            float h = rect.height - 8f;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect tab = new Rect(x, rect.y + 4f, 86f, h);
                if (SimUiStyle.DrawTabButton(tab, labels[i], filterIndex == i, DimText))
                {
                    filterIndex = i;
                    listScroll = Vector2.zero;
                    detailScroll = Vector2.zero;
                }
                x += 94f;
            }
        }

        /// <summary>
        /// 绘制主体区域，负责将左侧记录列表和右侧详情面板并排展示。
        /// </summary>
        private void DrawBody(Rect rect, List<CustomerReviewAiDebugRecord> records)
        {
            float leftW = Mathf.Clamp(rect.width * 0.32f, 300f, 390f);
            Rect listRect = new Rect(rect.x, rect.y, leftW, rect.height);
            Rect detailRect = new Rect(listRect.xMax + 10f, rect.y, Mathf.Max(260f, rect.width - leftW - 10f), rect.height);
            List<CustomerReviewAiDebugRecord> filtered = FilterRecords(records);
            EnsureSelection(filtered);
            DrawRecordList(listRect, filtered);
            CustomerReviewAiDebugRecord selected = filtered.FirstOrDefault(r => r.id == selectedRecordId);
            DrawDetail(detailRect, selected);
        }

        /// <summary>
        /// 根据当前筛选状态返回记录，负责让列表和导出当前可见记录保持一致。
        /// </summary>
        private List<CustomerReviewAiDebugRecord> FilterRecords(List<CustomerReviewAiDebugRecord> records)
        {
            IEnumerable<CustomerReviewAiDebugRecord> query = records ?? Enumerable.Empty<CustomerReviewAiDebugRecord>();
            if (filterIndex == 1)
                query = query.Where(r => r != null && r.IsSuccess());
            else if (filterIndex == 2)
                query = query.Where(r => r != null && r.IsFailed());
            else if (filterIndex == 3)
                query = query.Where(r => r != null && r.IsPending());
            return query.Where(r => r != null).ToList();
        }

        /// <summary>
        /// 确保选中项仍然存在，负责在筛选或清空后自动选择第一条可见记录。
        /// </summary>
        private void EnsureSelection(List<CustomerReviewAiDebugRecord> records)
        {
            if (records.NullOrEmpty())
            {
                selectedRecordId = 0;
                return;
            }

            if (!records.Any(r => r.id == selectedRecordId))
            {
                selectedRecordId = records[0].id;
                detailScroll = Vector2.zero;
            }
        }

        /// <summary>
        /// 绘制左侧记录列表，负责展示状态、顾客、店铺和摘要。
        /// </summary>
        private void DrawRecordList(Rect rect, List<CustomerReviewAiDebugRecord> records)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            if (records.NullOrEmpty())
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = DimText;
                Widgets.Label(rect, SimTranslation.T("RSMF.AiTerminal.NoRecords"));
                ResetText();
                return;
            }

            float viewW = Mathf.Max(120f, rect.width - 18f);
            float rowH = CalcListRowHeight(viewW);
            Rect viewRect = new Rect(0f, 0f, viewW, Mathf.Max(rect.height + 1f, records.Count * rowH + 8f));
            Widgets.BeginScrollView(rect.ContractedBy(4f), ref listScroll, viewRect);
            for (int i = 0; i < records.Count; i++)
            {
                Rect row = new Rect(0f, i * rowH, viewW, rowH - 6f);
                DrawRecordRow(row, records[i], i);
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// 计算列表行高，负责给 Tiny 字体回退和多行摘要预留空间。
        /// </summary>
        private float CalcListRowHeight(float width)
        {
            float line = Mathf.Max(Text.LineHeightOf(GameFont.Tiny), 20f);
            return line * 4f + 28f;
        }

        /// <summary>
        /// 绘制单条记录行，负责点击选择并用颜色区分状态。
        /// </summary>
        private void DrawRecordRow(Rect row, CustomerReviewAiDebugRecord record, int index)
        {
            bool selected = record.id == selectedRecordId;
            Widgets.DrawBoxSolid(row, selected ? new Color(0.25f, 0.65f, 0.85f, 0.22f) : (index % 2 == 0 ? PanelAlt : new Color(0f, 0f, 0f, 0.08f)));
            SimUiStyle.DrawBorder(row, selected ? new Color(0.25f, 0.65f, 0.85f, 0.75f) : new Color(1f, 1f, 1f, 0.08f));

            float pad = 8f;
            float textW = row.width - pad * 2f;
            float lineH = Mathf.Max(Text.LineHeightOf(GameFont.Tiny), 20f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = GetStatusColor(record);
            Widgets.Label(new Rect(row.x + pad, row.y + 6f, textW, lineH + 2f), record.StatusLabel() + "  #" + record.id);

            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + pad, row.y + 8f + lineH, textW, lineH + 2f), BuildListTitle(record).Truncate(textW));

            GUI.color = DimText;
            Widgets.Label(new Rect(row.x + pad, row.y + 10f + lineH * 2f, textW, lineH + 2f), (record.provider + " / " + record.model).Truncate(textW));
            Widgets.Label(new Rect(row.x + pad, row.y + 12f + lineH * 3f, textW, lineH + 2f), BuildListTail(record).Truncate(textW));
            ResetText();

            if (Widgets.ButtonInvisible(row, false))
            {
                selectedRecordId = record.id;
                detailScroll = Vector2.zero;
            }
        }

        /// <summary>
        /// 构造列表主标题，负责优先显示顾客和店铺。
        /// </summary>
        private string BuildListTitle(CustomerReviewAiDebugRecord record)
        {
            string customer = string.IsNullOrWhiteSpace(record.customerDisplayName) ? SimTranslation.T("RSMF.CustomerReview.Snapshot.UnknownCustomer") : record.customerDisplayName;
            string shop = string.IsNullOrWhiteSpace(record.zoneLabel) ? SimTranslation.T("RSMF.AiTerminal.UnknownShop") : record.zoneLabel;
            return customer + " · " + shop;
        }

        /// <summary>
        /// 构造列表尾部摘要，负责显示星级或失败原因。
        /// </summary>
        private string BuildListTail(CustomerReviewAiDebugRecord record)
        {
            if (record.IsSuccess())
                return record.parsedStars > 0 ? SimTranslation.T("RSMF.AiTerminal.StarsAtTime", record.parsedStars.Named("stars"), record.createdAt.Named("time")) : record.createdAt;
            if (record.IsFailed())
                return string.IsNullOrWhiteSpace(record.failureReason) ? record.createdAt : record.failureReason;
            return SimTranslation.T("RSMF.AiTerminal.PendingAtTime", record.createdAt.Named("time"));
        }

        /// <summary>
        /// 绘制右侧详情区，负责显示详情页签和大文本滚动区域。
        /// </summary>
        private void DrawDetail(Rect rect, CustomerReviewAiDebugRecord record)
        {
            Widgets.DrawBoxSolid(rect, PanelBg);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));
            if (record == null)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = DimText;
                Widgets.Label(rect, SimTranslation.T("RSMF.AiTerminal.SelectRecord"));
                ResetText();
                return;
            }

            Rect headerRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, Mathf.Max(34f, Text.LineHeightOf(GameFont.Small) + 12f));
            DrawDetailTabs(headerRect);

            Rect textRect = new Rect(rect.x + 8f, headerRect.yMax + 8f, rect.width - 16f, Mathf.Max(80f, rect.yMax - headerRect.yMax - 16f));
            DrawDetailText(textRect, BuildDetailText(record));
        }

        /// <summary>
        /// 绘制详情页签，负责在源数据、发送内容、AI 输出和解析结果间切换。
        /// </summary>
        private void DrawDetailTabs(Rect rect)
        {
            string[] labels =
            {
                SimTranslation.T("RSMF.AiTerminal.Tab.Source"),
                SimTranslation.T("RSMF.AiTerminal.Tab.Request"),
                SimTranslation.T("RSMF.AiTerminal.Tab.Output"),
                SimTranslation.T("RSMF.AiTerminal.Tab.Parsed")
            };
            float x = rect.x;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect tab = new Rect(x, rect.y, 116f, rect.height);
                if (SimUiStyle.DrawTabButton(tab, labels[i], detailTabIndex == i, DimText))
                {
                    detailTabIndex = i;
                    detailScroll = Vector2.zero;
                }
                x += 124f;
            }
        }

        /// <summary>
        /// 绘制详情文本滚动区，负责按内容动态计算高度避免长文本裁切。
        /// </summary>
        private void DrawDetailText(Rect rect, string text)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.24f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
            float viewW = Mathf.Max(120f, rect.width - 24f);
            GameFont oldFont = Text.Font;
            bool oldWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = true;
                float textH = Mathf.Max(rect.height + 1f, Text.CalcHeight(text ?? "", viewW) + 16f);
                Rect viewRect = new Rect(0f, 0f, viewW, textH);
                Widgets.BeginScrollView(rect.ContractedBy(4f), ref detailScroll, viewRect);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(0f, 0f, viewW, textH), text ?? "");
                Widgets.EndScrollView();
            }
            finally
            {
                Text.Font = oldFont;
                Text.WordWrap = oldWrap;
            }
            ResetText();
        }

        /// <summary>
        /// 构造当前详情页文本，负责让复制当前页与 UI 显示完全一致。
        /// </summary>
        private string BuildDetailText(CustomerReviewAiDebugRecord record)
        {
            if (record == null) return "";
            if (detailTabIndex == 0)
                return record.snapshotText;
            if (detailTabIndex == 1)
                return BuildRequestText(record);
            if (detailTabIndex == 2)
                return BuildOutputText(record);
            return BuildParsedText(record);
        }

        /// <summary>
        /// 构造发送给 AI 页文本，负责展示 system prompt、分层 user 消息和 HTTP 请求体。
        /// </summary>
        private string BuildRequestText(CustomerReviewAiDebugRecord record)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.SystemPrompt"));
            sb.AppendLine(record.systemPrompt);
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.UserPrompt"));
            sb.AppendLine(record.userPrompt);
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.StablePrefix"));
            sb.AppendLine(record.stablePromptPrefix);
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.DynamicInput"));
            sb.AppendLine(record.dynamicPrompt);
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.Request.Messages"));
            sb.AppendLine(record.messagesText);
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.HttpAttemptsHeader"));
            sb.AppendLine(CustomerReviewAiDebugLog.BuildAttemptsText(record));
            return sb.ToString();
        }

        /// <summary>
        /// 构造 AI 输出页文本，负责展示原始响应、抽取文本和失败原因。
        /// </summary>
        private string BuildOutputText(CustomerReviewAiDebugRecord record)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.AssistantTextHeader"));
            sb.AppendLine(record.rawAssistantText);
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.HttpAttemptDetailsHeader"));
            sb.AppendLine(CustomerReviewAiDebugLog.BuildAttemptsText(record));
            if (!string.IsNullOrWhiteSpace(record.failureReason))
            {
                sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.FailureReasonHeader"));
                sb.AppendLine(record.failureReason);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 构造解析结果页文本，负责展示结构化结果和请求元信息。
        /// </summary>
        private string BuildParsedText(CustomerReviewAiDebugRecord record)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.StatusLine", record.StatusLabel().Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.ProviderLine", record.provider.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.ModelLine", record.model.Named("value")));
            sb.AppendLine("Endpoint: " + record.endpoint);
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.NewConversationLine", record.startedNewConversation.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.ConversationTurnsLine", record.conversationTurnCount.Named("value")));
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.ConversationCharsLine", record.conversationCharCount.Named("value")));
            sb.AppendLine();
            sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.ParsedResultHeader"));
            sb.AppendLine(record.parsedResultText);
            if (!string.IsNullOrWhiteSpace(record.failureReason))
            {
                sb.AppendLine(SimTranslation.T("RSMF.AiTerminal.FailureReasonHeader"));
                sb.AppendLine(record.failureReason);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 绘制底部操作按钮，负责复制、导出 JSON 和清空本局日志。
        /// </summary>
        private void DrawBottomButtons(Rect rect, List<CustomerReviewAiDebugRecord> records)
        {
            float h = Mathf.Min(34f, rect.height);
            float y = rect.y + (rect.height - h) * 0.5f;
            float x = rect.x;
            List<CustomerReviewAiDebugRecord> filtered = FilterRecords(records);
            CustomerReviewAiDebugRecord selected = filtered.FirstOrDefault(r => r.id == selectedRecordId);

            if (DrawButton(ref x, y, 112f, h, SimTranslation.T("RSMF.AiTerminal.CopyPage"), selected != null))
            {
                GUIUtility.systemCopyBuffer = BuildDetailText(selected);
                Messages.Message(SimTranslation.T("RSMF.AiTerminal.CopiedPageMessage"), MessageTypeDefOf.PositiveEvent, false);
            }
            if (DrawButton(ref x, y, 132f, h, SimTranslation.T("RSMF.AiTerminal.CopyRecord"), selected != null))
            {
                GUIUtility.systemCopyBuffer = CustomerReviewAiDebugLog.BuildRecordText(selected);
                Messages.Message(SimTranslation.T("RSMF.AiTerminal.CopiedRecordMessage"), MessageTypeDefOf.PositiveEvent, false);
            }
            if (DrawButton(ref x, y, 138f, h, SimTranslation.T("RSMF.AiTerminal.ExportCurrentJson"), selected != null))
            {
                ExportJson(CustomerReviewAiDebugLog.BuildRecordJson(selected), "ai-debug-record");
            }
            if (DrawButton(ref x, y, 132f, h, SimTranslation.T("RSMF.AiTerminal.ExportAllJson"), filtered.Count > 0))
            {
                ExportJson(CustomerReviewAiDebugLog.BuildAllJson(filtered), "ai-debug-all");
            }

            Rect clearRect = new Rect(rect.xMax - 116f, y, 116f, h);
            if (SimUiStyle.DrawDangerButton(clearRect, SimTranslation.T("RSMF.AiTerminal.ClearLog"), records != null && records.Count > 0, GameFont.Tiny))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(SimTranslation.T("RSMF.AiTerminal.ClearLogConfirm"), CustomerReviewAiDebugLog.Clear));
            }
        }

        /// <summary>
        /// 绘制底部普通按钮并推进横向坐标，负责保持操作栏布局紧凑。
        /// </summary>
        private bool DrawButton(ref float x, float y, float w, float h, string label, bool enabled)
        {
            Rect rect = new Rect(x, y, w, h);
            x += w + 8f;
            return SimUiStyle.DrawSecondaryButton(rect, label, enabled, GameFont.Tiny);
        }

        /// <summary>
        /// 导出 JSON 并提示路径，负责把文件路径复制到剪贴板便于玩家打开。
        /// </summary>
        private void ExportJson(string json, string prefix)
        {
            try
            {
                string path = CustomerReviewAiDebugLog.ExportJson(json, prefix);
                GUIUtility.systemCopyBuffer = path;
                Messages.Message(SimTranslation.T("RSMF.AiTerminal.ExportSucceeded", path.Named("path")), MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Messages.Message(SimTranslation.T("RSMF.AiTerminal.ExportFailed", ex.Message.Named("message")), MessageTypeDefOf.RejectInput, false);
            }
        }

        /// <summary>
        /// 根据记录状态返回颜色，负责统一列表状态视觉反馈。
        /// </summary>
        private Color GetStatusColor(CustomerReviewAiDebugRecord record)
        {
            if (record == null) return DimText;
            if (record.IsSuccess()) return SuccessText;
            if (record.IsFailed()) return FailText;
            return PendingText;
        }

        /// <summary>
        /// 恢复窗口绘制的默认文本状态，负责避免局部控件污染后续 IMGUI 绘制。
        /// </summary>
        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
