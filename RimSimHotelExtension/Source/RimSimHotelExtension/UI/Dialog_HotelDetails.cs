using System;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //承载当前条目的二级表单或只读明细，确认只回写主窗口草稿。
    internal sealed class Dialog_HotelDetails : Window
    {
        private readonly string title;
        private readonly Func<float, float> draw;
        private readonly Action confirm;
        private readonly PageScroll scroll = new PageScroll();

        public override Vector2 InitialSize => new Vector2(Mathf.Min(620f, Verse.UI.screenWidth - 48f), Mathf.Min(620f, Verse.UI.screenHeight - 48f));

        //编辑内容由调用方复制到局部变量，关闭或取消不会写入父草稿。
        public Dialog_HotelDetails(string title, Func<float, float> draw, Action confirm = null)
        {
            this.title = title; this.draw = draw; this.confirm = confirm;
            doCloseX = true;
            closeOnAccept = false;
            absorbInputAroundWindow = true;
            draggable = true;
        }

        //先保留标题和页脚，再为正文分配独立滚动区。
        public override void DoWindowContents(Rect rect)
        {
            using var scope = new HotelUi();
            float top = ShopUiVisualUtility.DrawPageHeading(rect, title,
                HotelText.T(confirm == null ? "RSH.Ui.DetailHint" : "RSH.Ui.EditHint"), true);
            float footer = rect.yMax - HotelUi.Line;
            HotelUi.Scroll(new Rect(rect.x, rect.y + top, rect.width, Mathf.Max(0, footer - rect.y - top - 12f)),
                scroll, width => scroll.height = draw(width) + 8f);
            float buttonWidth = Mathf.Min(180f, (rect.width - 8f) / 2f);
            if (HotelUi.Button(new Rect(rect.x, footer, buttonWidth, HotelUi.Line),
                HotelText.T(confirm == null ? "RSH.Ui.Close" : "RSH.Ui.Cancel"))) Close();
            if (confirm != null && HotelUi.Button(new Rect(rect.xMax - buttonWidth, footer, buttonWidth, HotelUi.Line),
                    HotelText.T("RSH.Ui.ApplyDraft"), true)) { confirm(); Close(); }
        }
    }
}
