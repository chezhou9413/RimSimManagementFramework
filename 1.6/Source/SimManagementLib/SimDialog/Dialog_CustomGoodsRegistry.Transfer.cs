using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_CustomGoodsRegistry
    {
        /// <summary>
        /// Displays exportable Base64 data for sharing the custom goods database.
        /// </summary>
        private sealed class Dialog_CustomGoodsTransfer : Window
        {
            private string exportText;
            private Vector2 scroll;

            public override Vector2 InitialSize => new Vector2(900f, 520f);

            /// <summary>
            /// Initializes the export dialog with the serialized custom goods text.
            /// </summary>
            public Dialog_CustomGoodsTransfer(string exportText)
            {
                this.exportText = exportText ?? string.Empty;
                forcePause = true;
                absorbInputAroundWindow = true;
                doCloseX = true;
                closeOnClickedOutside = false;
            }

            /// <summary>
            /// Draws the export text area and clipboard copy action.
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

                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), "导出 Base64");

                    Text.Font = GameFont.Tiny;
                    float infoHeight = Mathf.Ceil(Text.CalcHeight("下面这串内容已经包含玩家自定义商品类型和商品，可以直接分享给其他玩家。", inRect.width)) + 4f;
                    Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, infoHeight), "下面这串内容已经包含玩家自定义商品类型和商品，可以直接分享给其他玩家。");

                    Rect textRect = new Rect(inRect.x, inRect.y + 42f + infoHeight, inRect.width, Mathf.Max(80f, inRect.height - infoHeight - 94f));
                    Widgets.DrawBoxSolid(textRect, new Color(0f, 0f, 0f, 0.22f));
                    SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));

                    Rect scrollRect = textRect.ContractedBy(4f);
                    Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, textRect.width - ScrollbarWidth), Mathf.Max(scrollRect.height, Text.CalcHeight(exportText, textRect.width - 24f) + 12f));
                    Widgets.BeginScrollView(scrollRect, ref scroll, viewRect);
                    exportText = Widgets.TextArea(viewRect, exportText);
                    Widgets.EndScrollView();

                    if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 130f, inRect.yMax - 38f, 130f, 32f), "复制到剪贴板"))
                    {
                        GUIUtility.systemCopyBuffer = exportText;
                        Messages.Message("Base64 导出内容已复制到剪贴板。", MessageTypeDefOf.PositiveEvent, false);
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
        /// Accepts Base64 data and imports it as the replacement custom goods database.
        /// </summary>
        private sealed class Dialog_CustomGoodsImport : Window
        {
            private readonly Action<CustomGoodsDatabaseData> importAction;
            private string importText = string.Empty;
            private Vector2 scroll;

            public override Vector2 InitialSize => new Vector2(900f, 560f);

            /// <summary>
            /// Initializes the import dialog with the callback that receives parsed data.
            /// </summary>
            public Dialog_CustomGoodsImport(Action<CustomGoodsDatabaseData> importAction)
            {
                this.importAction = importAction;
                forcePause = true;
                absorbInputAroundWindow = true;
                doCloseX = true;
                closeOnClickedOutside = false;
            }

            /// <summary>
            /// Draws the import text area, paste action, and confirmation action.
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

                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), "导入并覆盖");

                    Text.Font = GameFont.Tiny;
                    string infoText = "导入后会覆盖玩家本地所有自定义商品数据，但不会改动任何 GoodsDef 配置。粘贴 Base64 后点击确认导入。";
                    float infoHeight = Mathf.Ceil(Text.CalcHeight(infoText, inRect.width)) + 4f;
                    Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, infoHeight), infoText);

                    Rect textRect = new Rect(inRect.x, inRect.y + 42f + infoHeight, inRect.width, Mathf.Max(90f, inRect.height - infoHeight - 108f));
                    Widgets.DrawBoxSolid(textRect, new Color(0f, 0f, 0f, 0.22f));
                    SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));

                    Rect scrollRect = textRect.ContractedBy(4f);
                    Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, textRect.width - ScrollbarWidth), Mathf.Max(scrollRect.height, Text.CalcHeight(importText, textRect.width - 24f) + 12f));
                    Widgets.BeginScrollView(scrollRect, ref scroll, viewRect);
                    importText = Widgets.TextArea(viewRect, importText);
                    Widgets.EndScrollView();

                    if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.x, inRect.yMax - 38f, 130f, 32f), "从剪贴板粘贴"))
                        importText = GUIUtility.systemCopyBuffer ?? string.Empty;

                    if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 130f, inRect.yMax - 38f, 130f, 32f), "确认导入"))
                    {
                        if (!CustomGoodsDatabase.TryImportBase64(importText, out CustomGoodsDatabaseData data, out string error))
                        {
                            Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                            return;
                        }

                        importAction?.Invoke(data);
                        Close();
                        Messages.Message("自定义商品数据已导入并覆盖本地内容。", MessageTypeDefOf.PositiveEvent, false);
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
}
