using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 声明经商管理推荐扩展，负责让 XML 配置扩展入口、检测条件和展示素材。
    /// </summary>
    public class BusinessExtensionRecommendationDef : Def
    {
        public int order;
        public string labelKey = "";
        public string descriptionKey = "";
        public List<string> packageIds = new List<string>();
        public string publishedFileId = "";
        public string workshopUrl = "";
        public string previewTexturePath = "";
        public string previewImageUrl = "";

        /// <summary>
        /// 返回扩展显示名称，负责优先使用翻译并在缺失时回退到 Def 标签。
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                string fallback = LabelCap.RawText;
                if (string.IsNullOrEmpty(labelKey))
                    return fallback;
                return SimTranslation.TOrFallback(labelKey, fallback);
            }
        }

        /// <summary>
        /// 返回扩展简介，负责优先使用翻译并在缺失时回退到 Def 描述。
        /// </summary>
        public string DisplayDescription
        {
            get
            {
                if (string.IsNullOrEmpty(descriptionKey))
                    return description ?? "";
                return SimTranslation.TOrFallback(descriptionKey, description ?? "");
            }
        }
    }
}
