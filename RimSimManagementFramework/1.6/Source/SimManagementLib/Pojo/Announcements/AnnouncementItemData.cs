using SimManagementLib.SimDef;
using System.Collections.Generic;

namespace SimManagementLib.Pojo
{
    //保存本地公告的展示快照，职责是固定弹窗打开时的语言文本和版本信息。
    public sealed class AnnouncementItemData
    {
        public readonly string announcementCode;
        public readonly string title;
        public readonly string body;
        public readonly string readKey;
        public readonly string publishedAt;
        public readonly bool popupOnce;
        public readonly string workshopUrl;
        public readonly List<string> screenshotPaths;

        //从公告定义建立快照，职责是统一弹窗展示与已读历史的数据来源。
        public AnnouncementItemData(AnnouncementDef definition)
        {
            announcementCode = definition.defName;
            title = definition.DisplayTitle;
            body = definition.DisplayBody;
            readKey = definition.ReadKey;
            publishedAt = definition.publishedAt;
            popupOnce = definition.popupOnce;
            workshopUrl = definition.workshopUrl;
            screenshotPaths = new List<string>(definition.screenshotPaths);
        }
    }
}
