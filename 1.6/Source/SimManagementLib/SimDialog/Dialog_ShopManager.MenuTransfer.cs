using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        private const float TransferScrollbarWidth = 16f;
        private const float TransferCloseXReservedWidth = Widgets.CloseButtonSize + Widgets.CloseButtonMargin * 2f + 18f;

        /// <summary>
        /// 打开指定商店区域的套餐传输选项，负责把导入导出入口放到区划 Gizmo 工作流中。
        /// </summary>
        public static void OpenShopMenuTransferOptions(Zone_Shop zone)
        {
            if (zone == null)
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("导出套餐", delegate { OpenShopMenuExportDialog(zone); }),
                new FloatMenuOption("导入套餐", delegate { OpenShopMenuImportDialog(zone); })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// 打开套餐导出窗口，负责把指定商店套餐转换为可复制 Base64 文本。
        /// </summary>
        private static void OpenShopMenuExportDialog(Zone_Shop zone)
        {
            string exportText = ShopMenuTransferUtility.ExportBase64(GetZoneCombos(zone));
            Find.WindowStack.Add(new Dialog_ShopMenuExport(exportText));
        }

        /// <summary>
        /// 打开套餐导入窗口，负责接收 Base64 文本并合并到指定商店套餐列表。
        /// </summary>
        private static void OpenShopMenuImportDialog(Zone_Shop zone)
        {
            Find.WindowStack.Add(new Dialog_ShopMenuImport(result => ImportShopMenuCombos(zone, result)));
        }

        /// <summary>
        /// 返回指定商店区域的套餐列表，负责统一窗口和 Gizmo 两条入口的数据源。
        /// </summary>
        private static List<ComboData> GetZoneCombos(Zone_Shop zone)
        {
            GameComponent_ShopComboManager comboManager = Current.Game?.GetComponent<GameComponent_ShopComboManager>();
            return zone != null && comboManager != null ? comboManager.GetCombosForZone(zone) : new List<ComboData>();
        }

        /// <summary>
        /// 导入套餐并刷新导航，负责合并追加套餐并显示结果。
        /// </summary>
        private static List<ComboData> ImportShopMenuCombos(Zone_Shop zone, ShopMenuImportResult result)
        {
            if (result == null || result.Combos.NullOrEmpty())
            {
                Messages.Message("没有可导入的有效套餐。", MessageTypeDefOf.RejectInput, false);
                return new List<ComboData>();
            }

            List<ComboData> zoneCombos = GetZoneCombos(zone);
            List<ComboData> added = ShopMenuTransferUtility.MergeImportedCombos(zoneCombos, result.Combos);
            if (added.Count > 0)
                SimShopUiApi.RequestRefresh();

            string message = "导入套餐 " + added.Count + " 个";
            if (result.SkippedItemCount > 0 || result.SkippedComboCount > 0)
                message += "，跳过物品 " + result.SkippedItemCount + " 个，跳过空套餐 " + result.SkippedComboCount + " 个";
            if (result.MissingThingDefNames.Count > 0)
                message += "，缺失物品：" + string.Join(", ", result.MissingThingDefNames.Take(5).ToArray());

            Messages.Message(message, added.Count > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
            return added;
        }

        /// <summary>
        /// 套餐导出窗口，负责展示 Base64 文本并提供复制操作。
        /// </summary>
        private sealed class Dialog_ShopMenuExport : Window
        {
            private string exportText;
            private Vector2 scroll;

            public override Vector2 InitialSize => new Vector2(900f, 520f);

            /// <summary>
            /// 使用导出文本初始化窗口，负责设置模态输入行为。
            /// </summary>
            public Dialog_ShopMenuExport(string exportText)
            {
                this.exportText = exportText ?? string.Empty;
                forcePause = true;
                absorbInputAroundWindow = true;
                doCloseX = true;
                closeOnClickedOutside = false;
            }

            /// <summary>
            /// 绘制导出窗口内容，负责避免标题、文本区和底部按钮重合。
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
                    Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - TransferCloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), "导出套餐");

                    Text.Font = GameFont.Tiny;
                    string info = "复制下方 Base64 文本，可以导入到其他店铺。当前导出范围只包含套餐。";
                    float infoHeight = Mathf.Ceil(Text.CalcHeight(info, inRect.width - TransferCloseXReservedWidth)) + 4f;
                    Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width - TransferCloseXReservedWidth, infoHeight), info);

                    Rect textRect = new Rect(inRect.x, inRect.y + 44f + infoHeight, inRect.width, Mathf.Max(90f, inRect.height - infoHeight - 100f));
                    Widgets.DrawBoxSolid(textRect, new Color(0f, 0f, 0f, 0.22f));
                    SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));

                    Rect scrollRect = textRect.ContractedBy(4f);
                    Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, scrollRect.width - TransferScrollbarWidth), Mathf.Max(scrollRect.height, Text.CalcHeight(exportText, scrollRect.width - 24f) + 12f));
                    Widgets.BeginScrollView(scrollRect, ref scroll, viewRect);
                    exportText = Widgets.TextArea(viewRect, exportText);
                    Widgets.EndScrollView();

                    if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 140f, inRect.yMax - 38f, 140f, 32f), "复制到剪贴板", true, GameFont.Tiny))
                    {
                        GUIUtility.systemCopyBuffer = exportText;
                        Messages.Message("套餐 Base64 已复制。", MessageTypeDefOf.PositiveEvent, false);
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
        /// 套餐导入窗口，负责接收 Base64 文本并提交解析结果。
        /// </summary>
        private sealed class Dialog_ShopMenuImport : Window
        {
            private readonly Action<ShopMenuImportResult> importAction;
            private string importText = string.Empty;
            private Vector2 scroll;

            public override Vector2 InitialSize => new Vector2(900f, 540f);

            /// <summary>
            /// 使用导入回调初始化窗口，负责设置模态输入行为。
            /// </summary>
            public Dialog_ShopMenuImport(Action<ShopMenuImportResult> importAction)
            {
                this.importAction = importAction;
                forcePause = true;
                absorbInputAroundWindow = true;
                doCloseX = true;
                closeOnClickedOutside = false;
            }

            /// <summary>
            /// 绘制导入窗口内容，负责提供粘贴和确认导入操作。
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
                    Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - TransferCloseXReservedWidth, Text.LineHeightOf(GameFont.Medium) + 4f), "导入套餐");

                    Text.Font = GameFont.Tiny;
                    string info = "粘贴套餐 Base64。导入会合并追加到当前店铺，已有套餐不会被清空。";
                    float infoHeight = Mathf.Ceil(Text.CalcHeight(info, inRect.width - TransferCloseXReservedWidth)) + 4f;
                    Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width - TransferCloseXReservedWidth, infoHeight), info);

                    Rect textRect = new Rect(inRect.x, inRect.y + 44f + infoHeight, inRect.width, Mathf.Max(90f, inRect.height - infoHeight - 100f));
                    Widgets.DrawBoxSolid(textRect, new Color(0f, 0f, 0f, 0.22f));
                    SimUiStyle.DrawBorder(textRect, new Color(1f, 1f, 1f, 0.10f));

                    Rect scrollRect = textRect.ContractedBy(4f);
                    Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, scrollRect.width - TransferScrollbarWidth), Mathf.Max(scrollRect.height, Text.CalcHeight(importText, scrollRect.width - 24f) + 12f));
                    Widgets.BeginScrollView(scrollRect, ref scroll, viewRect);
                    importText = Widgets.TextArea(viewRect, importText);
                    Widgets.EndScrollView();

                    if (SimUiStyle.DrawSecondaryButton(new Rect(inRect.x, inRect.yMax - 38f, 140f, 32f), "从剪贴板粘贴", true, GameFont.Tiny))
                        importText = GUIUtility.systemCopyBuffer ?? string.Empty;

                    if (SimUiStyle.DrawPrimaryButton(new Rect(inRect.xMax - 140f, inRect.yMax - 38f, 140f, 32f), "确认导入", true, GameFont.Tiny))
                    {
                        if (!ShopMenuTransferUtility.TryImportBase64(importText, out ShopMenuImportResult result, out string error))
                        {
                            Messages.Message(error ?? "套餐 Base64 解析失败。", MessageTypeDefOf.RejectInput, false);
                            return;
                        }

                        importAction?.Invoke(result);
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
}
