using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimDef
{
    //声明本地公告，职责是让模组通过 Def 配置正文、版本、展示顺序和发布状态。
    public sealed class AnnouncementDef : Def
    {
        public bool enabled = true;
        public int revision = 1;
        public int order;
        public string publishedAt = "";
        public string titleKey = "";
        public string bodyKey = "";
        public bool popupOnce;
        public bool mainMenuOnly;
        public string workshopUrl = "";
        public List<string> screenshotPaths = new List<string>();

        //返回当前语言标题，职责是在未指定翻译键时使用 Def 标签。
        public string DisplayTitle => titleKey.NullOrEmpty() ? LabelCap.ToString() : SimTranslation.T(titleKey);

        //返回当前语言正文，职责是在未指定翻译键时使用 Def 描述。
        public string DisplayBody => bodyKey.NullOrEmpty() ? description : SimTranslation.T(bodyKey);

        //生成本地公告已读标识，职责是区分永久单次提醒和按版本重新提醒。
        public string ReadKey => "local:" + defName + ":" + (popupOnce ? "once" : revision.ToString());
    }
}
