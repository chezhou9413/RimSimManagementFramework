using System;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //管理共享绘制状态，职责是保证窗口和页面退出时恢复字体、对齐、换行与颜色。
    internal sealed class RestaurantGuiScope : IDisposable
    {
        private readonly GameFont font = Text.Font;
        private readonly TextAnchor anchor = Text.Anchor;
        private readonly bool wrap = Text.WordWrap;
        private readonly Color color = GUI.color;

        //设置标准正文绘制状态，职责是让餐厅控件复用框架字体与颜色。
        public RestaurantGuiScope()
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }

        //恢复全局绘制状态，职责是防止餐厅页面影响其他框架页面。
        public void Dispose()
        {
            Text.Font = font;
            Text.Anchor = anchor;
            Text.WordWrap = wrap;
            GUI.color = color;
        }
    }
}
