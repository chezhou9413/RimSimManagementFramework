using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        /// <summary>
        /// 绘制商店营业设置面板，负责编辑手动开关和二十四小时营业日程。
        /// </summary>
        private void DrawBusinessHoursPanel(Rect rect)
        {
            if (draftSchedule == null)
                draftSchedule = new ShopScheduleData();

            float y = rect.y;
            DrawScheduleStatusCard(new Rect(rect.x, y, rect.width, 78f));
            y += 88f;

            DrawScheduleToggleRow(new Rect(rect.x, y, rect.width, 38f));
            y += 48f;

            DrawScheduleQuickButtons(new Rect(rect.x, y, rect.width, 32f));
            y += 42f;

            DrawScheduleHourGrid(new Rect(rect.x, y, rect.width, rect.yMax - y));
        }

        /// <summary>
        /// 绘制当前营业状态摘要，帮助玩家确认设置会如何影响刷客和店员工作。
        /// </summary>
        private void DrawScheduleStatusCard(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.14f));
            DrawBorderRect(rect, CDivider, 1f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = draftSchedule.IsOpenNow(shopZone.Map) && shopZone.IsValidShop() ? CStockOk : CStockLow;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 26f), SimTranslation.T("RSMF.ShopManager.CurrentStatus", GetDraftOpenStatusText().Named("status")));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextMid;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 40f, rect.width - 24f, 24f), SimTranslation.T("RSMF.ShopManager.Schedule", draftSchedule.GetScheduleSummary().Named("schedule")));
            ResetText();
        }

        /// <summary>
        /// 绘制营业总开关和是否启用日程的二元设置。
        /// </summary>
        private void DrawScheduleToggleRow(Rect rect)
        {
            Rect openRect = new Rect(rect.x, rect.y, Mathf.Min(220f, rect.width * 0.45f), rect.height);
            Rect scheduleRect = new Rect(openRect.xMax + 18f, rect.y, Mathf.Min(260f, rect.xMax - openRect.xMax - 18f), rect.height);

            bool manualOpen = draftSchedule.manualOpen;
            DrawBooleanPill(openRect, SimTranslation.T("RSMF.ShopManager.ManualOpen"), ref manualOpen, SimTranslation.T("RSMF.ShopManager.ManualOpenTip"));
            draftSchedule.manualOpen = manualOpen;

            bool useSchedule = draftSchedule.useSchedule;
            DrawBooleanPill(scheduleRect, SimTranslation.T("RSMF.ShopManager.ScheduledOpen"), ref useSchedule, SimTranslation.T("RSMF.ShopManager.ScheduledOpenTip"));
            draftSchedule.useSchedule = useSchedule;
        }

        /// <summary>
        /// 绘制营业时间快捷按钮，减少玩家逐小时调整的操作量。
        /// </summary>
        private void DrawScheduleQuickButtons(Rect rect)
        {
            float x = rect.x;
            if (SimUiStyle.DrawSecondaryButton(new Rect(x, rect.y, 86f, rect.height), SimTranslation.T("RSMF.ShopManager.AlwaysOpen"), true, GameFont.Tiny))
            {
                draftSchedule.SetAllHours(true);
                draftSchedule.useSchedule = false;
            }
            x += 94f;

            if (SimUiStyle.DrawSecondaryButton(new Rect(x, rect.y, 86f, rect.height), SimTranslation.T("RSMF.ShopManager.AlwaysClosed"), true, GameFont.Tiny))
            {
                draftSchedule.SetAllHours(false);
                draftSchedule.useSchedule = true;
            }
            x += 94f;

            if (SimUiStyle.DrawSecondaryButton(new Rect(x, rect.y, 118f, rect.height), SimTranslation.T("RSMF.ShopManager.DefaultSchedule"), true, GameFont.Tiny))
            {
                draftSchedule.SetDefaultBusinessHours();
                draftSchedule.useSchedule = true;
            }
        }

        /// <summary>
        /// 绘制二十四小时营业格，点击单个小时可切换该小时是否营业。
        /// </summary>
        private void DrawScheduleHourGrid(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextMid;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), SimTranslation.T("RSMF.ShopManager.BusinessHours"));
            ResetText();

            float top = rect.y + 30f;
            int columns = 6;
            float gap = 6f;
            float cellW = (rect.width - gap * (columns - 1)) / columns;
            float cellH = 34f;

            for (int hour = 0; hour < 24; hour++)
            {
                int col = hour % columns;
                int row = hour / columns;
                Rect cell = new Rect(rect.x + col * (cellW + gap), top + row * (cellH + gap), cellW, cellH);
                DrawHourCell(cell, hour);
            }
        }

        /// <summary>
        /// 绘制单个小时格，并在点击时切换营业状态。
        /// </summary>
        private void DrawHourCell(Rect rect, int hour)
        {
            bool open = draftSchedule.openHours != null && hour >= 0 && hour < draftSchedule.openHours.Count && draftSchedule.openHours[hour];
            Color fill = open ? new Color(CAccent.r, CAccent.g, CAccent.b, 0.22f) : new Color(1f, 1f, 1f, 0.035f);
            Color text = open ? Color.white : CTextDim;

            Widgets.DrawBoxSolid(rect, fill);
            DrawBorderRect(rect, open ? CAccent : CDivider, 1f);
            Widgets.DrawHighlightIfMouseover(rect);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = text;
            Widgets.Label(rect, $"{hour:00}:00");
            ResetText();

            if (Widgets.ButtonInvisible(rect))
            {
                draftSchedule.useSchedule = true;
                draftSchedule.SetHourOpen(hour, !open);
            }
        }

        /// <summary>
        /// 绘制带说明的布尔开关按钮。
        /// </summary>
        private void DrawBooleanPill(Rect rect, string label, ref bool value, string tooltip)
        {
            Color fill = value ? new Color(CAccent.r, CAccent.g, CAccent.b, 0.24f) : new Color(1f, 1f, 1f, 0.04f);
            Widgets.DrawBoxSolid(rect, fill);
            DrawBorderRect(rect, value ? CAccent : CDivider, 1f);
            Widgets.DrawHighlightIfMouseover(rect);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = value ? Color.white : CTextMid;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, rect.width - 46f, rect.height), label);

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = value ? CStockOk : CTextDim;
            Widgets.Label(new Rect(rect.xMax - 38f, rect.y, 32f, rect.height), value ? SimTranslation.T("RSMF.ShopManager.OpenShort") : SimTranslation.T("RSMF.ShopManager.ClosedShort"));
            ResetText();

            TooltipHandler.TipRegion(rect, tooltip);
            if (Widgets.ButtonInvisible(rect))
                value = !value;
        }

        /// <summary>
        /// 根据草稿营业设置和商店设施状态生成当前状态文本。
        /// </summary>
        private string GetDraftOpenStatusText()
        {
            if (!shopZone.IsValidShop())
                return shopZone.GetValidationMessage();
            if (!draftSchedule.manualOpen)
                return SimTranslation.T("RSMF.ShopManager.ManuallyClosed");
            if (draftSchedule.useSchedule && !draftSchedule.IsOpenNow(shopZone.Map))
                return SimTranslation.T("RSMF.ShopManager.OutsideSchedule");
            return SimTranslation.T("RSMF.ShopManager.OpenNow");
        }
    }
}
