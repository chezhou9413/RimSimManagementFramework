using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SimManagementLib.Tool;

namespace SimManagementLib.BusinessAdvisor
{
    //区分聊天正文与工具协议，只从完整结构化对象读取查询和待审批操作。
    internal static class AdvisorReplyReader
    {
        //兼容完整 JSON 代码围栏；普通文字交回任务层处理，不从文字中搜出指令执行。
        internal static AdvisorReply Read(string raw, out bool plainText)
        {
            plainText = false;
            string text = (raw ?? "").Trim().TrimStart('\uFEFF').TrimStart();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = text.IndexOf('\n');
                if (newline >= 0 && text.EndsWith("```", StringComparison.Ordinal) && text.Length > newline + 4)
                {
                    string header = text.Substring(0, newline).TrimEnd();
                    if (header == "```" || header.Equals("```json", StringComparison.OrdinalIgnoreCase))
                        text = text.Substring(newline + 1, text.Length - newline - 4).Trim();
                }
            }
            if (text.Length == 0) throw InvalidResponse(SimTranslation.T("RSMF.Llm.EmptyResponse"), raw);
            if (text[0] != '{' && text[0] != '[' && text[0] != '"')
            {
                plainText = true;
                return new AdvisorReply { reply = raw.Trim(), queries = new List<AdvisorQuery>(), actions = new List<AdvisorAction>() };
            }
            AdvisorReply reply;
            try { reply = JsonSerializer.Deserialize<AdvisorReply>(text); }
            catch (JsonException ex) { throw InvalidResponse(ex.Message, raw); }
            if (reply?.reply == null || reply.queries == null || reply.actions == null
                || reply.queries.Any(q => q == null || string.IsNullOrWhiteSpace(q.topic))
                || reply.actions.Any(a => a == null || string.IsNullOrWhiteSpace(a.kind))
                || (string.IsNullOrWhiteSpace(reply.reply) && reply.queries.Count == 0 && reply.actions.Count == 0))
                throw InvalidResponse(SimTranslation.T("RSMF.Business.Advisor.ResponseFieldsInvalid"), raw);
            bool hasTools = reply.queries.Count > 0 || reply.actions.Count > 0;
            if (hasTools && reply.taskState != null && reply.taskState != "continue")
                throw InvalidResponse("有查询或操作时 taskState 必须为 continue，不能同时宣告任务结束。", raw);
            if (!hasTools && reply.taskState != "completed" && reply.taskState != "blocked" && reply.taskState != "needs_input")
                throw InvalidResponse("空工具列表不能表示继续或等待。继续任务请提交查询或操作；等待施工必须提交 wait_work。只有确已完成才用 taskState=completed；实际受阻用 blocked 并说明原因；需要玩家补充信息用 needs_input。", raw);
            if (!hasTools && string.IsNullOrWhiteSpace(reply.reply))
                throw InvalidResponse("结束任务必须在 reply 说明完成结果、具体阻碍或需要玩家补充的信息。", raw);
            return reply;
        }

        //保留格式错误和返回片段，由任务执行器反馈给模型修正，不猜测缺失参数。
        private static LlmRequestException InvalidResponse(string reason, string raw)
        {
            string message = SimTranslation.T("RSMF.Business.Advisor.ResponseInvalid");
            string excerpt = raw ?? "";
            if (excerpt.Length > 2000) excerpt = excerpt.Substring(0, 2000) + "…";
            return new LlmRequestException(message, message + "\n\n" + reason + "\n\n"
                + SimTranslation.T("RSMF.Business.Advisor.ResponseExcerpt") + "\n" + excerpt, true);
        }
    }
}
