using SimManagementLib.Api;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //编辑单项通用服务，确认只修改商店管理窗口的草稿。
    internal sealed class Dialog_ShopServiceEditor : Window
    {
        private readonly string title;
        private readonly ServiceSlotData target;
        private bool enabled, overridePrice;
        private float price;
        private int capacity;
        private string priceBuffer, capacityBuffer;
        private Vector2 scroll;
        private float contentHeight;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(540f, UI.screenWidth - 48f), Mathf.Min(500f, UI.screenHeight - 48f));

        //独立复制编辑字段，取消和关闭按钮均不会提交。
        public Dialog_ShopServiceEditor(string title, ServiceSlotData target)
        {
            this.title = title; this.target = target;
            enabled = target.enabled; overridePrice = target.priceOverrideEnabled;
            price = target.priceOverride; capacity = target.maxSimultaneousUsers;
            priceBuffer = price.ToString(); capacityBuffer = capacity.ToString();
            doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true; draggable = true;
        }

        //标题、滚动表单和确认栏分别保留空间，中文标签按实际字体测量。
        public override void DoWindowContents(Rect rect)
        {
            GameFont font = Text.Font; TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap; Color color = GUI.color; bool guiEnabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                float h = ShopUiVisualUtility.ControlHeight();
                float top = ShopUiVisualUtility.DrawPageHeading(rect, title, SimTranslation.T("RSMF.ServiceEditor.Hint"), true);
                float footer = rect.height - h;
                Rect outer = new Rect(0, top, rect.width, Mathf.Max(0f, footer - top - 12f));
                float width = Mathf.Max(1f, rect.width - 18f);
                Widgets.BeginScrollView(outer, ref scroll, new Rect(0, 0, width, Mathf.Max(outer.height, contentHeight)));
                try
                {
                    float y = 0;
                    Check(width, ref y, SimTranslation.T("RSMF.ShopManager.Enabled"), ref enabled);
                    Check(width, ref y, SimTranslation.T("RSMF.ShopManager.OverrideEnabled"), ref overridePrice);
                    Label(width, ref y, SimTranslation.T("RSMF.ShopManager.OverridePrice"));
                    GUI.enabled = overridePrice;
                    Widgets.TextFieldNumeric(new Rect(0, y, width, h), ref price, ref priceBuffer, 0f, 99999f);
                    GUI.enabled = guiEnabled; y += h + 12f;
                    Label(width, ref y, SimTranslation.T("RSMF.ShopManager.Capacity"));
                    Widgets.TextFieldNumeric(new Rect(0, y, width, h), ref capacity, ref capacityBuffer, 1, 99);
                    y += h + 12f;
                    contentHeight = y;
                }
                finally { Widgets.EndScrollView(); }
                float buttonWidth = Mathf.Min(180f, (rect.width - 8f) / 2f);
                if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(0, footer, buttonWidth, h), SimTranslation.T("RSMF.Common.Cancel"))) Close();
                if (ShopUiVisualUtility.DrawPrimaryButton(new Rect(rect.width - buttonWidth, footer, buttonWidth, h), SimTranslation.T("RSMF.ServiceEditor.Apply")))
                {
                    target.enabled = enabled; target.priceOverrideEnabled = overridePrice;
                    target.priceOverride = price; target.maxSimultaneousUsers = capacity;
                    target.priceBuffer = priceBuffer; target.capacityBuffer = capacityBuffer;
                    Close();
                }
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; GUI.enabled = guiEnabled; }
        }

        //长标签完整换行，不挤压复选框。
        private static void Check(float width, ref float y, string text, ref bool value)
        {
            float h = Mathf.Max(ShopUiVisualUtility.ControlHeight(), Text.CalcHeight(text, Mathf.Max(1f, width - 32f)) + 8f);
            Widgets.CheckboxLabeled(new Rect(0, y, width, h), text, ref value);
            y += h + 8f;
        }

        //数字输入的标签独占一行，适应不同语言宽度。
        private static void Label(float width, ref float y, string text)
        {
            float h = Mathf.Max(Text.LineHeight, Text.CalcHeight(text, width));
            Widgets.Label(new Rect(0, y, width, h), text); y += h + 6f;
        }
    }
}
