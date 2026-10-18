using SimManagementLib.Pojo;
using System;
using System.Reflection;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 通过反射读取 RimTalk 的当前 AI 接口配置，负责在不建立编译期依赖的情况下复用 RimTalk 模型和密钥。
    /// </summary>
    public static class RimTalkConfigBridge
    {
        private const string RimTalkPackageId = "cj.rimtalk";
        private static bool? cachedLoaded;
        private static Type cachedSettingsType;

        /// <summary>
        /// 判断当前加载列表是否包含 RimTalk，负责控制设置界面的导入入口。
        /// </summary>
        public static bool IsRimTalkLoaded()
        {
            if (cachedLoaded.HasValue)
                return cachedLoaded.Value;

            ModMetaData meta = ModLister.GetActiveModWithIdentifier(RimTalkPackageId, true);
            if (meta != null)
            {
                cachedLoaded = true;
                return true;
            }

            cachedLoaded = AccessToolsType("RimTalk.Settings") != null;
            return cachedLoaded.Value;
        }

        /// <summary>
        /// 尝试把 RimTalk 当前有效配置写入本模组设置，负责返回可展示给玩家的导入结果。
        /// </summary>
        public static bool TryApplyTo(SimManagementLibSettings settings, out string message)
        {
            message = "";
            if (settings == null)
            {
                message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.NoSettings");
                return false;
            }

            object activeConfig = TryGetActiveConfig(out message);
            if (activeConfig == null)
                return false;

            string provider = ReadString(activeConfig, "Provider");
            string apiKey = ReadString(activeConfig, "ApiKey");
            string selectedModel = ReadString(activeConfig, "SelectedModel");
            string customModel = ReadString(activeConfig, "CustomModelName");
            string baseUrl = ReadString(activeConfig, "BaseUrl");

            string model = ResolveModel(selectedModel, customModel);
            string endpoint = ResolveEndpoint(provider, baseUrl);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.UnsupportedProvider", provider.Named("provider"));
                return false;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.NoModel");
                return false;
            }

            settings.llmProvider = SimLlmProvider.OpenAICompatible;
            settings.llmOpenAiBaseUrl = endpoint;
            settings.llmOpenAiApiKey = apiKey ?? "";
            settings.llmOpenAiModel = model;
            settings.llmEnabled = true;
            settings.reviewAiEnabled = true;
            settings.SyncLegacyReviewAiConnectionFields();
            settings.Write();

            message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.Success", provider.Named("provider"), model.Named("model"));
            return true;
        }

        /// <summary>
        /// 调用 RimTalk.Settings.Get().GetActiveConfig()，负责避开直接引用 RimTalk 程序集。
        /// </summary>
        private static object TryGetActiveConfig(out string message)
        {
            message = "";
            Type settingsType = AccessToolsType("RimTalk.Settings");
            if (settingsType == null)
            {
                message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.NotLoaded");
                return null;
            }

            try
            {
                MethodInfo getMethod = settingsType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
                object rimTalkSettings = getMethod?.Invoke(null, null);
                if (rimTalkSettings == null)
                {
                    message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.NoSettings");
                    return null;
                }

                MethodInfo activeMethod = rimTalkSettings.GetType().GetMethod("GetActiveConfig", BindingFlags.Public | BindingFlags.Instance);
                object activeConfig = activeMethod?.Invoke(rimTalkSettings, null);
                if (activeConfig == null)
                {
                    message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.NoActiveConfig");
                    return null;
                }

                return activeConfig;
            }
            catch (Exception ex)
            {
                message = SimTranslation.T("RSMF.ReviewSettings.RimTalkImport.Exception", ex.Message.Named("message"));
                return null;
            }
        }

        /// <summary>
        /// 读取类型名，负责兼容 Harmony 反射工具不存在时的普通程序集扫描。
        /// </summary>
        private static Type AccessToolsType(string typeName)
        {
            if (typeName == "RimTalk.Settings" && cachedSettingsType != null)
                return cachedSettingsType;

            Type type = HarmonyLib.AccessTools.TypeByName(typeName);
            if (type != null)
            {
                if (typeName == "RimTalk.Settings")
                    cachedSettingsType = type;
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(typeName, false);
                if (type != null)
                {
                    if (typeName == "RimTalk.Settings")
                        cachedSettingsType = type;
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// 从对象字段或属性读取字符串值，负责适配 RimTalk 公开字段结构。
        /// </summary>
        private static string ReadString(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
                return "";

            Type type = target.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(target)?.ToString() ?? "";

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(target, null)?.ToString() ?? "";
        }

        /// <summary>
        /// 解析 RimTalk 模型字段，负责把 Custom 选择映射到真实模型名。
        /// </summary>
        private static string ResolveModel(string selectedModel, string customModel)
        {
            if (string.Equals(selectedModel, "Custom", StringComparison.OrdinalIgnoreCase))
                return customModel ?? "";

            if (string.Equals(selectedModel, "(choose model)", StringComparison.OrdinalIgnoreCase))
                return "";

            return selectedModel ?? "";
        }

        /// <summary>
        /// 根据 RimTalk 供应商解析 OpenAI 兼容入口，负责把内置供应商映射为完整兼容地址。
        /// </summary>
        private static string ResolveEndpoint(string provider, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return "";

            switch (provider)
            {
                case "Google":
                    return "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
                case "OpenAI":
                    return "https://api.openai.com/v1/chat/completions";
                case "DeepSeek":
                    return "https://api.deepseek.com/v1/chat/completions";
                case "Grok":
                    return "https://api.x.ai/v1/chat/completions";
                case "GLM":
                    return "https://api.z.ai/api/paas/v4/chat/completions";
                case "GLMCoding":
                    return "https://api.z.ai/api/coding/paas/v4/chat/completions";
                case "AlibabaIntl":
                    return "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/chat/completions";
                case "AlibabaCN":
                    return "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
                case "OpenRouter":
                    return "https://openrouter.ai/api/v1/chat/completions";
                case "Local":
                case "Custom":
                    return NormalizeRimTalkCustomEndpoint(baseUrl);
                default:
                    return "";
            }
        }

        /// <summary>
        /// 按 RimTalk 的本地接口规则补全地址，负责让只填写根地址的本地模型也能直接导入使用。
        /// </summary>
        private static string NormalizeRimTalkCustomEndpoint(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return "";

            string trimmed = baseUrl.Trim().TrimEnd('/');
            try
            {
                Uri uri = new Uri(trimmed);
                string path = uri.AbsolutePath == null ? "" : uri.AbsolutePath.Trim('/');
                return string.IsNullOrEmpty(path) ? trimmed + "/v1/chat/completions" : trimmed;
            }
            catch
            {
                return trimmed;
            }
        }
    }
}
