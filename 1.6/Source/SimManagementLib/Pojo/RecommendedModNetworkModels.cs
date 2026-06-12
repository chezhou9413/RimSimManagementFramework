using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存服务端推荐 Mod 数据，负责替代本地 Def 作为扩展推荐页数据源。
    /// </summary>
    [DataContract]
    public sealed class RecommendedModNetworkItemData : IBusinessExtensionRecommendation
    {
        [DataMember] public string defName = "";
        [DataMember] public int order;
        [DataMember] public string label = "";
        [DataMember] public string description = "";
        [DataMember] public List<string> packageIds = new List<string>();
        [DataMember] public string publishedFileId = "";
        [DataMember] public string workshopUrl = "";
        [DataMember] public string previewTexturePath = "";
        [DataMember] public string previewImageUrl = "";

        public int Order => order;
        public string StableId => defName;
        public string DisplayLabel => string.IsNullOrWhiteSpace(label) ? defName : label;
        public string DisplayDescription => description ?? "";
        public List<string> PackageIds => packageIds;
        public string PublishedFileId => publishedFileId;
        public string WorkshopUrl => workshopUrl;
        public string PreviewTexturePath => previewTexturePath;
        public string PreviewImageUrl => previewImageUrl;

        /// <summary>
        /// 清理服务端推荐字段，负责避免坏文本进入 UI 和状态检测。
        /// </summary>
        public void Sanitize()
        {
            defName = StringEncodingUtility.SanitizeUtf16(defName);
            label = StringEncodingUtility.SanitizeUtf16(label);
            description = StringEncodingUtility.SanitizeUtf16(description);
            publishedFileId = StringEncodingUtility.SanitizeUtf16(publishedFileId);
            workshopUrl = StringEncodingUtility.SanitizeUtf16(workshopUrl);
            previewTexturePath = StringEncodingUtility.SanitizeUtf16(previewTexturePath);
            previewImageUrl = StringEncodingUtility.SanitizeUtf16(previewImageUrl);
            if (packageIds == null)
                packageIds = new List<string>();
            for (int i = packageIds.Count - 1; i >= 0; i--)
            {
                packageIds[i] = StringEncodingUtility.SanitizeUtf16(packageIds[i]);
                if (string.IsNullOrWhiteSpace(packageIds[i]))
                    packageIds.RemoveAt(i);
            }
        }
    }
}
