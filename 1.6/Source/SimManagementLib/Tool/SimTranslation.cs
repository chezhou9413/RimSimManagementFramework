using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供本模组统一翻译入口，负责封装 RimWorld Keyed 翻译和当前语言信息。
    /// </summary>
    public static class SimTranslation
    {
        public const string KeyPrefix = "RSMF.";

        /// <summary>
        /// 读取当前 RimWorld 语言目录名，负责给 AI 提示词和调试信息提供稳定语言标识。
        /// </summary>
        public static string ActiveLanguageFolderName
        {
            get
            {
                return LanguageDatabase.activeLanguage?.folderName ?? LanguageDatabase.DefaultLangFolderName;
            }
        }

        /// <summary>
        /// 读取当前 RimWorld 语言显示名，负责让 AI 了解玩家当前使用的自然语言。
        /// </summary>
        public static string ActiveLanguageDisplayName
        {
            get
            {
                LoadedLanguage language = LanguageDatabase.activeLanguage;
                if (language == null)
                    return "English";

                string english = language.FriendlyNameEnglish;
                if (!string.IsNullOrWhiteSpace(english))
                    return english;

                return language.DisplayName;
            }
        }

        /// <summary>
        /// 按完整翻译键读取文本，负责在代码中替代硬编码展示文本。
        /// </summary>
        public static string T(string key)
        {
            return key.Translate().Resolve();
        }

        /// <summary>
        /// 按完整翻译键读取文本，负责在语言系统尚未激活的加载阶段返回硬兜底文本。
        /// </summary>
        public static string TOrFallback(string key, string fallback)
        {
            if (LanguageDatabase.activeLanguage == null)
                return fallback ?? key;

            try
            {
                return key.Translate().Resolve();
            }
            catch
            {
                return fallback ?? key;
            }
        }

        /// <summary>
        /// 按完整翻译键和参数读取文本，负责处理带变量的 UI、消息和提示文本。
        /// </summary>
        public static string T(string key, params NamedArgument[] args)
        {
            return key.Translate(args).Resolve();
        }
    }
}
