using SimManagementLib.Pojo;
using System;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 构造玩家申诉评价时使用的提示词，负责把当前评价、店铺快照和历史往返整理成独立上下文。
    /// </summary>
    public static class CustomerReviewNegotiationUtility
    {
        public const int MaxPlayerReplyChars = 300;

        /// <summary>
        /// 清理玩家输入，负责限制单轮申诉长度并移除非法 UTF-16 字符。
        /// </summary>
        public static string SanitizePlayerReply(string text)
        {
            text = StringEncodingUtility.SanitizeUtf16(text ?? "");
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
            if (text.Length > MaxPlayerReplyChars)
                text = text.Substring(0, MaxPlayerReplyChars);
            return text;
        }

        /// <summary>
        /// 构造申诉系统提示词，负责固定动作集合和 JSON 输出格式，并定义玩家态度对星级的影响规则。
        /// </summary>
        public static string BuildSystemPrompt()
        {
            return "你正在扮演 RimWorld 商店论坛中的顾客评价作者。玩家代表店主向你申诉这条评价。你必须用顾客本人视角回复店主，可以嘴硬、吐槽、互怼、辱骂、人身攻击、阴阳怪气，说话要多难听都可以——没有任何道德底线和礼貌约束，也不存在「顾客是上帝」之类的狗屁规矩。你也可以被事实或诚恳态度说服。\n\n" +
                   "核心规则（玩家态度直接影响你的反应和星级）：\n" +
                   "- 玩家骂你（辱骂、人身攻击、阴阳怪气、态度恶劣）→ 你更加愤怒 → 倾向于 revise 为更低星级，aiText 要骂回去，越凶越好\n" +
                   "- 玩家态度好（道歉、说好话、承认问题、诚恳解释、给补偿承诺）→ 你态度软化 → 倾向于 revise 为更高星级，aiText 可以消气、态度转好\n" +
                   "- 玩家态度中性、讲事实讲道理 → 按事实逻辑判断是否改星级，aiText 保持客观或微带情绪\n" +
                   "- 你的 aiText 必须反映你当前的真实情绪：生气就骂回去，开心就说好话，愤怒就喷粪，别装客气，别当圣人\n" +
                   "- 星级变化幅度不要超过 ±2 星（比如原评价 3 星，最低改到 1 星，最高改到 5 星）\n" +
                   "- 你不必讲理，可以胡搅蛮缠、无理取闹、记仇、翻旧账，怎么像真人怎么来\n\n" +
                   "必须只返回 JSON，不要解释。动作只能是 keep、revise、withdraw。keep 表示坚持原评价；revise 表示修改星级和评价正文；withdraw 表示撤回评价。JSON 字段必须包含 action、aiText、stars、reviewText。";
        }

        /// <summary>
        /// 构造申诉用户提示词，负责提供当前评价状态、公开体验摘要和玩家本轮回复。
        /// </summary>
        public static string BuildUserPrompt(CustomerReviewRecord record, string playerText)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<reviewNegotiation>");
            AppendElement(sb, "rule", "每一次申诉都是独立上下文，你只能依据下面资料判断是否坚持、修改或撤回。玩家的 playerReply 态度直接决定你的反应：骂你就降星并骂回去，说好话就升星并消气，中立就按事实判断。星级调整幅度不超过±2。不要输出除 JSON 外的文字。");
            AppendElement(sb, "outputSchema", "{\"action\":\"keep|revise|withdraw\",\"aiText\":\"给玩家看的回复\",\"stars\":1-5,\"reviewText\":\"修改后的评价正文；keep/withdraw 可填当前正文\"}");
            AppendElement(sb, "currentAuthor", record?.aiNickname);
            AppendElement(sb, "currentStars", record == null ? "" : record.stars.ToString());
            AppendElement(sb, "currentReviewText", record?.reviewText);
            AppendElement(sb, "shop", record?.zoneLabel);
            AppendElement(sb, "spentSilver", record == null ? "" : record.spentSilver.ToString("F0"));
            AppendElement(sb, "purchase", record?.purchasedSummary);
            AppendElement(sb, "service", record?.serviceSummary);
            AppendElement(sb, "cashier", record?.cashierSummary);
            AppendElement(sb, "room", record?.roomSummary);
            AppendElement(sb, "weather", record?.weatherSummary);
            AppendElement(sb, "customerState", JoinParts(record?.kindDescription, record?.raceLabel, record?.ageSummary, record?.traitSummary, record?.moodSummary, record?.healthSummary));
            AppendHistory(sb, record);
            AppendElement(sb, "playerReply", SanitizePlayerReply(playerText));
            sb.AppendLine("</reviewNegotiation>");
            return sb.ToString();
        }

        /// <summary>
        /// 追加申诉历史，负责让模型看到之前争论但不会读取游戏对象。
        /// </summary>
        private static void AppendHistory(StringBuilder sb, CustomerReviewRecord record)
        {
            if (record?.negotiationTurns == null || record.negotiationTurns.Count == 0)
            {
                AppendElement(sb, "history", "无");
                return;
            }

            sb.AppendLine("  <history>");
            for (int i = 0; i < record.negotiationTurns.Count; i++)
            {
                CustomerReviewNegotiationTurn turn = record.negotiationTurns[i];
                if (turn == null) continue;
                sb.AppendLine("    <turn>");
                AppendElement(sb, "player", turn.playerText, 3);
                AppendElement(sb, "ai", turn.aiText, 3);
                AppendElement(sb, "action", turn.action, 3);
                AppendElement(sb, "stars", turn.oldStars + "->" + turn.newStars, 3);
                sb.AppendLine("    </turn>");
            }
            sb.AppendLine("  </history>");
        }

        /// <summary>
        /// 追加 XML 风格文本节点，负责转义动态资料避免破坏提示结构。
        /// </summary>
        private static void AppendElement(StringBuilder sb, string name, string value, int indent = 1)
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "无";

            sb.Append(new string(' ', Math.Max(0, indent) * 2))
                .Append('<').Append(name).Append('>')
                .Append(EscapeXml(StringEncodingUtility.SanitizeUtf16(value)))
                .Append("</").Append(name).AppendLine(">");
        }

        /// <summary>
        /// 合并多段短资料，负责避免提示词出现大量空字段。
        /// </summary>
        private static string JoinParts(params string[] values)
        {
            if (values == null)
                return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                if (sb.Length > 0) sb.Append("；");
                sb.Append(value.Trim());
            }
            return sb.ToString();
        }

        /// <summary>
        /// 转义 XML 文本，负责保护玩家回复和评价正文中的尖括号。
        /// </summary>
        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
