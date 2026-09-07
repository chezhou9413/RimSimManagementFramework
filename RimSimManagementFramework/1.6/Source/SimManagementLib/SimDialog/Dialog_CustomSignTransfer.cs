using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// 负责浏览本地招牌图库，并把选中的图片返回给调用方。
    /// </summary>
    public sealed class Dialog_SignImageBrowser : Window
    {
        private const float ScrollbarWidth = 16f;
        private static readonly Color PanelBg = new Color(0.15f, 0.17f, 0.20f, 0.95f);
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f, 1f);

        private readonly Action<string> selectAction;
        private List<SignImageRecord> images = new List<SignImageRecord>();
        private string searchText = string.Empty;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(860f, 640f);

        /// <summary>
        /// 负责初始化招牌图库浏览窗口。
        /// </summary>
        public Dialog_SignImageBrowser(Action<string> selectAction)
        {
            this.selectAction = selectAction;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnClickedOutside = false;
            ReloadImages();
        }

        /// <summary>
        /// 负责绘制图库搜索、导入和选择列表。
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
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeightOf(GameFont.Medium) + 4f), SimTranslation.T("RSMF.CustomSign.SelectGalleryImage"));

                Rect searchRect = new Rect(inRect.x, inRect.y + 42f, inRect.width - 250f, 30f);
                searchText = Widgets.TextField(searchRect, searchText);
                if (string.IsNullOrEmpty(searchText))
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = MutedText;
                    Widgets.Label(new Rect(searchRect.x + 8f, searchRect.y + 7f, searchRect.width - 16f, Text.LineHeightOf(GameFont.Tiny) + 2f), SimTranslation.T("RSMF.CustomSign.SearchImagePlaceholder"));
                    GUI.color = Color.white;
                }

                if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.xMax - 236f, inRect.y + 41f, 108f, 32f), SimTranslation.T("RSMF.CustomSign.RefreshGallery")))
                    ReloadImages();
                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 120f, inRect.y + 41f, 120f, 32f), SimTranslation.T("RSMF.CustomSign.ImportPath")))
                    Find.WindowStack.Add(new Dialog_SignImagePathImport(delegate(SignImageRecord record)
                    {
                        ReloadImages();
                        selectAction?.Invoke(record.imageId);
                        Close();
                    }));

                Rect listRect = new Rect(inRect.x, inRect.y + 86f, inRect.width, inRect.height - 92f);
                Widgets.DrawBoxSolid(listRect, PanelBg);
                SimUiStyle.DrawBorder(listRect, new Color(1f, 1f, 1f, 0.08f));

                List<SignImageRecord> filtered = images.Where(MatchesSearch).ToList();
                Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, listRect.width - ScrollbarWidth), Mathf.Max(listRect.height, filtered.Count * 68f));
                Widgets.BeginScrollView(listRect.ContractedBy(6f), ref scroll, viewRect);
                float y = 0f;
                for (int i = 0; i < filtered.Count; i++)
                {
                    DrawImageRow(new Rect(0f, y, viewRect.width, 60f), filtered[i]);
                    y += 68f;
                }
                Widgets.EndScrollView();
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private void DrawImageRow(Rect rect, SignImageRecord record)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, Mouse.IsOver(rect) ? 0.05f : 0.02f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.05f));

            Rect iconRect = new Rect(rect.x + 8f, rect.y + 7f, 46f, 46f);
            GUI.DrawTexture(iconRect, SignTextureCache.GetTexture(record.imageId), ScaleMode.ScaleToFit, true);
            SimUiStyle.DrawBorder(iconRect, new Color(1f, 1f, 1f, 0.10f));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 8f, rect.width - 190f, Text.LineHeightOf(GameFont.Small) + 2f), record.label.Truncate(rect.width - 200f));

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            string meta = record.width + "x" + record.height + " / " + record.imageId;
            Widgets.Label(new Rect(iconRect.xMax + 10f, rect.y + 34f, rect.width - 190f, Text.LineHeightOf(GameFont.Tiny) + 2f), meta.Truncate(rect.width - 200f));

            if (SimUiStyle.DrawPrimaryButton(new Rect(rect.xMax - 86f, rect.y + 15f, 76f, 30f), SimTranslation.T("RSMF.CustomSign.Select"), true, GameFont.Tiny))
            {
                selectAction?.Invoke(record.imageId);
                Close();
            }

            GUI.color = Color.white;
        }

        private bool MatchesSearch(SignImageRecord record)
        {
            if (record == null)
                return false;
            if (string.IsNullOrEmpty(searchText))
                return true;
            return (record.label ?? "").IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || (record.imageId ?? "").IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ReloadImages()
        {
            images = SignImageLibrary.Load().images.OrderBy(record => record.label).ThenBy(record => record.imageId).ToList();
        }
    }

    /// <summary>
    /// 负责从玩家输入的本地路径导入招牌图片到图库。
    /// </summary>
    public sealed class Dialog_SignImagePathImport : Window
    {
        private static readonly Color MutedText = new Color(0.73f, 0.77f, 0.82f, 1f);
        private readonly Action<SignImageRecord> importAction;
        private string pathText = string.Empty;

        public override Vector2 InitialSize => new Vector2(760f, 260f);

        /// <summary>
        /// 负责初始化路径导入窗口。
        /// </summary>
        public Dialog_SignImagePathImport(Action<SignImageRecord> importAction)
        {
            this.importAction = importAction;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnClickedOutside = false;
        }

        /// <summary>
        /// 负责绘制路径输入框和导入按钮。
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
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeightOf(GameFont.Medium) + 4f), SimTranslation.T("RSMF.CustomSign.ImportImageFromPathTitle"));

                Text.Font = GameFont.Tiny;
                GUI.color = MutedText;
                string info = SimTranslation.T("RSMF.CustomSign.ImportImageFromPathInfo");
                Widgets.Label(new Rect(inRect.x, inRect.y + 38f, inRect.width, Text.CalcHeight(info, inRect.width) + 4f), info);

                Rect fieldRect = new Rect(inRect.x, inRect.y + 94f, inRect.width, 32f);
                pathText = Widgets.TextField(fieldRect, pathText);

                if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.x, inRect.yMax - 38f, 140f, 32f), SimTranslation.T("RSMF.CustomSign.ReadClipboard")))
                    pathText = GUIUtility.systemCopyBuffer ?? string.Empty;

                if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 120f, inRect.yMax - 38f, 120f, 32f), SimTranslation.T("RSMF.CustomSign.Import")))
                {
                    if (!SignImageLibrary.TryImportFromPath(pathText, out SignImageRecord record, out string error))
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    importAction?.Invoke(record);
                    Messages.Message(SimTranslation.T("RSMF.CustomSign.ImageImported"), MessageTypeDefOf.PositiveEvent, false);
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
