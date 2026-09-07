using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.SimDef
{
    //声明经商管理教程页数据，负责让 XML 配置教程标题、正文和居中图片。
    public class BusinessTutorialDef : Def
    {
        public int order;
        public string titleKey = "";
        public string title = "";
        public string textBeforeImageKey = "";
        public string textBeforeImage = "";
        public string imagePath = "";
        public float imageMaxHeight = 180f;
        public string textAfterImageKey = "";
        public string textAfterImage = "";

        //返回教程标题，负责优先读取翻译并在缺失时回退到 XML 直写文本。
        public string DisplayTitle
        {
            get
            {
                return ResolveText(titleKey, title, LabelCap.RawText);
            }
        }

        //返回图片前正文，负责兼容翻译键和 XML 直写文本。
        public string DisplayTextBeforeImage
        {
            get
            {
                return ResolveText(textBeforeImageKey, textBeforeImage, "");
            }
        }

        //返回图片后正文，负责兼容翻译键和 XML 直写文本。
        public string DisplayTextAfterImage
        {
            get
            {
                return ResolveText(textAfterImageKey, textAfterImage, "");
            }
        }

        //解析教程文本，负责统一处理翻译键、XML 文本和兜底值。
        private static string ResolveText(string key, string directText, string fallback)
        {
            string direct = string.IsNullOrWhiteSpace(directText) ? fallback ?? "" : directText;
            if (string.IsNullOrWhiteSpace(key))
                return direct;

            return SimTranslation.TOrFallback(key, direct);
        }
    }
}
