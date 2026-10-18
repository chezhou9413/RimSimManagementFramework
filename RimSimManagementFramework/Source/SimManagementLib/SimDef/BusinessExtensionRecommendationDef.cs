using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimDef
{
    //提供推荐扩展展示和检测所需字段，职责是统一推荐条目的 UI 数据接口。
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
    //声明经商管理推荐扩展，职责是让 XML 配置官方分类、扩展入口、检测条件和展示素材。
    public class BusinessExtensionRecommendationDef : Def, IBusinessExtensionRecommendation
    {
        public int order;
        public bool official;
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
        //返回扩展显示名称，负责优先使用翻译并在缺失时回退到 Def 标签。
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
        //返回扩展简介，负责优先使用翻译并在缺失时回退到 Def 描述。
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
