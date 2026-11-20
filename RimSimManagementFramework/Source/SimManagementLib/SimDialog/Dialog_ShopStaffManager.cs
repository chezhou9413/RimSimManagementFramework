using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //显示商店岗位、候选员工筛选与对齐式 24 小时排班矩阵窗口。
    public partial class Dialog_ShopStaffManager : Window
    {
        private readonly Zone_Shop zone;
        private Vector2 roleScroll;
        private Vector2 rosterScroll;
        private Vector2 candidateScroll;
        private string selectedRoleDefName;
        private string searchText = "";
        private int activeTab;
        private bool onlyEligibleCandidates = true;
        private readonly int[] roleListCoverageBuffer = new int[24];

        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.18f);
        private static readonly Color PanelAlt = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color Accent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color Dim = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color Ok = new Color(0.35f, 0.80f, 0.45f, 1f);
        private static readonly Color Warn = new Color(0.95f, 0.72f, 0.25f, 1f);
        private static readonly Color Danger = new Color(0.90f, 0.36f, 0.30f, 1f);

        public override Vector2 InitialSize => new Vector2(1180f, 740f);

        //初始化店员与排班窗口，按首个岗位是否已有员工自动落到排班表或添加页。
        public Dialog_ShopStaffManager(Zone_Shop zone)
        {
            this.zone = zone;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = false;
            forcePause = false;

            ShopStaffRoleDef first = ShopStaffUtility.GetVisibleRoles(zone).FirstOrDefault();
            selectedRoleDefName = first?.defName ?? "";
            if (first != null && zone.GetAssignedPawnRecords(first.defName, true).Count == 0)
                activeTab = 1;
        }

        //绘制窗口主体内容并维护排班矩阵拖拽状态。
        public override void DoWindowContents(Rect inRect)
        {
            UpdateShiftPaintState();
            try
            {
                Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, 52f);
                DrawHeader(headerRect);

                Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 8f, inRect.width, inRect.height - headerRect.height - 8f);
                float leftW = Mathf.Clamp(bodyRect.width * 0.25f, 260f, 320f);
                Rect leftRect = new Rect(bodyRect.x, bodyRect.y, leftW, bodyRect.height);
                Rect rightRect = new Rect(leftRect.xMax + 10f, bodyRect.y, bodyRect.width - leftW - 10f, bodyRect.height);

                DrawRoleList(leftRect);
                DrawRoleDetailPanel(rightRect);
            }
            finally
            {
                ResetText();
            }
        }

        //绘制窗口标题、店铺营业状态与当前地图时刻。
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 12f, rect.y, rect.width * 0.52f, rect.height),
                SimTranslation.T("RSMF.StaffManager.Title", zone.label.CapitalizeFirst().Named("shop")));

            int curHour = zone.Map != null ? GenLocalDate.HourInteger(zone.Map) : 0;
            string statusText = $"{curHour:00}:00 · {SimTranslation.T("RSMF.StaffManager.OpenStatus", zone.GetOpenStatusMessage().Named("status"))}";
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = zone.IsOpenNow() ? Ok : Warn;
            Widgets.Label(new Rect(rect.x + rect.width * 0.48f, rect.y + 2f, rect.width * 0.52f - 12f, rect.height), statusText);
            ResetText();
        }

        //绘制左侧岗位列表，每个岗位内嵌 24 小时迷你排班覆盖条与空窗提醒。
        private void DrawRoleList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(zone);
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 22f);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, SimTranslation.T("RSMF.StaffManager.RoleList"));

            Rect infoRect = new Rect(rect.x + 10f, titleRect.yMax + 2f, rect.width - 20f, 18f);
            Text.Font = GameFont.Tiny;
            GUI.color = Dim;
            Widgets.Label(infoRect, SimTranslation.T("RSMF.StaffManager.RoleListTip").Truncate(infoRect.width));

            float rowH = 82f;
            Rect outRect = new Rect(rect.x + 8f, infoRect.yMax + 8f, rect.width - 16f, Mathf.Max(0f, rect.yMax - infoRect.yMax - 14f));
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, roles.Count * (rowH + 6f)));
            Widgets.BeginScrollView(outRect, ref roleScroll, viewRect);

            ShopScheduleData shopSchedule = zone.GetSchedule();
            int currentHour = zone.Map != null ? GenLocalDate.HourInteger(zone.Map) : 0;

            for (int i = 0; i < roles.Count; i++)
            {
                ShopStaffRoleDef role = roles[i];
                bool selected = role.defName == selectedRoleDefName;
                Rect row = new Rect(0f, i * (rowH + 6f), viewRect.width, rowH);
                Widgets.DrawBoxSolid(row, selected ? new Color(Accent.r, Accent.g, Accent.b, 0.20f) : (i % 2 == 0 ? PanelAlt : new Color(1f, 1f, 1f, 0.02f)));
                SimUiStyle.DrawBorder(row, selected ? Accent : new Color(1f, 1f, 1f, 0.08f));

                List<ShopRoleAssignment> records = zone.GetAssignedPawnRecords(role.defName, true);
                int maxAssigned = ShopStaffUtility.GetMaxAssignedPawns(zone, role);
                int gapHours = ShopStaffShiftUtility.CountRoleOpenGapHours(zone, role.defName, roleListCoverageBuffer, out _, out _);

                //首行：岗位名 + 人数计数。
                string countStr = $"{records.Count}/{(maxAssigned <= 0 ? SimTranslation.T("RSMF.Common.Unlimited") : maxAssigned.ToString())}";
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 6f, row.width - 78f, 22f), role.LabelCap);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = records.Count > 0 ? Ok : Warn;
                Widgets.Label(new Rect(row.xMax - 72f, row.y + 6f, 62f, 22f), countStr);

                //次行：覆盖状态徽章 + 描述摘要。
                string coverageTag = records.Count == 0
                    ? SimTranslation.T("RSMF.StaffShift.RoleUnassignedShort")
                    : (gapHours > 0
                        ? SimTranslation.T("RSMF.StaffShift.RoleGapShort", gapHours.Named("gap"))
                        : SimTranslation.T("RSMF.StaffShift.RoleCoveredShort"));
                Color tagColor = records.Count == 0 ? Dim : (gapHours > 0 ? Warn : Ok);
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = tagColor;
                Widgets.Label(new Rect(row.x + 10f, row.y + 30f, row.width - 20f, 18f), coverageTag);

                //底部：24 小时迷你覆盖条。
                DrawMiniCoverageStrip(new Rect(row.x + 10f, row.y + 54f, row.width - 20f, 16f),
                    roleListCoverageBuffer, shopSchedule, currentHour, records.Count > 0);

                if (!role.description.NullOrEmpty())
                    TooltipHandler.TipRegion(row, role.description);

                if (Widgets.ButtonInvisible(row))
                {
                    selectedRoleDefName = role.defName;
                    if (records.Count == 0) activeTab = 1;
                }
            }

            Widgets.EndScrollView();
            ResetText();
        }

        //绘制右侧选中岗位详情：顶部标签切换（排班表 / 添加员工）及对应内容。
        private void DrawRoleDetailPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            ShopStaffRoleDef role = DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail(selectedRoleDefName);
            if (role == null)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Dim;
                Widgets.Label(rect, SimTranslation.T("RSMF.StaffManager.SelectRole"));
                ResetText();
                return;
            }

            List<Pawn> assigned = zone.GetAssignedPawns(role.defName);
            List<ShopRoleAssignment> assignmentRecords = zone.GetAssignedPawnRecords(role.defName, true);
            List<Pawn> allCandidates = ShopStaffUtility.GetAssignablePawns(zone.Map).Where(p => !assigned.Contains(p)).ToList();

            //顶部标题与双标签页切换栏。
            float pad = 10f;
            float headerY = rect.y + 8f;
            float tabH = 28f;

            string rosterTabLabel = SimTranslation.T("RSMF.StaffShift.TabRoster", assignmentRecords.Count.Named("count"));
            string candTabLabel = SimTranslation.T("RSMF.StaffShift.TabCandidates", allCandidates.Count.Named("count"));
            Text.Font = GameFont.Small;
            float titleW = Mathf.Min(rect.width * 0.36f, Text.CalcSize(role.LabelCap).x + 12f);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + pad, headerY, titleW, tabH), role.LabelCap);

            Text.Font = GameFont.Tiny;
            float rosterTabW = Mathf.Max(104f, Text.CalcSize(rosterTabLabel).x + 24f);
            float candTabW = Mathf.Max(110f, Text.CalcSize(candTabLabel).x + 24f);
            float tabX = rect.x + pad + titleW + 10f;

            if (DrawTabButton(new Rect(tabX, headerY, rosterTabW, tabH), rosterTabLabel, activeTab == 0))
                activeTab = 0;
            if (DrawTabButton(new Rect(tabX + rosterTabW + 6f, headerY, candTabW, tabH), candTabLabel, activeTab == 1))
                activeTab = 1;
            ResetText();

            Rect contentRect = new Rect(rect.x + pad, headerY + tabH + 8f, rect.width - pad * 2f,
                Mathf.Max(0f, rect.yMax - (headerY + tabH + 8f) - pad));

            if (activeTab == 0)
                DrawShiftMatrixTab(contentRect, role, assignmentRecords);
            else
                DrawCandidatesTab(contentRect, role, assigned, allCandidates);
        }

        //绘制候选员工页：支持搜索、仅看可指派筛选、按岗位技能优先排序并展示核心属性。
        private void DrawCandidatesTab(Rect rect, ShopStaffRoleDef role, List<Pawn> assigned, List<Pawn> allCandidates)
        {
            float barH = Mathf.Max(28f, Text.LineHeightOf(GameFont.Small) + 6f);
            Rect searchRect = new Rect(rect.x, rect.y, Mathf.Min(240f, rect.width * 0.4f), barH);
            searchText = Widgets.TextField(searchRect, searchText);

            string onlyLabel = SimTranslation.T("RSMF.StaffShift.OnlyEligible");
            Text.Font = GameFont.Tiny;
            float toggleW = Mathf.Max(110f, Text.CalcSize(onlyLabel).x + 34f);
            Rect toggleRect = new Rect(searchRect.xMax + 14f, rect.y + (barH - 22f) * 0.5f, toggleW, 22f);
            Widgets.CheckboxLabeled(toggleRect, onlyLabel, ref onlyEligibleCandidates);
            ResetText();

            int maxAssigned = ShopStaffUtility.GetMaxAssignedPawns(zone, role);
            bool canAddMore = maxAssigned <= 0 || assigned.Count < maxAssigned;

            var evaluated = new List<(Pawn pawn, ShopStaffUtility.StaffEligibility elig, float score)>(allCandidates.Count);
            for (int i = 0; i < allCandidates.Count; i++)
            {
                Pawn p = allCandidates[i];
                if (!string.IsNullOrEmpty(searchText) && p.LabelShortCap.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                ShopStaffUtility.StaffEligibility elig = ShopStaffUtility.EvaluateEligibility(zone, p, role);
                if (onlyEligibleCandidates && !elig.Eligible)
                    continue;
                evaluated.Add((p, elig, ScoreCandidateForRole(p, role)));
            }
            evaluated.Sort((a, b) =>
            {
                int eCmp = b.elig.Eligible.CompareTo(a.elig.Eligible);
                if (eCmp != 0) return eCmp;
                int sCmp = b.score.CompareTo(a.score);
                if (sCmp != 0) return sCmp;
                return string.CompareOrdinal(a.pawn.LabelShortCap, b.pawn.LabelShortCap);
            });

            Rect outRect = new Rect(rect.x, searchRect.yMax + 8f, rect.width, Mathf.Max(0f, rect.yMax - searchRect.yMax - 8f));
            float rowH = 74f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, evaluated.Count * (rowH + 6f)));
            Widgets.BeginScrollView(outRect, ref candidateScroll, viewRect);

            for (int i = 0; i < evaluated.Count; i++)
            {
                Pawn pawn = evaluated[i].pawn;
                ShopStaffUtility.StaffEligibility eligibility = evaluated[i].elig;
                bool canAssign = eligibility.Eligible && canAddMore;
                Rect row = new Rect(0f, i * (rowH + 6f), viewRect.width, rowH);
                Color rowFill = !eligibility.Eligible
                    ? new Color(0.90f, 0.35f, 0.35f, 0.08f)
                    : (i % 2 == 0 ? PanelAlt : new Color(1f, 1f, 1f, 0.02f));
                Widgets.DrawBoxSolid(row, rowFill);
                SimUiStyle.DrawBorder(row, !eligibility.Eligible ? new Color(0.90f, 0.35f, 0.35f, 0.25f) : new Color(1f, 1f, 1f, 0.07f));

                Rect portraitRect = new Rect(row.x + 8f, row.y + 7f, 60f, 60f);
                RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(96f, 96f), Rot4.South);
                GUI.color = Color.white;
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit, true);

                Rect infoRect = new Rect(portraitRect.xMax + 10f, row.y + 6f, Mathf.Max(80f, row.width - 252f), 62f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 22f), pawn.LabelShortCap);

                Text.Font = GameFont.Tiny;
                GUI.color = Accent;
                Widgets.Label(new Rect(infoRect.x, infoRect.y + 22f, infoRect.width, 18f), BuildCandidateSkillSummary(pawn).Truncate(infoRect.width));

                GUI.color = eligibility.Eligible ? Dim : Warn;
                string thirdLine = eligibility.Eligible
                    ? SimTranslation.T("RSMF.StaffManager.CurrentJob", (pawn.CurJobDef?.LabelCap.RawText ?? SimTranslation.T("RSMF.StaffManager.Idle")).Named("job"))
                    : SimTranslation.T("RSMF.StaffManager.Ineligible", eligibility.Reason.Truncate(infoRect.width).Named("reason"));
                Widgets.Label(new Rect(infoRect.x, infoRect.y + 41f, infoRect.width, 18f), thirdLine.Truncate(infoRect.width));
                if (!eligibility.Eligible && !eligibility.Reason.NullOrEmpty())
                    TooltipHandler.TipRegion(new Rect(infoRect.x, infoRect.y + 41f, infoRect.width, 18f), eligibility.Reason);

                Rect selectRect = new Rect(row.xMax - 172f, row.y + 22f, 54f, 28f);
                Rect actionRect = new Rect(row.xMax - 110f, row.y + 22f, 100f, 28f);
                if (SimUiStyle.DrawSecondaryButton(selectRect, SimTranslation.T("RSMF.Common.Locate"), pawn.Spawned, GameFont.Tiny))
                {
                    Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                    CameraJumper.TryJump(pawn.Position, pawn.Map);
                }

                if (SimUiStyle.DrawPrimaryButton(actionRect, SimTranslation.T("RSMF.StaffManager.AddRole"), canAssign, GameFont.Tiny))
                    zone.AddAssignedPawn(role.defName, pawn, maxAssigned);
            }

            Widgets.EndScrollView();
            ResetText();
        }

        //按岗位类型评估候选员工的排序参考分，方便把高社交或高烹饪人员排在靠前位置。
        private static float ScoreCandidateForRole(Pawn pawn, ShopStaffRoleDef role)
        {
            if (pawn == null) return 0f;
            float speed = pawn.GetStatValue(StatDefOf.WorkSpeedGlobal);
            if (pawn.skills == null) return speed * 10f;
            string defName = role?.defName ?? "";
            if (defName.IndexOf("Chef", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Cook", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return pawn.skills.GetSkill(SkillDefOf.Cooking).Level * 10f + speed;
            if (defName.IndexOf("Cashier", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Reception", System.StringComparison.OrdinalIgnoreCase) >= 0
                || defName.IndexOf("Waiter", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return pawn.skills.GetSkill(SkillDefOf.Social).Level * 10f + speed;
            return speed * 10f;
        }

        //构造候选人的关键技能与工速摘要。
        private static string BuildCandidateSkillSummary(Pawn pawn)
        {
            if (pawn == null) return "";
            if (pawn.IsColonyMech)
                return $"{SimTranslation.T("RSMF.StaffShift.MechLabel")} · {Mathf.RoundToInt(pawn.GetStatValue(StatDefOf.WorkSpeedGlobal) * 100f)}%";
            int social = pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            int cooking = pawn.skills?.GetSkill(SkillDefOf.Cooking)?.Level ?? 0;
            int speedPct = Mathf.RoundToInt(pawn.GetStatValue(StatDefOf.WorkSpeedGlobal) * 100f);
            return SimTranslation.T("RSMF.StaffShift.SkillSummary", social.Named("social"), cooking.Named("cooking"), speedPct.Named("speed"));
        }

        //绘制分页切换按钮。
        private static bool DrawTabButton(Rect rect, string label, bool selected)
        {
            return selected
                ? SimUiStyle.DrawPrimaryButton(rect, label, true, GameFont.Tiny)
                : SimUiStyle.DrawSecondaryButton(rect, label, true, GameFont.Tiny);
        }

        //绘制 24 小时微型覆盖色条，供左侧岗位列表与总览面板共享。
        internal static void DrawMiniCoverageStrip(Rect rect, int[] hourlyCounts, ShopScheduleData schedule, int currentHour, bool hasAssigned)
        {
            float cellW = rect.width / 24f;
            for (int h = 0; h < 24; h++)
            {
                Rect cell = new Rect(rect.x + h * cellW, rect.y, Mathf.Max(1f, cellW - 0.5f), rect.height);
                bool open = ShopStaffShiftUtility.IsShopScheduledOpenAtHour(schedule, h);
                int count = hourlyCounts[h];
                Color fill;
                if (!hasAssigned)
                    fill = open ? new Color(0.35f, 0.52f, 0.65f, 0.28f) : new Color(1f, 1f, 1f, 0.05f);
                else if (count > 0)
                    fill = new Color(0.24f, 0.72f, 0.42f, count > 1 ? 0.68f : 0.45f);
                else
                    fill = open ? new Color(0.90f, 0.35f, 0.24f, 0.48f) : new Color(1f, 1f, 1f, 0.05f);
                Widgets.DrawBoxSolid(cell, fill);
                if (h == currentHour)
                    SimUiStyle.DrawBorder(cell, Color.white);
            }
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));
        }

        //构建已离图员工的来源详情说明。
        private string BuildAssignmentDetail(ShopRoleAssignment assignment)
        {
            if (assignment == null) return "";
            if (assignment.HasUsablePawnOn(zone.Map))
                return SimTranslation.T("RSMF.StaffManager.AssignedCurrentMap");

            string faction = assignment.factionLabel.NullOrEmpty() ? SimTranslation.T("RSMF.Common.Unknown") : assignment.factionLabel;
            string id = assignment.pawnThingId >= 0 ? assignment.pawnThingId.ToString() : SimTranslation.T("RSMF.Common.Unknown");
            return SimTranslation.T("RSMF.StaffManager.AssignedUnavailableDetail", faction.Named("faction"), id.Named("id"));
        }

        //恢复 IMGUI 文本与颜色状态。
        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
