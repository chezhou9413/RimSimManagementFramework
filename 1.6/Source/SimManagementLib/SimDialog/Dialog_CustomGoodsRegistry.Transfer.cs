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
        /// 显示可分享的自定义商品数据库 Base64 导出内容。
        /// </summary>
        private sealed class Dialog_CustomGoodsTransfer : Window
        {
            private string exportText;
            private Vector2 scroll;

            public override Vector2 InitialSize => new Vector2(900f, 520f);

            /// <summary>
            /// 使用序列化后的自定义商品文本初始化导出窗口。
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
            /// 绘制导出文本区域和复制到剪贴板操作。
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
        /// 接收 Base64 数据，并按增量合并或覆盖替换模式导入自定义商品数据库。
        /// </summary>
        private sealed class Dialog_CustomGoodsImport : Window
        {
            private readonly Action<CustomGoodsDatabaseData, bool> importAction;
            private string importText = string.Empty;
            private Vector2 scroll;
            private bool replaceExisting;

            public override Vector2 InitialSize => new Vector2(900f, 560f);

            /// <summary>
            /// 使用接收解析结果和导入模式的回调初始化导入窗口。
            /// </summary>
            public Dialog_CustomGoodsImport(Action<CustomGoodsDatabaseData, bool> importAction)
            {
                this.importAction = importAction;
                forcePause = true;
                absorbInputAroundWindow = true;
                doCloseX = true;
                closeOnClickedOutside = false;
            }

            /// <summary>
            /// 绘制导入模式切换、文本区域、粘贴操作和确认操作。
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
                    Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - CloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), "导入 Base64");

                    Text.Font = GameFont.Tiny;
                    string infoText = replaceExisting
                        ? "覆盖替换会用导入内容替换玩家本地所有自定义商品数据，但不会改动任何 GoodsDef 配置。"
                        : "增量合并会保留本地现有商品类型和关联，并把导入内容按类型 ID 追加合并。";
                    float infoHeight = Mathf.Ceil(Text.CalcHeight(infoText, inRect.width)) + 4f;
                    Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, infoHeight), infoText);

                    Rect modeRect = new Rect(inRect.x, inRect.y + 42f + infoHeight, inRect.width, 34f);
                    DrawImportModeSelector(modeRect);

                    Rect textRect = new Rect(inRect.x, modeRect.yMax + 8f, inRect.width, Mathf.Max(90f, inRect.height - infoHeight - 150f));
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

                        importAction?.Invoke(data, replaceExisting);
                        Close();
                        Messages.Message(replaceExisting ? "自定义商品数据已导入并覆盖本地内容。" : "自定义商品数据已增量合并到本地内容。", MessageTypeDefOf.PositiveEvent, false);
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

            /// <summary>
            /// 绘制导入模式选择控件。
            /// </summary>
            private void DrawImportModeSelector(Rect rect)
            {
                float buttonWidth = 124f;
                Rect mergeRect = new Rect(rect.x, rect.y, buttonWidth, 30f);
                Rect replaceRect = new Rect(mergeRect.xMax + 8f, rect.y, buttonWidth, 30f);

                bool mergeSelected = !replaceExisting;
                if ((mergeSelected ? SimUiStyle.DrawPrimaryButton(mergeRect, "增量合并", true, GameFont.Tiny) : SimUiStyle.DrawSecondaryButton(mergeRect, "增量合并", true, GameFont.Tiny)))
                    replaceExisting = false;

                if ((replaceExisting ? SimUiStyle.DrawPrimaryButton(replaceRect, "覆盖替换", true, GameFont.Tiny) : SimUiStyle.DrawSecondaryButton(replaceRect, "覆盖替换", true, GameFont.Tiny)))
                    replaceExisting = true;

                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.73f, 0.77f, 0.82f, 1f);
                Widgets.Label(new Rect(replaceRect.xMax + 12f, rect.y + 6f, rect.width - replaceRect.width - mergeRect.width - 28f, 20f), replaceExisting ? "用导入内容替换本地自定义商品。" : "保留本地内容，只追加导入内容。");
                GUI.color = Color.white;
            }
        }
    }
}
