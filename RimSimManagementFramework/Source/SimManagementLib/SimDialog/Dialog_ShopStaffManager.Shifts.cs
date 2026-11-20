using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //负责对齐式 24 小时排班矩阵视图、原版作息冲突叠加、左键涂刷/右键擦除及快捷班次操作。
    public partial class Dialog_ShopStaffManager
    {
        private const float MatrixRowHeight = 62f;
        private const float MatrixHeaderHeight = 46f;
        private const float ToolbarHeight = 30f;
        private const float LegendHeight = 22f;

        private readonly int[] hourlyCoverageBuffer = new int[24];
        private ShopRoleAssignment shiftPaintAssignment;
        private bool shiftPaintTargetValue;
        private int shiftPaintButton = -1;

        //每帧开始时在鼠标松开后结束排班拖拽涂刷状态。
        private void UpdateShiftPaintState()
        {
            if (Event.current.type == EventType.MouseUp
                || (shiftPaintButton == 0 && !Input.GetMouseButton(0))
                || (shiftPaintButton == 1 && !Input.GetMouseButton(1)))
            {
                shiftPaintAssignment = null;
                shiftPaintButton = -1;
            }
        }

        //绘制排班矩阵标签页：顶部快捷工具栏、固定 00~23 标尺与覆盖汇总行、员工排班矩阵和底部图例。
        private void DrawShiftMatrixTab(Rect rect, ShopStaffRoleDef role, List<ShopRoleAssignment> assignmentRecords)
        {
            if (assignmentRecords.Count == 0)
            {
                Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.14f));
                SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Dim;
                Widgets.Label(rect.ContractedBy(24f), SimTranslation.T("RSMF.StaffShift.EmptyRoster"));
                ResetText();
                return;
            }

            ShopScheduleData shopSchedule = zone.GetSchedule();
            int currentHour = zone.Map != null ? GenLocalDate.HourInteger(zone.Map) : 0;
            int openGapHours = ShopStaffShiftUtility.CountRoleOpenGapHours(zone, role.defName, hourlyCoverageBuffer, out int coveredHours, out _);

            float y = rect.y;
            DrawShiftToolbar(new Rect(rect.x, y, rect.width, ToolbarHeight), role, coveredHours, openGapHours);
            y += ToolbarHeight + 6f;

            float leftColW = Mathf.Clamp(rect.width * 0.32f, 224f, 286f);
            float gridW = Mathf.Max(240f, rect.width - leftColW - 16f);

            //固定表头：00~23 时间轴标尺 + 岗位每小时覆盖人数汇总行。
            DrawMatrixHeader(new Rect(rect.x, y, rect.width, MatrixHeaderHeight), role, leftColW, gridW, shopSchedule, currentHour);
            y += MatrixHeaderHeight + 4f;

            //滚动区：逐行展示已分配员工与严格垂直对齐的 24 小时排班格。
            float scrollH = Mathf.Max(0f, rect.yMax - y - LegendHeight - 6f);
            Rect outRect = new Rect(rect.x, y, rect.width, scrollH);
            float totalH = assignmentRecords.Count * (MatrixRowHeight + 4f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(outRect.height, totalH));
            Widgets.BeginScrollView(outRect, ref rosterScroll, viewRect);

            for (int i = 0; i < assignmentRecords.Count; i++)
            {
                Rect row = new Rect(0f, i * (MatrixRowHeight + 4f), viewRect.width, MatrixRowHeight);
                DrawMatrixStaffRow(row, assignmentRecords[i], role, i, leftColW, gridW, currentHour);
            }

            Widgets.EndScrollView();

            //底部一行图例与操作提示。
            DrawMatrixLegend(new Rect(rect.x, rect.yMax - LegendHeight, rect.width, LegendHeight));
        }

        //绘制顶部覆盖统计与一键倒班、按营业排班、全天、同步作息表按钮栏。
        private void DrawShiftToolbar(Rect rect, ShopStaffRoleDef role, int coveredHours, int openGapHours)
        {
            string syncLabel = SimTranslation.T("RSMF.StaffShift.SyncAllTimetables");
            string clearLabel = SimTranslation.T("RSMF.StaffShift.ClearAll");
            string openLabel = SimTranslation.T("RSMF.StaffShift.AutoOpenHours");
            string autoLabel = SimTranslation.T("RSMF.StaffShift.AutoDistribute");

            Text.Font = GameFont.Tiny;
            float syncW = Mathf.Max(78f, Text.CalcSize(syncLabel).x + 16f);
            float clearW = Mathf.Max(64f, Text.CalcSize(clearLabel).x + 14f);
            float openW = Mathf.Max(76f, Text.CalcSize(openLabel).x + 16f);
            float autoW = Mathf.Max(68f, Text.CalcSize(autoLabel).x + 16f);
            float btnH = 26f;
            float btnY = rect.y + (rect.height - btnH) * 0.5f;

            float buttonsW = syncW + clearW + openW + autoW + 18f;
            float titleW = Mathf.Max(60f, rect.width - buttonsW - 8f);
            string titleText = SimTranslation.T("RSMF.StaffShift.CoverageTitle", coveredHours.Named("covered"))
                + (openGapHours > 0
                    ? SimTranslation.T("RSMF.StaffShift.CoverageGap", openGapHours.Named("gap"))
                    : SimTranslation.T("RSMF.StaffShift.CoverageFull"));
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x, rect.y, titleW, rect.height), titleText, openGapHours > 0 ? Warn : Ok, GameFont.Tiny);

            float bx = rect.xMax - syncW;
            Rect syncRect = new Rect(bx, btnY, syncW, btnH);
            TooltipHandler.TipRegion(syncRect, SimTranslation.T("RSMF.StaffShift.SyncAllTimetablesTip"));
            if (SimUiStyle.DrawSecondaryButton(syncRect, syncLabel, true, GameFont.Tiny))
            {
                int synced = ShopStaffShiftUtility.SyncRoleTimetables(zone, role.defName);
                Messages.Message(synced > 0
                    ? SimTranslation.T("RSMF.StaffShift.SyncDone", synced.Named("count"))
                    : SimTranslation.T("RSMF.StaffShift.SyncNone"), MessageTypeDefOf.NeutralEvent, false);
            }

            bx -= clearW + 6f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(bx, btnY, clearW, btnH), clearLabel, true, GameFont.Tiny))
                ShopStaffShiftUtility.ClearRoleShifts(zone, role.defName);

            bx -= openW + 6f;
            Rect openRect = new Rect(bx, btnY, openW, btnH);
            TooltipHandler.TipRegion(openRect, SimTranslation.T("RSMF.StaffShift.AutoOpenHoursTip"));
            if (SimUiStyle.DrawSecondaryButton(openRect, openLabel, true, GameFont.Tiny))
            {
                int distributed = ShopStaffShiftUtility.AutoDistributeOpenHoursShifts(zone, role.defName);
                if (distributed > 0)
                    Messages.Message(SimTranslation.T("RSMF.StaffShift.AutoDistributeDone", distributed.Named("count")), MessageTypeDefOf.NeutralEvent, false);
            }

            bx -= autoW + 6f;
            Rect autoRect = new Rect(bx, btnY, autoW, btnH);
            TooltipHandler.TipRegion(autoRect, SimTranslation.T("RSMF.StaffShift.AutoDistributeTip"));
            if (SimUiStyle.DrawPrimaryButton(autoRect, autoLabel, true, GameFont.Tiny))
            {
                int distributed = ShopStaffShiftUtility.AutoDistributeShifts(zone, role.defName);
                if (distributed > 0)
                    Messages.Message(SimTranslation.T("RSMF.StaffShift.AutoDistributeDone", distributed.Named("count")), MessageTypeDefOf.NeutralEvent, false);
            }
            ResetText();
        }

        //绘制与下方员工行像素对齐的 00~23 小时标尺和每小时覆盖人数行。
        private void DrawMatrixHeader(Rect rect, ShopStaffRoleDef role, float leftColW, float gridW, ShopScheduleData schedule, int currentHour)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.26f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Dim;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 2f, leftColW - 14f, 20f), role.LabelCap);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 23f, leftColW - 14f, 20f), SimTranslation.T("RSMF.ShopManager.Schedule", schedule.GetScheduleSummary().Named("schedule")).Truncate(leftColW - 14f));

            float gridX = rect.x + leftColW;
            float cellW = gridW / 24f;
            Text.Anchor = TextAnchor.MiddleCenter;
            for (int h = 0; h < 24; h++)
            {
                float cx = gridX + h * cellW;
                float cw = Mathf.Max(1f, cellW - 1f);
                bool openAtHour = ShopStaffShiftUtility.IsShopScheduledOpenAtHour(schedule, h);
                int count = hourlyCoverageBuffer[h];

                //上排：小时数字标尺。
                Rect hourRect = new Rect(cx, rect.y + 2f, cw, 20f);
                if (h == currentHour)
                    Widgets.DrawBoxSolid(hourRect, new Color(1f, 1f, 1f, 0.18f));
                GUI.color = h == currentHour ? Color.white : (openAtHour ? new Color(0.86f, 0.88f, 0.92f) : Dim);
                Widgets.Label(hourRect, h.ToString("00"));

                //下排：该小时在班人数汇总。
                Rect covRect = new Rect(cx, rect.y + 24f, cw, 19f);
                Color covFill = count > 0
                    ? new Color(0.24f, 0.68f, 0.38f, count > 1 ? 0.56f : 0.36f)
                    : (openAtHour ? new Color(0.88f, 0.34f, 0.24f, 0.42f) : new Color(1f, 1f, 1f, 0.04f));
                Widgets.DrawBoxSolid(covRect, covFill);
                if (h == currentHour)
                    SimUiStyle.DrawBorder(covRect, Color.white);
                GUI.color = count > 0 ? Color.white : (openAtHour ? new Color(1f, 0.85f, 0.78f) : Dim);
                Widgets.Label(covRect, count > 0 ? count.ToString() : (openAtHour ? "0" : "·"));

                if (Mouse.IsOver(covRect) || Mouse.IsOver(hourRect))
                {
                    string staffText = ShopStaffShiftUtility.FormatHourlyStaffNames(zone, role.defName, h);
                    if (staffText.NullOrEmpty()) staffText = SimTranslation.T("RSMF.StaffShift.NoStaffThisHour");
                    string tip = SimTranslation.T("RSMF.StaffShift.CoverageCellTip",
                        h.ToString("00").Named("hour"),
                        SimTranslation.T(openAtHour ? "RSMF.StaffShift.HourOpen" : "RSMF.StaffShift.HourClosed").Named("openState"),
                        count.Named("count"),
                        staffText.Named("staff"));
                    TooltipHandler.TipRegion(new Rect(cx, rect.y, cw, rect.height), tip);
                }
            }
            ResetText();
        }

        //绘制单名员工在排班矩阵中的行：左列头像/状态/班次预设，右列 24 格排班与原版作息叠加。
        private void DrawMatrixStaffRow(Rect row, ShopRoleAssignment assignment, ShopStaffRoleDef role, int index,
            float leftColW, float gridW, int currentHour)
        {
            bool usable = assignment.HasUsablePawnOn(zone.Map);
            bool onShiftNow = usable && assignment.IsOnShiftNow(zone.Map);
            int conflicts = usable ? ShopStaffShiftUtility.CountTimetableConflicts(assignment) : 0;

            Color rowBg = !usable
                ? new Color(0.92f, 0.50f, 0.20f, 0.10f)
                : (index % 2 == 0 ? PanelAlt : new Color(1f, 1f, 1f, 0.015f));
            Widgets.DrawBoxSolid(row, rowBg);
            SimUiStyle.DrawBorder(row, conflicts > 0 ? new Color(Warn.r, Warn.g, Warn.b, 0.40f) : new Color(1f, 1f, 1f, 0.08f));

            Pawn pawn = assignment.pawn;
            Rect portraitRect = new Rect(row.x + 6f, row.y + 8f, 46f, 46f);
            if (pawn != null && !pawn.Destroyed)
            {
                RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(84f, 84f), Rot4.South);
                GUI.color = Color.white;
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit, true);
                if (pawn.Spawned && Widgets.ButtonInvisible(portraitRect))
                {
                    Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                    CameraJumper.TryJump(pawn.Position, pawn.Map);
                }
            }
            else
            {
                Widgets.DrawBoxSolid(portraitRect, new Color(1f, 1f, 1f, 0.06f));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Dim;
                Widgets.Label(portraitRect, "?");
            }

            //左侧信息与快捷按钮区。
            float infoX = portraitRect.xMax + 8f;
            float infoW = Mathf.Max(80f, leftColW - (infoX - row.x) - 8f);

            //上排：状态点 + 姓名 + 冲突警示徽章。
            Rect dotRect = new Rect(infoX, row.y + 11f, 8f, 8f);
            Widgets.DrawBoxSolid(dotRect, !usable ? Danger : (onShiftNow ? Ok : Dim));

            float nameRightX = infoX + infoW;
            if (conflicts > 0)
            {
                string cLabel = SimTranslation.T("RSMF.StaffShift.ConflictBadge", conflicts.Named("count"));
                Text.Font = GameFont.Tiny;
                float cW = Mathf.Min(infoW * 0.5f, Text.CalcSize(cLabel).x + 10f);
                Rect cRect = new Rect(infoX + infoW - cW, row.y + 6f, cW, 18f);
                Widgets.DrawBoxSolid(cRect, new Color(0.88f, 0.45f, 0.15f, 0.35f));
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Warn;
                Widgets.Label(cRect, cLabel);
                TooltipHandler.TipRegion(cRect, SimTranslation.T("RSMF.StaffShift.ConflictTip", conflicts.Named("count")));
                nameRightX = cRect.x - 4f;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(dotRect.xMax + 5f, row.y + 4f, Mathf.Max(30f, nameRightX - dotRect.xMax - 5f), 22f),
                assignment.DisplayLabel().Truncate(Mathf.Max(30f, nameRightX - dotRect.xMax - 5f)));

            //下排：班次摘要 + 「预设」「同步」「×」紧凑操作按钮。
            float btnY = row.y + 32f;
            float btnH = 22f;
            float removeW = 24f;
            bool isColonist = pawn != null && !pawn.Destroyed && pawn.IsColonist && !pawn.IsPrisonerOfColony && pawn.timetable != null;
            string syncLbl = SimTranslation.T("RSMF.StaffShift.SyncSingle");
            string presetLbl = SimTranslation.T("RSMF.StaffShift.PresetMenu");

            Text.Font = GameFont.Tiny;
            float syncW = isColonist ? Mathf.Max(42f, Text.CalcSize(syncLbl).x + 10f) : 0f;
            float presetW = Mathf.Max(40f, Text.CalcSize(presetLbl).x + 10f);

            float rx = infoX + infoW - removeW;
            Rect removeRect = new Rect(rx, btnY, removeW, btnH);
            TooltipHandler.TipRegion(removeRect, SimTranslation.T("RSMF.StaffManager.RemoveRole"));
            if (SimUiStyle.DrawDangerButton(removeRect, "×", true, GameFont.Tiny))
                zone.RemoveAssignedPawnRecord(role.defName, assignment);

            if (isColonist)
            {
                rx -= syncW + 3f;
                Rect syncRect = new Rect(rx, btnY, syncW, btnH);
                if (SimUiStyle.DrawSecondaryButton(syncRect, syncLbl, assignment.useShiftSchedule, GameFont.Tiny))
                {
                    if (ShopStaffShiftUtility.SyncColonistTimetableToShifts(pawn, zone.Map))
                        Messages.Message(SimTranslation.T("RSMF.StaffShift.SyncSingleDone", assignment.DisplayLabel().Named("pawn")), MessageTypeDefOf.NeutralEvent, false);
                }
            }

            rx -= presetW + 3f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(rx, btnY, presetW, btnH), presetLbl, true, GameFont.Tiny))
                OpenSingleShiftPresetMenu(assignment);

            float summaryW = Mathf.Max(30f, rx - infoX - 4f);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Dim;
            string summaryText = usable ? assignment.GetShiftSummary() : SimTranslation.T("RSMF.StaffManager.AssignedUnavailable");
            Rect summaryRect = new Rect(infoX, btnY, summaryW, btnH);
            Widgets.Label(summaryRect, summaryText.Truncate(summaryW));
            TooltipHandler.TipRegion(summaryRect, usable
                ? $"{summaryText}\n{SimTranslation.T("RSMF.StaffManager.CurrentJob", (pawn.CurJobDef?.LabelCap.RawText ?? SimTranslation.T("RSMF.StaffManager.Idle")).Named("job"))}"
                : BuildAssignmentDetail(assignment));

            //右侧：24 小时可涂刷排班格（底部叠加原版作息条，冲突小时带顶部橙红警示）。
            DrawMatrixHourCells(new Rect(row.x + leftColW, row.y + 8f, gridW, row.height - 16f), assignment, pawn, currentHour);
            ResetText();
        }

        //绘制单个员工 24 格排班条，支持左键涂刷/翻转与右键快速擦除，并叠加原版作息色条与冲突警示。
        private void DrawMatrixHourCells(Rect gridRect, ShopRoleAssignment assignment, Pawn pawn, int currentHour)
        {
            float cellW = gridRect.width / 24f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            for (int h = 0; h < 24; h++)
            {
                Rect cell = new Rect(gridRect.x + h * cellW, gridRect.y, Mathf.Max(1f, cellW - 1f), gridRect.height);
                bool on = assignment.IsOnShiftAtHour(h);
                TimeAssignmentDef timetable = ShopStaffShiftUtility.GetColonistTimetableAtHour(pawn, h);
                bool conflict = on && ShopStaffShiftUtility.IsTimetableOffDuty(timetable);

                Color fill = on
                    ? (assignment.useShiftSchedule ? new Color(Accent.r, Accent.g, Accent.b, 0.52f) : new Color(0.28f, 0.64f, 0.44f, 0.38f))
                    : new Color(1f, 1f, 1f, 0.04f);
                Widgets.DrawBoxSolid(cell, fill);

                //底部 4px 叠加原版作息色条，直观看清睡眠/娱乐/冥想时段。
                if (timetable != null)
                    Widgets.DrawBoxSolid(new Rect(cell.x + 1f, cell.yMax - 5f, Mathf.Max(1f, cell.width - 2f), 4f), TimetableBarColor(timetable));

                //顶部 3px 冲突警示条 + 格内感叹号。
                if (conflict)
                {
                    Widgets.DrawBoxSolid(new Rect(cell.x + 1f, cell.y + 1f, Mathf.Max(1f, cell.width - 2f), 3f), Danger);
                    GUI.color = Warn;
                    Widgets.Label(new Rect(cell.x, cell.y + 2f, cell.width, cell.height - 8f), "!");
                }

                SimUiStyle.DrawBorder(cell, h == currentHour ? Color.white : new Color(1f, 1f, 1f, 0.10f));
                Widgets.DrawHighlightIfMouseover(cell);

                if (Mouse.IsOver(cell))
                {
                    if (Event.current.type == EventType.MouseDown && (Event.current.button == 0 || Event.current.button == 1))
                    {
                        shiftPaintAssignment = assignment;
                        shiftPaintButton = Event.current.button;
                        shiftPaintTargetValue = Event.current.button == 0 ? !on : false;
                        assignment.SetShiftHour(h, shiftPaintTargetValue);
                        Event.current.Use();
                    }
                    else if (shiftPaintAssignment == assignment && shiftPaintButton >= 0
                        && ((shiftPaintButton == 0 && Input.GetMouseButton(0)) || (shiftPaintButton == 1 && Input.GetMouseButton(1))))
                    {
                        if (assignment.IsOnShiftAtHour(h) != shiftPaintTargetValue)
                            assignment.SetShiftHour(h, shiftPaintTargetValue);
                    }

                    string tip = BuildHourCellTooltip(h, on, timetable, conflict);
                    TooltipHandler.TipRegion(cell, tip);
                }
            }
            ResetText();
        }

        //弹出单名员工快捷班次预设菜单。
        private static void OpenSingleShiftPresetMenu(ShopRoleAssignment assignment)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(SimTranslation.T("RSMF.StaffShift.AllDay"), () => assignment.ClearShiftSchedule()),
                new FloatMenuOption(SimTranslation.T("RSMF.StaffShift.PresetMorning"), () => assignment.SetShiftRange(6, 14)),
                new FloatMenuOption(SimTranslation.T("RSMF.StaffShift.PresetEvening"), () => assignment.SetShiftRange(14, 22)),
                new FloatMenuOption(SimTranslation.T("RSMF.StaffShift.PresetNight"), () => assignment.SetShiftRange(22, 6)),
                new FloatMenuOption(SimTranslation.T("RSMF.StaffShift.PresetDay12"), () => assignment.SetShiftRange(8, 20)),
                new FloatMenuOption(SimTranslation.T("RSMF.StaffShift.PresetNight12"), () => assignment.SetShiftRange(20, 8)),
                new FloatMenuOption(SimTranslation.T("RSMF.StaffShift.ClearSingle"), () =>
                {
                    assignment.EnsureShiftHours();
                    assignment.useShiftSchedule = true;
                    for (int i = 0; i < 24; i++) assignment.shiftHours[i] = false;
                })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //绘制底部简洁图例与鼠标涂刷操作提示。
        private static void DrawMatrixLegend(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float x = rect.x;
            x = DrawLegendChip(x, rect.y, rect.height, new Color(Accent.r, Accent.g, Accent.b, 0.65f), SimTranslation.T("RSMF.StaffShift.LegendOn"));
            x = DrawLegendChip(x, rect.y, rect.height, new Color(0.88f, 0.34f, 0.24f, 0.65f), SimTranslation.T("RSMF.StaffShift.LegendGap"));
            DrawLegendChip(x, rect.y, rect.height, Danger, SimTranslation.T("RSMF.StaffShift.LegendConflict"));

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Dim;
            Widgets.Label(rect, SimTranslation.T("RSMF.StaffShift.LegendHint"));
            ResetText();
        }

        //绘制单个图例色块和文字。
        private static float DrawLegendChip(float x, float y, float h, Color color, string text)
        {
            Widgets.DrawBoxSolid(new Rect(x, y + (h - 10f) * 0.5f, 10f, 10f), color);
            float w = Text.CalcSize(text).x + 6f;
            GUI.color = Dim;
            Widgets.Label(new Rect(x + 14f, y, w, h), text);
            return x + 14f + w + 12f;
        }

        //构造单个排班小时格的悬停详情文本。
        private static string BuildHourCellTooltip(int hour, bool on, TimeAssignmentDef timetable, bool conflict)
        {
            string line1 = $"{hour:00}:00 - {SimTranslation.T(on ? "RSMF.StaffShift.StateOn" : "RSMF.StaffShift.StateOff")}";
            string line2 = timetable == null
                ? ""
                : (conflict
                    ? "\n" + SimTranslation.T("RSMF.StaffShift.HourCellConflict", timetable.LabelCap.RawText.Named("timetable"))
                    : "\n" + SimTranslation.T("RSMF.StaffShift.HourCellTimetable", timetable.LabelCap.RawText.Named("timetable")));
            return line1 + line2 + "\n" + SimTranslation.T("RSMF.StaffShift.HourCellDragHint");
        }

        //将原版作息定义映射为排班格底部的辅助色条。
        private static Color TimetableBarColor(TimeAssignmentDef assignment)
        {
            if (assignment == TimeAssignmentDefOf.Sleep) return new Color(0.36f, 0.42f, 0.88f, 0.90f);
            if (assignment == TimeAssignmentDefOf.Joy) return new Color(0.35f, 0.78f, 0.46f, 0.90f);
            if (assignment == TimeAssignmentDefOf.Meditate) return new Color(0.66f, 0.45f, 0.86f, 0.90f);
            if (assignment == TimeAssignmentDefOf.Work) return new Color(0.86f, 0.72f, 0.30f, 0.75f);
            return new Color(0.55f, 0.55f, 0.55f, 0.35f);
        }
    }
}
