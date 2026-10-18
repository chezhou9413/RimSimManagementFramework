using System;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //在独立窗口复制或粘贴餐厅设置，导入只替换父窗口草稿。
    internal sealed class Dialog_RestaurantSettingsTransfer : Window
    {
        private readonly RestaurantShopSettings target;
        private readonly bool importing;
        private readonly Action imported;
        private string text;
        private string error = "";
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(860f, Verse.UI.screenWidth - 48f), Mathf.Min(600f, Verse.UI.screenHeight - 48f));

        //导出使用当前草稿快照，导入保持空文本等待粘贴。
        public Dialog_RestaurantSettingsTransfer(RestaurantShopSettings target, bool importing, Action imported = null)
        {
            this.target = target;
            this.importing = importing;
            this.imported = imported;
            text = importing ? "" : RestaurantSettingsTransferUtility.Export(target);
            doCloseX = true;
            closeOnAccept = false;
            absorbInputAroundWindow = true;
        }

        //给标题、错误说明和操作栏分别预留空间，长文本使用滚动区域。
        public override void DoWindowContents(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                float y = ShopUiVisualUtility.DrawPageHeading(rect,
                    SimTranslation.T(importing ? "RSR.Transfer.Import" : "RSR.Transfer.Export"),
                    SimTranslation.T(importing ? "RSR.Transfer.ImportHint" : "RSR.Transfer.ExportHint"), true);
                float h = RestaurantUiStyle.ControlHeight();
                float footer = rect.yMax - h;
                float errorHeight = error.NullOrEmpty() ? 0f : Text.CalcHeight(error, rect.width) + 8f;
                var area = new Rect(rect.x, y, rect.width, Mathf.Max(0f, footer - y - errorHeight - 12f));
                var view = new Rect(0, 0, area.width - 16f, Mathf.Max(area.height, Text.CalcHeight(text, area.width - 24f) + 12f));
                Widgets.BeginScrollView(area, ref scroll, view);
                try { text = Widgets.TextArea(view, text); }
                finally { Widgets.EndScrollView(); }
                if (!error.NullOrEmpty())
                {
                    GUI.color = RestaurantUiStyle.Warning;
                    Widgets.Label(new Rect(rect.x, footer - errorHeight - 4f, rect.width, errorHeight), error);
                    GUI.color = Color.white;
                }
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.x, footer, 140f, h),
                    SimTranslation.T(importing ? "RSR.Transfer.Paste" : "RSR.Transfer.Copy")))
                {
                    if (importing) { text = GUIUtility.systemCopyBuffer ?? ""; error = ""; }
                    else GUIUtility.systemCopyBuffer = text;
                }
                if (importing && RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.xMax - 180f, footer, 180f, h),
                    SimTranslation.T("RSR.Transfer.Apply"))) Import();
            }
        }

        //全部校验通过后才替换草稿，并保留当前店铺身份。
        private void Import()
        {
            if (!RestaurantSettingsTransferUtility.TryImport(text, out var settings, out error)) return;
            settings.shopZoneId = target.shopZoneId;
            target.CopyFrom(settings);
            imported?.Invoke();
            Messages.Message(SimTranslation.T("RSR.Transfer.Imported"), MessageTypeDefOf.PositiveEvent, false);
            Close();
        }
    }
}
