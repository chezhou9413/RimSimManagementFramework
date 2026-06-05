using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供套餐 AI 取名能力，负责把套餐商品摘要交给已配置的大模型并清理返回名称。
    /// </summary>
    public static class ComboAiNameUtility
    {
        private const int MaxNameLength = 24;

        /// <summary>
        /// 根据当前套餐和商店信息生成套餐名称，负责在失败时返回空文本交给 UI 提示。
        /// </summary>
        public static async Task<string> GenerateNameAsync(ComboData combo, Zone_Shop shopZone, SimManagementLibSettings settings, CancellationToken token)
        {
            if (combo == null || combo.items.NullOrEmpty() || settings == null || !settings.HasValidLlmConfig())
                return "";

            string systemPrompt = BuildSystemPrompt();
            string userPrompt = BuildUserPrompt(combo, shopZone);
            string raw = await SimLlmUtility.GenerateTextAsync(systemPrompt, userPrompt, settings, token);
            return CleanGeneratedName(raw);
        }

        /// <summary>
        /// 构造套餐取名的系统提示词，负责把输出限制为单个可直接使用的短名称。
        /// </summary>
        private static string BuildSystemPrompt()
        {
            return "你是殖民地商店的套餐命名助手。只输出一个套餐名称，不要解释，不要编号，不要引号，不要 Markdown。名称要适合 RimWorld 商店界面，使用玩家当前语言，长度不超过 12 个中文字符或 24 个英文字符。";
        }

        /// <summary>
        /// 构造套餐取名的用户提示词，负责提供商品、数量、价格和店铺上下文。
        /// </summary>
        private static string BuildUserPrompt(ComboData combo, Zone_Shop shopZone)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("请为这个商店套餐取一个好记、像真实店铺会使用的名称。");
            sb.AppendLine("当前语言: " + SimTranslation.ActiveLanguageDisplayName);
            sb.AppendLine("店铺: " + StringEncodingUtility.SanitizeUtf16(shopZone?.label ?? "商店"));
            sb.AppendLine("价格: " + combo.totalPrice.ToString("0.##"));
            sb.AppendLine("商品:");

            List<ComboItem> items = combo.items ?? new List<ComboItem>();
            for (int i = 0; i < items.Count; i++)
            {
                ComboItem item = items[i];
                if (item?.def == null || item.count <= 0)
                    continue;

                sb.Append("- ");
                sb.Append(StringEncodingUtility.SanitizeUtf16(item.def.label));
                sb.Append(" x");
                sb.Append(item.count);
                sb.AppendLine();
            }

            sb.AppendLine("只返回最终套餐名称。");
            return sb.ToString();
        }

        /// <summary>
        /// 清理模型返回的套餐名，负责去掉包装文本、非法字符和过长内容。
        /// </summary>
        private static string CleanGeneratedName(string raw)
        {
            string name = StringEncodingUtility.SanitizeUtf16(raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return "";

            name = ExtractNameField(name);
            name = name.Replace("\r", " ").Replace("\n", " ").Trim();
            name = name.Trim('"', '\'', '“', '”', '‘', '’', '`', ' ', '\t');
            name = Regex.Replace(name, "^[\\-\\*\\d\\.、：:）\\)\\s]+", "");
            name = Regex.Replace(name, "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return "";

            if (name.Length > MaxNameLength)
                name = name.Substring(0, MaxNameLength).Trim();

            return name;
        }

        /// <summary>
        /// 从常见 JSON 包装中提取名称字段，负责兼容模型没有遵守纯文本输出的情况。
        /// </summary>
        private static string ExtractNameField(string raw)
        {
            Match match = Regex.Match(raw, "\"(?:name|comboName|title)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
                return UnescapeJsonString(match.Groups["value"].Value);

            return raw;
        }

        /// <summary>
        /// 反转义模型返回 JSON 字符串片段，负责支持套餐名字段的基础转义字符。
        /// </summary>
        private static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char next = value[++i];
                switch (next)
                {
                    case '"':
                    case '\\':
                    case '/':
                        sb.Append(next);
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    default:
                        sb.Append(next);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
