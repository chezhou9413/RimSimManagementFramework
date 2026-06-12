using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 提供推荐扩展展示和检测所需字段，负责让本地 Def 与服务端数据共用 UI 逻辑。
    /// </summary>
    public interface IBusinessExtensionRecommendation
    {
        int Order { get; }
        string StableId { get; }
        string DisplayLabel { get; }
        string DisplayDescription { get; }
        List<string> PackageIds { get; }
        string PublishedFileId { get; }
        string WorkshopUrl { get; }
        string PreviewTexturePath { get; }
        string PreviewImageUrl { get; }
    }

    /// <summary>
    /// 声明经商管理推荐扩展，负责让 XML 配置扩展入口、检测条件和展示素材。
    /// </summary>
    public class BusinessExtensionRecommendationDef : Def, IBusinessExtensionRecommendation
    {
        public int order;
        public string labelKey = "";
        public string descriptionKey = "";
        public List<string> packageIds = new List<string>();
        public string publishedFileId = "";
        public string workshopUrl = "";
        public string previewTexturePath = "";
        public string previewImageUrl = "";

        public int Order => order;
        public string StableId => defName;
        public List<string> PackageIds => packageIds;
        public string PublishedFileId => publishedFileId;
        public string WorkshopUrl => workshopUrl;
        public string PreviewTexturePath => previewTexturePath;
        public string PreviewImageUrl => previewImageUrl;

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
