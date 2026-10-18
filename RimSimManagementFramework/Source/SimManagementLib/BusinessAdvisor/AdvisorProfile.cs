using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using SimManagementLib.Tool;

namespace SimManagementLib.BusinessAdvisor
{
    //人格文件只包含角色信息，不能导入接口凭据或预先批准操作。
    [DataContract]
    internal sealed class AdvisorProfile
    {
        [DataMember] public string name;
        [DataMember] public string personality;
        [DataMember] public string expectations;

        //限制导入大小并验证字段，然后一次性应用人格配置。
        public static void Import(string path, SimManagementLibSettings settings)
        {
            var file = new FileInfo(path.Trim().Trim('"'));
            if (!file.Exists) throw new InvalidOperationException("找不到人格文件。");
            if (file.Length > 65536) throw new InvalidOperationException("人格文件不能超过 64 KiB。");
            string text = File.ReadAllText(file.FullName, Encoding.UTF8);
            AdvisorProfile profile = file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? AdvisorJson.Read<AdvisorProfile>(text) : new AdvisorProfile { personality = text };
            if (profile == null || (profile.name == null && profile.personality == null && profile.expectations == null))
                throw new InvalidOperationException("人格 JSON 需要 name、personality 或 expectations 字段。");
            if ((profile.name?.Length ?? 0) > 80 || (profile.personality?.Length ?? 0) > 8000 || (profile.expectations?.Length ?? 0) > 8000)
                throw new InvalidOperationException("名称上限 80 字符，人格和期望各上限 8000 字符。");
            if (profile.name != null) settings.advisorName = profile.name;
            if (profile.personality != null) settings.advisorPersonality = profile.personality;
            if (profile.expectations != null) settings.advisorExpectations = profile.expectations;
        }

        //生成稳定人格块；时间和经营快照由后续查询消息追加。
        public static string Prompt(SimManagementLibSettings settings) => "玩家语言=" + SimTranslation.ActiveLanguageDisplayName
            + "\n人格设定=" + AdvisorJson.Write(new AdvisorProfile { name = settings.advisorName,
                personality = settings.advisorPersonality, expectations = settings.advisorExpectations });

        //使用独立模型，按需继承统一连接或覆盖为顾问专用供应商。
        public static SimManagementLibSettings Connection(SimManagementLibSettings source)
        {
            var copy = SimLlmUtility.BuildRequestSettings(source, true);
            copy.advisorJsonMode = source.advisorJsonMode;
            if (source.advisorSeparateConnection)
            {
                copy.llmProvider = source.advisorProvider;
                copy.llmOpenAiBaseUrl = source.advisorBaseUrl;
                copy.llmOpenAiApiKey = source.advisorApiKey;
                copy.llmAnthropicApiKey = source.advisorApiKey;
            }
            if (!string.IsNullOrWhiteSpace(source.advisorModel))
            {
                copy.llmOpenAiModel = source.advisorModel.Trim();
                copy.llmAnthropicModel = source.advisorModel.Trim();
            }
            else if (source.advisorSeparateConnection)
                throw new InvalidOperationException("顾问独立接口需要填写模型名称。");
            copy.SyncLegacyReviewAiConnectionFields();
            return copy;
        }
    }
}
