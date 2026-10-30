using System;
using SimManagementLib.Tool;
using Verse;

namespace RimSimHotelExtension.Tool
{
    //统一接入原版语言系统，中英文文本均由语言文件提供。
    public static class HotelText
    {
        //按当前游戏语言解析文本及命名参数。
        public static string T(string key, params NamedArgument[] args) => SimTranslation.T(key, args);

        //业务记录保存翻译键；外部店名和框架返回的信息保持原文。
        public static string Display(string value) =>
            value != null && value.StartsWith("RSH.Text.", StringComparison.Ordinal) ? T(value) : value ?? "";
    }
}
