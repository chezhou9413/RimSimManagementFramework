using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 负责显示招牌分享文本，并提供复制到剪贴板的操作。
    /// </summary>
    public sealed class Dialog_CustomSignTransfer : Window
    {
        private const float ScrollbarWidth = 16f;
        private const float CloseXReservedWidth = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 18f;
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f, 1f);

        private string exportText;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(940f, 540f);

        /// <summary>
        /// 负责初始化招牌导出窗口。
        /// </summary>
        public Dialog_CustomSignTransfer(string exportText)
        {
            this.exportText = exportText ?? string.Empty;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnClickedOutside = false;
        }

        /// <summary>
        /// 负责绘制导出文本框和复制按钮。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                Widgets.DrawBoxSolid(inRect, new Color(0.10f, 0.11f, 0.13f, 1f));
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), SimTranslation.T("RSMF.CustomSign.ExportTitle"));

                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                string info = SimTranslation.T("RSMF.CustomSign.ExportInfo");
                float infoHeight = Mathf.Ceil(Text.CalcHeight(info, inRect.width - CloseXReservedWidth)) + 4f;
                Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width - CloseXReservedWidth, infoHeight), info);

                Rect textRect = new Rect(inRect.x, inRect.y + 48f + infoHeight, inRect.width, Mathf.Max(100f, inRect.height - infoHeight - 108f));
                Widgets.DrawBoxSolid(textRect, PanelBg);
                SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));

                Rect scrollRect = textRect.ContractedBy(4f);
                Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, scrollRect.width - ScrollbarWidth), Mathf.Max(scrollRect.height, Text.CalcHeight(exportText, scrollRect.width - 24f) + 12f));
                Widgets.BeginScrollView(scrollRect, ref scroll, viewRect);
                exportText = Widgets.TextArea(viewRect, exportText);
                Widgets.EndScrollView();

                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 140f, inRect.yMax - 38f, 140f, 32f), SimTranslation.T("RSMF.CustomSign.CopyClipboard")))
                {
                    GUIUtility.systemCopyBuffer = exportText;
                    Messages.Message(SimTranslation.T("RSMF.CustomSign.ShareTextCopied"), MessageTypeDefOf.PositiveEvent, false);
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }
    }

    /// <summary>
    /// 负责接收招牌分享文本，并把解析结果回传给调用方。
    /// </summary>
    public sealed class Dialog_CustomSignImport : Window
    {
        private const float ScrollbarWidth = 16f;
        private const float CloseXReservedWidth = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 18f;
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f, 1f);

        private readonly Action<SignFaceData, SignFaceData, SignFaceData> importAction;
        private string importText = string.Empty;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(940f, 580f);

        /// <summary>
        /// 负责初始化招牌导入窗口。
        /// </summary>
        public Dialog_CustomSignImport(Action<SignFaceData, SignFaceData, SignFaceData> importAction)
        {
            this.importAction = importAction;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnClickedOutside = false;
        }

        /// <summary>
        /// 负责绘制导入文本框、剪贴板粘贴和确认导入按钮。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Color oldColor = GUI.color;

            try
            {
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                Widgets.DrawBoxSolid(inRect, new Color(0.10f, 0.11f, 0.13f, 1f));
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), SimTranslation.T("RSMF.CustomSign.ImportTitle"));

                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                string info = SimTranslation.T("RSMF.CustomSign.ImportInfo");
                float infoHeight = Mathf.Ceil(Text.CalcHeight(info, inRect.width - CloseXReservedWidth)) + 4f;
                Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width - CloseXReservedWidth, infoHeight), info);

                Rect textRect = new Rect(inRect.x, inRect.y + 48f + infoHeight, inRect.width, Mathf.Max(120f, inRect.height - infoHeight - 122f));
                Widgets.DrawBoxSolid(textRect, PanelBg);
                SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));

                Rect scrollRect = textRect.ContractedBy(4f);
                Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, scrollRect.width - ScrollbarWidth), Mathf.Max(scrollRect.height, Text.CalcHeight(importText, scrollRect.width - 24f) + 12f));
                Widgets.BeginScrollView(scrollRect, ref scroll, viewRect);
                importText = Widgets.TextArea(viewRect, importText);
                Widgets.EndScrollView();

                if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.x, inRect.yMax - 38f, 140f, 32f), SimTranslation.T("RSMF.CustomSign.PasteClipboard")))
                    importText = GUIUtility.systemCopyBuffer ?? string.Empty;

                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 140f, inRect.yMax - 38f, 140f, 32f), SimTranslation.T("RSMF.CustomSign.ConfirmImport")))
                {
                    if (!SignImageLibrary.TryImportShareText(importText, out SignFaceData south, out SignFaceData east, out SignFaceData north, out string error))
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    importAction?.Invoke(south, east, north);
                    Close();
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }
    }
}
