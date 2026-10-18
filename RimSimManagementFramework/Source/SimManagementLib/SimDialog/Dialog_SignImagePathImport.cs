using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //从图片路径导入图库，错误留在窗口内方便直接修正路径。
    public sealed class Dialog_SignImagePathImport : Window
    {
        private readonly Action<SignImageRecord> importAction;
        private string path = "";
        private string error = "";
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(780f, UI.screenWidth - 48f), Mathf.Min(400f, UI.screenHeight - 48f));

        //保持输入框打开，只有成功导入才返回图片记录。
        public Dialog_SignImagePathImport(Action<SignImageRecord> importAction)
        {
            this.importAction = importAction;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
        }

        //说明、路径和错误随内容滚动，粘贴与导入按钮固定在底部。
        public override void DoWindowContents(Rect rect)
        {
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                float top = ShopUiVisualUtility.DrawPageHeading(rect, SimTranslation.T("RSMF.CustomSign.ImportImageFromPathTitle"), "", true);
                float h = Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 10f);
                Rect area = new Rect(rect.x, top, rect.width, Mathf.Max(0f, rect.yMax - top - h - 12f));
                float width = area.width - 16f;
                string hint = SimTranslation.T("RSMF.CustomSign.ImportImageFromPathInfo");
                float hintHeight = Text.CalcHeight(hint, width);
                float errorHeight = error.NullOrEmpty() ? 0f : Text.CalcHeight(error, width) + 8f;
                Rect view = new Rect(0f, 0f, width, Mathf.Max(area.height, hintHeight + h + errorHeight + 24f));
                Widgets.BeginScrollView(area, ref scroll, view);
                try
                {
                    Widgets.Label(new Rect(0f, 0f, width, hintHeight), hint);
                    path = Widgets.TextField(new Rect(0f, hintHeight + 8f, width, h), path);
                    if (!error.NullOrEmpty())
                    {
                        GUI.color = new Color(1f, 0.65f, 0.4f);
                        Widgets.Label(new Rect(0f, hintHeight + h + 16f, width, errorHeight), error);
                        GUI.color = Color.white;
                    }
                }
                finally { Widgets.EndScrollView(); }
                float half = (rect.width - 8f) / 2f;
                if (SimUiStyle.DrawSecondaryButton(new Rect(rect.x, rect.yMax - h, half, h), SimTranslation.T("RSMF.CustomSign.ReadClipboard")))
                { path = GUIUtility.systemCopyBuffer ?? ""; error = ""; }
                if (SimUiStyle.DrawPrimaryButton(new Rect(rect.xMax - half, rect.yMax - h, half, h), SimTranslation.T("RSMF.CustomSign.Import"))) Import();
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; }
        }

        //解码失败时保留路径，成功后返回图库记录。
        private void Import()
        {
            if (!SignImageLibrary.TryImportFromPath(path, out SignImageRecord record, out error)) return;
            importAction(record);
            Messages.Message(SimTranslation.T("RSMF.CustomSign.ImageImported"), MessageTypeDefOf.PositiveEvent, false);
            Close();
        }
    }
}
