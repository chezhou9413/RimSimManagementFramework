using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        private bool schedulePaintActive;
        private bool schedulePaintOpen;
        private int schedulePaintButton = -1;
        private readonly int[] scheduleRoleCoverageBuffer = new int[24];
        private Vector2 scheduleRoleScroll;

        //绘制商店营业设置面板：状态卡、总开关、快捷预设、可拖拽 24h 营业时间轴及店内各岗位排班覆盖对照。
        private void DrawBusinessHoursPanel(Rect rect)
        {
            if (draftSchedule == null)
                draftSchedule = new ShopScheduleData();

            if (Event.current.type == EventType.MouseUp
                || (schedulePaintButton == 0 && !Input.GetMouseButton(0))
                || (schedulePaintButton == 1 && !Input.GetMouseButton(1)))
            {
                schedulePaintActive = false;
                schedulePaintButton = -1;
            }

            float y = rect.y;
            DrawScheduleStatusCard(new Rect(rect.x, y, rect.width, 68f));
            y += 76f;

            DrawScheduleToggleRow(new Rect(rect.x, y, rect.width, 34f));
            y += 42f;

            DrawScheduleQuickButtons(new Rect(rect.x, y, rect.width, 28f));
            y += 36f;

            float timelineH = 82f;
            DrawScheduleTimeline(new Rect(rect.x, y, rect.width, timelineH));
            y += timelineH + 10f;

            if (rect.yMax - y >= 72f)
                DrawScheduleRoleCoveragePreview(new Rect(rect.x, y, rect.width, rect.yMax - y));
        }

        //绘制当前营业状态摘要与直达店员排班入口。
        private void DrawScheduleStatusCard(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.14f));
            DrawBorderRect(rect, CDivider, 1f);

            string staffBtnLabel = SimTranslation.T("RSMF.ShopManager.OpenStaffShifts");
            Text.Font = GameFont.Tiny;
            float btnW = Mathf.Max(116f, Text.CalcSize(staffBtnLabel).x + 20f);
            Rect btnRect = new Rect(rect.xMax - btnW - 12f, rect.y + (rect.height - 30f) * 0.5f, btnW, 30f);
            if (SimUiStyle.DrawPrimaryButton(btnRect, staffBtnLabel, true, GameFont.Tiny))
                Find.WindowStack.Add(new Dialog_ShopStaffManager(shopZone));

            float textW = Mathf.Max(80f, btnRect.x - rect.x - 24f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = draftSchedule.IsOpenNow(shopZone.Map) && shopZone.IsValidShop() ? CStockOk : CStockLow;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 8f, textW, 24f),
                SimTranslation.T("RSMF.ShopManager.CurrentStatus", GetDraftOpenStatusText().Named("status")).Truncate(textW));

            Text.Font = GameFont.Tiny;
            GUI.color = CTextMid;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 36f, textW, 22f),
                SimTranslation.T("RSMF.ShopManager.Schedule", draftSchedule.GetScheduleSummary().Named("schedule")).Truncate(textW));
            ResetText();
        }

        //绘制营业总开关和是否启用分时日程的二元设置。
        private void DrawScheduleToggleRow(Rect rect)
        {
            Rect openRect = new Rect(rect.x, rect.y, Mathf.Min(230f, rect.width * 0.46f), rect.height);
            Rect scheduleRect = new Rect(openRect.xMax + 14f, rect.y, Mathf.Min(260f, rect.xMax - openRect.xMax - 14f), rect.height);

            bool manualOpen = draftSchedule.manualOpen;
            DrawBooleanPill(openRect, SimTranslation.T("RSMF.ShopManager.ManualOpen"), ref manualOpen, SimTranslation.T("RSMF.ShopManager.ManualOpenTip"));
            draftSchedule.manualOpen = manualOpen;

            bool useSchedule = draftSchedule.useSchedule;
            DrawBooleanPill(scheduleRect, SimTranslation.T("RSMF.ShopManager.ScheduledOpen"), ref useSchedule, SimTranslation.T("RSMF.ShopManager.ScheduledOpenTip"));
            draftSchedule.useSchedule = useSchedule;
        }

        //绘制常用营业时间快捷预设按钮（全天、白班、默认、夜市、全关）。
        private void DrawScheduleQuickButtons(Rect rect)
        {
            float x = rect.x;
            Text.Font = GameFont.Tiny;

            string alwaysOpen = SimTranslation.T("RSMF.ShopManager.AlwaysOpen");
            string dayShift = SimTranslation.T("RSMF.ShopManager.ScheduleDayShift");
            string defaultSch = SimTranslation.T("RSMF.ShopManager.DefaultSchedule");
            string nightMkt = SimTranslation.T("RSMF.ShopManager.ScheduleNightMarket");
            string alwaysClosed = SimTranslation.T("RSMF.ShopManager.AlwaysClosed");

            x = DrawQuickScheduleBtn(x, rect.y, rect.height, alwaysOpen, () =>
            {
                draftSchedule.SetAllHours(true);
                draftSchedule.useSchedule = false;
            });
            x = DrawQuickScheduleBtn(x, rect.y, rect.height, dayShift, () => SetDraftScheduleRange(8, 20));
            x = DrawQuickScheduleBtn(x, rect.y, rect.height, defaultSch, () =>
            {
                draftSchedule.SetDefaultBusinessHours();
                draftSchedule.useSchedule = true;
            });
            x = DrawQuickScheduleBtn(x, rect.y, rect.height, nightMkt, () => SetDraftScheduleRange(16, 4));
            DrawQuickScheduleBtn(x, rect.y, rect.height, alwaysClosed, () =>
            {
                draftSchedule.SetAllHours(false);
                draftSchedule.useSchedule = true;
            });
            ResetText();
        }

        //绘制单个营业时间快捷按钮并返回下一个按钮起点。
        private static float DrawQuickScheduleBtn(float x, float y, float h, string label, System.Action onClick)
        {
            float w = Mathf.Max(74f, Text.CalcSize(label).x + 18f);
            if (SimUiStyle.DrawSecondaryButton(new Rect(x, y, w, h), label, true, GameFont.Tiny))
                onClick?.Invoke();
            return x + w + 6f;
        }

        //按起止小时（支持跨午夜）设置草稿营业时间段。
        private void SetDraftScheduleRange(int startHour, int endHourExclusive)
        {
            draftSchedule.SetAllHours(false);
            draftSchedule.useSchedule = true;
            int cursor = ((startHour % 24) + 24) % 24;
            int end = ((endHourExclusive % 24) + 24) % 24;
            while (cursor != end)
            {
                draftSchedule.SetHourOpen(cursor, true);
                cursor = (cursor + 1) % 24;
            }
        }

        //绘制支持左键拖动开启、右键拖动关闭的 24 小时连续营业时间轴。
        private void DrawScheduleTimeline(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.14f));
            DrawBorderRect(rect, CDivider, 1f);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextMid;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, 20f), SimTranslation.T("RSMF.ShopManager.ScheduleDragHint"));

            int currentHour = shopZone?.Map != null ? GenLocalDate.HourInteger(shopZone.Map) : 0;
            float leftPad = 10f;
            float gridW = Mathf.Max(240f, rect.width - leftPad * 2f);
            float cellW = gridW / 24f;
            float barY = rect.y + 28f;
            float barH = Mathf.Max(38f, rect.yMax - barY - 10f);

            Text.Anchor = TextAnchor.MiddleCenter;
            for (int hour = 0; hour < 24; hour++)
            {
                Rect cell = new Rect(rect.x + leftPad + hour * cellW, barY, Mathf.Max(1f, cellW - 1f), barH);
                bool open = ShopStaffShiftUtility.IsShopScheduledOpenAtHour(draftSchedule, hour);
                Color fill = open ? new Color(CAccent.r, CAccent.g, CAccent.b, 0.36f) : new Color(1f, 1f, 1f, 0.04f);
                Widgets.DrawBoxSolid(cell, fill);
                DrawBorderRect(cell, hour == currentHour ? Color.white : (open ? CAccent : CDivider), 1f);
                Widgets.DrawHighlightIfMouseover(cell);

                GUI.color = open ? Color.white : CTextDim;
                Widgets.Label(new Rect(cell.x, cell.y + 2f, cell.width, 18f), hour.ToString("00"));
                GUI.color = open ? CStockOk : CTextDim;
                Widgets.Label(new Rect(cell.x, cell.y + 20f, cell.width, cell.height - 22f), open ? "●" : "·");

                if (Mouse.IsOver(cell))
                {
                    if (Event.current.type == EventType.MouseDown && (Event.current.button == 0 || Event.current.button == 1))
                    {
                        schedulePaintActive = true;
                        schedulePaintButton = Event.current.button;
                        schedulePaintOpen = Event.current.button == 0 ? !open : false;
                        draftSchedule.useSchedule = true;
                        draftSchedule.SetHourOpen(hour, schedulePaintOpen);
                        Event.current.Use();
                    }
                    else if (schedulePaintActive && schedulePaintButton >= 0
                        && ((schedulePaintButton == 0 && Input.GetMouseButton(0)) || (schedulePaintButton == 1 && Input.GetMouseButton(1))))
                    {
                        draftSchedule.useSchedule = true;
                        draftSchedule.SetHourOpen(hour, schedulePaintOpen);
                    }
                }
            }
            ResetText();
        }

        //绘制店内各岗位 24 小时排班覆盖对照表，方便调整营业时间时立即发现无人值守空窗。
        private void DrawScheduleRoleCoveragePreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.14f));
            DrawBorderRect(rect, CDivider, 1f);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextMid;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 20f),
                SimTranslation.T("RSMF.ShopManager.ScheduleRoleCoverageTitle"));

            List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(shopZone);
            if (roles.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = CTextDim;
                Widgets.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, Mathf.Max(24f, rect.height - 36f)),
                    SimTranslation.T("RSMF.ShopManager.NoRolesForCoverage"));
                ResetText();
                return;
            }

            int currentHour = shopZone?.Map != null ? GenLocalDate.HourInteger(shopZone.Map) : 0;
            Rect outRect = new Rect(rect.x + 8f, rect.y + 30f, rect.width - 16f, Mathf.Max(0f, rect.height - 38f));
            float rowH = 34f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, roles.Count * (rowH + 4f)));
            float labelColW = Mathf.Clamp(viewRect.width * 0.30f, 150f, 230f);

            Widgets.BeginScrollView(outRect, ref scheduleRoleScroll, viewRect);
            for (int i = 0; i < roles.Count; i++)
            {
                ShopStaffRoleDef role = roles[i];
                Rect row = new Rect(0f, i * (rowH + 4f), viewRect.width, rowH);
                Widgets.DrawBoxSolid(row, i % 2 == 0 ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.06f));

                int assignedCount = shopZone.GetAssignedPawns(role.defName).Count;
                ShopStaffShiftUtility.CountHourlyCoverage(shopZone, role.defName, scheduleRoleCoverageBuffer);
                int gapHours = 0;
                for (int h = 0; h < 24; h++)
                {
                    if (assignedCount > 0 && scheduleRoleCoverageBuffer[h] == 0
                        && ShopStaffShiftUtility.IsShopScheduledOpenAtHour(draftSchedule, h))
                        gapHours++;
                }

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                string tag = assignedCount == 0
                    ? SimTranslation.T("RSMF.StaffShift.RoleUnassignedShort")
                    : (gapHours > 0
                        ? SimTranslation.T("RSMF.StaffShift.RoleGapShort", gapHours.Named("gap"))
                        : SimTranslation.T("RSMF.StaffShift.RoleCoveredShort"));
                Widgets.Label(new Rect(row.x + 6f, row.y, labelColW - 10f, 18f), $"{role.LabelCap} ({assignedCount})".Truncate(labelColW - 10f));
                GUI.color = assignedCount == 0 ? CTextDim : (gapHours > 0 ? CStockLow : CStockOk);
                Widgets.Label(new Rect(row.x + 6f, row.y + 16f, labelColW - 10f, 16f), tag.Truncate(labelColW - 10f));

                Rect stripRect = new Rect(row.x + labelColW, row.y + 8f, Mathf.Max(120f, row.width - labelColW - 6f), 18f);
                Dialog_ShopStaffManager.DrawMiniCoverageStrip(stripRect, scheduleRoleCoverageBuffer, draftSchedule, currentHour, assignedCount > 0);
            }
            Widgets.EndScrollView();
            ResetText();
        }

        //绘制带说明的布尔开关按钮。
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

        //根据草稿营业设置和商店设施状态生成当前状态文本。
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
