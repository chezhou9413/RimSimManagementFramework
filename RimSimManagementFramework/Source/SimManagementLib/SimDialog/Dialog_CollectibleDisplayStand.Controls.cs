using System.Collections.Generic;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 收藏品展台管理窗口控件部分，职责是绘制槽位、来源菜单和参数调整控件。
    /// </summary>
    public partial class Dialog_CollectibleDisplayStand
    {
        /// <summary>
        /// 绘制单个槽位格子，并处理点击选择和来源菜单。
        /// </summary>
        private void DrawSlotCell(Rect rect, CollectibleDisplaySlotData slot)
        {
            if (slot == null)
                return;

            bool selected = slot.index == selectedSlotIndex;
            Widgets.DrawBoxSolid(rect, selected ? AccentSoft : PanelAlt);
            SimUiStyle.DrawBorder(rect, selected ? Accent : new Color(1f, 1f, 1f, 0.12f), selected ? 2f : 1f);

            Rect iconRect = rect.ContractedBy(6f);
            if (slot.HasStoredThing)
            {
                Widgets.ThingIcon(iconRect, slot.StoredThing);
            }
            else if (slot.HasPendingSource)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Accent;
                Widgets.Label(iconRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Grid.Pending"));
            }
            else
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = MutedText;
                Widgets.Label(iconRect, "+");
            }

            TooltipHandler.TipRegion(rect, SlotTooltip(slot));
            if (Widgets.ButtonInvisible(rect, false))
            {
                selectedSlotIndex = slot.index;
                if (!slot.HasStoredThing)
                    OpenSourceMenu(slot.index);
            }
            ResetText();
        }

        /// <summary>
        /// 绘制槽位详情和显示参数调整控件。
        /// </summary>
        private void DrawSlotDetails(Rect rect, CollectibleDisplaySlotData slot)
        {
            float y = rect.y;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, y, rect.width, Mathf.Max(28f, Text.LineHeightOf(GameFont.Small) + 6f)),
                SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Title", (slot.index + 1).Named("index")));
            y += 36f;

            DrawSlotPreview(new Rect(rect.x, y, rect.width, 86f), slot);
            y += 98f;

            if (slot.HasStoredThing)
            {
                Rect unloadRect = new Rect(rect.x, y, 130f, Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 12f));
                if (SimUiStyle.DrawDangerButton(unloadRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Unload")))
                    stand.TryUnloadSlot(slot.index);
            }
            else
            {
                Rect chooseRect = new Rect(rect.x, y, 150f, Mathf.Max(32f, Text.LineHeightOf(GameFont.Small) + 12f));
                if (SimUiStyle.DrawPrimaryButton(chooseRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.ChooseSource")))
                    OpenSourceMenu(slot.index);

                if (slot.HasPendingSource)
                {
                    Rect clearRect = new Rect(chooseRect.xMax + 8f, y, 110f, chooseRect.height);
                    if (SimUiStyle.DrawSecondaryButton(clearRect, SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.ClearPending")))
                    {
                        slot.ClearPendingSource();
                        stand.RefreshDisplayMesh();
                    }
                }
            }

            y += 52f;
            DrawSlotBusinessSummary(new Rect(rect.x, y, rect.width, Mathf.Max(72f, rect.yMax - y)), slot);
            ResetText();
        }

        /// <summary>
        /// 绘制槽位预览卡，负责显示已存收藏品、待搬运来源或空槽状态。
        /// </summary>
        private void DrawSlotPreview(Rect rect, CollectibleDisplaySlotData slot)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            Rect iconRect = new Rect(rect.x + 10f, rect.y + 10f, 66f, 66f);
            Widgets.DrawBoxSolid(iconRect, new Color(0f, 0f, 0f, 0.25f));
            if (slot.HasStoredThing)
                Widgets.ThingIcon(iconRect.ContractedBy(5f), slot.StoredThing);

            string title = slot.HasStoredThing
                ? slot.StoredThing.LabelCapNoCount
                : (slot.HasPendingSource ? SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Pending", slot.pendingSourceLabel.Named("source")) : SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.Empty"));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = slot.HasPendingSource && !slot.HasStoredThing ? Accent : Color.white;
            Widgets.Label(new Rect(iconRect.xMax + 12f, rect.y + 10f, rect.width - iconRect.width - 32f, Mathf.Max(28f, Text.LineHeightOf(GameFont.Small) + 6f)), title);

            Text.Font = GameFont.Tiny;
            GUI.color = MutedText;
            Widgets.Label(new Rect(iconRect.xMax + 12f, rect.y + 43f, rect.width - iconRect.width - 32f, 34f),
                SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.BusinessHint"));
        }

        /// <summary>
        /// 绘制槽位业务摘要，职责是让普通管理面板只呈现玩家需要操作的状态。
        /// </summary>
        private void DrawSlotBusinessSummary(Rect rect, CollectibleDisplaySlotData slot)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.16f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = MutedText;
            string text = slot.HasStoredThing
                ? SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.StoredSummary", slot.StoredThing.LabelCapNoCount.Named("thing"))
                : (slot.HasPendingSource
                    ? SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.PendingSummary", slot.pendingSourceLabel.Named("source"))
                    : SimTranslation.T("RSMF.CollectibleDisplayStand.Detail.EmptySummary"));
            Widgets.Label(rect.ContractedBy(10f), text);
        }

        /// <summary>
        /// 打开当前地图可用收藏品来源菜单。
        /// </summary>
        private void OpenSourceMenu(int slotIndex)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Thing source in CollectibleDisplayStandUtility.EnumerateAvailableSources(stand.Map, stand))
            {
                Thing captured = source;
                string label = CollectibleDisplayStandUtility.SourceLabel(source);
                options.Add(new FloatMenuOption(label, delegate
                {
                    if (!stand.TrySetPendingSource(slotIndex, captured, out string error))
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                }));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption(SimTranslation.T("RSMF.CollectibleDisplayStand.Menu.NoSources"), null));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// 构建槽位提示文本。
        /// </summary>
        private static string SlotTooltip(CollectibleDisplaySlotData slot)
        {
            if (slot.HasStoredThing)
                return slot.StoredThing.LabelCap;
            if (slot.HasPendingSource)
                return SimTranslation.T("RSMF.CollectibleDisplayStand.Grid.PendingTooltip", slot.pendingSourceLabel.Named("source"));
            return SimTranslation.T("RSMF.CollectibleDisplayStand.Grid.EmptyTooltip");
        }

    }
}
