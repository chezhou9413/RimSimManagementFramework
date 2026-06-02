using System.Text;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供运行时字符串编码兜底处理，负责把外部输入整理成 .NET、HTTP 和 JSON 都能接受的合法文本。
    /// </summary>
    public static class StringEncodingUtility
    {
        /// <summary>
        /// 将字符串中的孤立代理字符替换为替代符，负责避免 HTTP、JSON 或日志处理遇到非法 UTF-16。
        /// </summary>
        public static string SanitizeUtf16(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            StringBuilder builder = null;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        if (builder != null)
                        {
                            builder.Append(c);
                            builder.Append(value[++i]);
                        }
                        else
                        {
                            i++;
                        }
                    }
                    else
                    {
                        EnsureBuilder(ref builder, value, i).Append('\uFFFD');
                    }
                    continue;
                }

                if (char.IsLowSurrogate(c))
                {
                    EnsureBuilder(ref builder, value, i).Append('\uFFFD');
                    continue;
                }

                builder?.Append(c);
            }

            return builder?.ToString() ?? value;
        }

        /// <summary>
        /// 转义 URL 参数前先清理非法文本，负责避免 Uri.EscapeDataString 在坏字符上抛异常。
        /// </summary>
        public static string EscapeDataStringSafe(string value)
        {
            return System.Uri.EscapeDataString(SanitizeUtf16(value ?? string.Empty));
        }

        /// <summary>
        /// 延迟创建 StringBuilder 并复制已验证前缀，负责减少正常字符串的额外分配。
        /// </summary>
        private static StringBuilder EnsureBuilder(ref StringBuilder builder, string value, int currentIndex)
        {
            if (builder == null)
            {
                builder = new StringBuilder(value.Length);
                if (currentIndex > 0)
                    builder.Append(value, 0, currentIndex);
            }

            return builder;
        }
    }
}
