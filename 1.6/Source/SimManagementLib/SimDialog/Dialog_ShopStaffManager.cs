using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 显示商店岗位和员工分配窗口，负责添加当前地图员工并清理已经离开的历史分配记录。
    /// </summary>
    public class Dialog_ShopStaffManager : Window
    {
        private readonly Zone_Shop zone;
        private Vector2 roleScroll;
        private Vector2 pawnScroll;
        private string selectedRoleDefName;
        private string searchText = "";

        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.18f);
        private static readonly Color PanelAlt = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color Accent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color Dim = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color Ok = new Color(0.35f, 0.80f, 0.45f, 1f);
        private static readonly Color Warn = new Color(0.95f, 0.72f, 0.25f, 1f);

        public override Vector2 InitialSize => new Vector2(1080f, 700f);

        /// <summary>
        /// 初始化店员配置窗口。
        /// </summary>
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
        }

        /// <summary>
        /// 绘制窗口主体内容。
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, 56f);
            DrawHeader(headerRect);

            Rect bodyRect = new Rect(inRect.x, headerRect.yMax + 8f, inRect.width, inRect.height - headerRect.height - 8f);
            Rect leftRect = new Rect(bodyRect.x, bodyRect.y, 360f, bodyRect.height);
            Rect rightRect = new Rect(leftRect.xMax + 10f, bodyRect.y, bodyRect.width - leftRect.width - 10f, bodyRect.height);

            DrawRoleList(leftRect);
            DrawPawnPanel(rightRect);
        }

        /// <summary>
        /// 绘制窗口标题和营业状态。
        /// </summary>
        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 12f, rect.y, rect.width - 220f, rect.height), SimTranslation.T("RSMF.StaffManager.Title", zone.label.CapitalizeFirst().Named("shop")));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = zone.IsOpenNow() ? Ok : Warn;
            Widgets.Label(new Rect(rect.x + 240f, rect.y + 2f, rect.width - 252f, rect.height), SimTranslation.T("RSMF.StaffManager.OpenStatus", zone.GetOpenStatusMessage().Named("status")));

            ResetText();
        }

        /// <summary>
        /// 绘制岗位列表和岗位人数摘要。
        /// </summary>
        private void DrawRoleList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(zone);
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 24f);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, SimTranslation.T("RSMF.StaffManager.RoleList"));

            Rect infoRect = new Rect(rect.x + 10f, titleRect.yMax + 2f, rect.width - 20f, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = Dim;
            Widgets.Label(infoRect, SimTranslation.T("RSMF.StaffManager.RoleListTip"));

            Rect outRect = new Rect(rect.x + 8f, infoRect.yMax + 6f, rect.width - 16f, rect.height - 70f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, roles.Count * 84f));
            Widgets.BeginScrollView(outRect, ref roleScroll, viewRect);

            for (int i = 0; i < roles.Count; i++)
            {
                ShopStaffRoleDef role = roles[i];
                bool selected = role.defName == selectedRoleDefName;
                Rect row = new Rect(0f, i * 84f, viewRect.width, 76f);
                Widgets.DrawBoxSolid(row, selected ? new Color(Accent.r, Accent.g, Accent.b, 0.18f) : (i % 2 == 0 ? PanelAlt : new Color(1f, 1f, 1f, 0.02f)));
                SimUiStyle.DrawBorder(row, selected ? Accent : new Color(1f, 1f, 1f, 0.08f));

                List<Pojo.ShopRoleAssignment> assignmentRecords = zone.GetAssignedPawnRecords(role.defName, true);
                int maxAssigned = ShopStaffUtility.GetMaxAssignedPawns(zone, role);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 4f, row.width - 20f, 24f), role.LabelCap);

                Text.Font = GameFont.Tiny;
                GUI.color = Dim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 28f, row.width - 20f, 20f), role.description.NullOrEmpty() ? SimTranslation.T("RSMF.Common.NoDescription") : role.description);

                GUI.color = assignmentRecords.Count > 0 ? Ok : Warn;
                Widgets.Label(new Rect(row.x + 10f, row.y + 48f, row.width - 20f, 20f), SimTranslation.T("RSMF.StaffManager.AssignedCount",
                    assignmentRecords.Count.Named("assigned"),
                    (maxAssigned <= 0 ? SimTranslation.T("RSMF.Common.Unlimited") : maxAssigned.ToString()).Named("max")));

                if (Widgets.ButtonInvisible(row))
                    selectedRoleDefName = role.defName;
            }

            Widgets.EndScrollView();
            ResetText();
        }

        /// <summary>
        /// 绘制当前岗位的已分配员工和可加入候选员工。
        /// </summary>
        private void DrawPawnPanel(Rect rect)
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

            Rect titleRect = new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, SimTranslation.T("RSMF.StaffManager.CandidateTitle", role.LabelCap.Named("role")));

            Rect searchRect = new Rect(rect.x + 12f, titleRect.yMax + 6f, 220f, 28f);
            searchText = Widgets.TextField(searchRect, searchText);

            Rect summaryRect = new Rect(searchRect.xMax + 12f, searchRect.y, rect.width - 256f, 28f);
            List<Pawn> assigned = zone.GetAssignedPawns(role.defName);
            List<Pojo.ShopRoleAssignment> assignmentRecords = zone.GetAssignedPawnRecords(role.defName, true);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = assignmentRecords.Count > 0 ? Ok : Warn;
            Widgets.Label(summaryRect, SimTranslation.T("RSMF.StaffManager.CurrentAssigned",
                string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), assignmentRecords.Select(a => FormatAssignmentSummary(a)).DefaultIfEmpty(SimTranslation.T("RSMF.StaffManager.Unassigned"))).Named("pawns")));

            IEnumerable<Pawn> pawns = ShopStaffUtility.GetAssignablePawns(zone.Map);
            if (!string.IsNullOrEmpty(searchText))
                pawns = pawns.Where(p => p.LabelShortCap.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
            pawns = pawns.Where(p => !assigned.Contains(p));
            pawns = pawns.Where(p => ShopStaffUtility.EvaluateEligibility(zone, p, role).Eligible);

            List<Pawn> list = pawns.ToList();
            Rect outRect = new Rect(rect.x + 10f, summaryRect.yMax + 10f, rect.width - 20f, rect.height - 64f);
            int rowCount = assignmentRecords.Count + list.Count;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, rowCount * 88f));
            Widgets.BeginScrollView(outRect, ref pawnScroll, viewRect);

            float curY = 0f;
            for (int i = 0; i < assignmentRecords.Count; i++)
            {
                DrawAssignedRecordRow(new Rect(0f, curY, viewRect.width, 80f), assignmentRecords[i], role, i);
                curY += 88f;
            }

            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                bool isAssigned = false;
                int maxAssigned = ShopStaffUtility.GetMaxAssignedPawns(zone, role);
                ShopStaffUtility.StaffEligibility eligibility = ShopStaffUtility.EvaluateEligibility(zone, pawn, role);
                bool canAdd = maxAssigned <= 0 || assigned.Count < maxAssigned || isAssigned;
                bool canAssign = eligibility.Eligible && canAdd;
                Rect row = new Rect(0f, curY, viewRect.width, 80f);
                curY += 88f;
                Color rowFill = isAssigned
                    ? new Color(Accent.r, Accent.g, Accent.b, 0.14f)
                    : (!eligibility.Eligible ? new Color(0.90f, 0.35f, 0.35f, 0.08f) : (i % 2 == 0 ? PanelAlt : new Color(1f, 1f, 1f, 0.02f)));
                Widgets.DrawBoxSolid(row, rowFill);
                SimUiStyle.DrawBorder(row, isAssigned ? Accent : (!eligibility.Eligible ? new Color(0.90f, 0.35f, 0.35f, 0.25f) : new Color(1f, 1f, 1f, 0.07f)));

                Rect portraitRect = new Rect(row.x + 8f, row.y + 8f, 64f, 64f);
                RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(96f, 96f), Rot4.South);
                GUI.color = Color.white;
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit, true);

                Rect infoRect = new Rect(portraitRect.xMax + 10f, row.y + 8f, row.width - 220f, 64f);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 24f), pawn.LabelShortCap);

                Text.Font = GameFont.Tiny;
                GUI.color = Dim;
                string curJob = pawn.CurJobDef?.LabelCap.RawText ?? SimTranslation.T("RSMF.StaffManager.Idle");
                Widgets.Label(new Rect(infoRect.x, infoRect.y + 22f, infoRect.width, 18f), SimTranslation.T("RSMF.StaffManager.CurrentJob", curJob.Named("job")));
                GUI.color = eligibility.Eligible ? Dim : Warn;
                Widgets.Label(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), eligibility.Eligible
                    ? SimTranslation.T("RSMF.StaffManager.MapLine", (pawn.Map?.info?.parent?.LabelCap ?? pawn.Map?.ToString() ?? SimTranslation.T("RSMF.Common.Unknown")).Named("map"))
                    : SimTranslation.T("RSMF.StaffManager.Ineligible", eligibility.Reason.Truncate(infoRect.width).Named("reason")));
                if (!eligibility.Eligible && !eligibility.Reason.NullOrEmpty())
                    TooltipHandler.TipRegion(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), eligibility.Reason);

                Rect selectRect = new Rect(row.xMax - 174f, row.y + 22f, 54f, 30f);
                Rect actionRect = new Rect(row.xMax - 112f, row.y + 22f, 104f, 30f);

                if (SimUiStyle.DrawSecondaryButton(selectRect, SimTranslation.T("RSMF.Common.Locate"), pawn.Spawned, GameFont.Tiny))
                {
                    Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                    CameraJumper.TryJump(pawn.Position, pawn.Map);
                }

                if (isAssigned)
                {
                    if (SimUiStyle.DrawDangerButton(actionRect, SimTranslation.T("RSMF.StaffManager.RemoveRole"), true, GameFont.Tiny))
                        zone.RemoveAssignedPawn(role.defName, pawn);
                }
                else
                {
                    if (SimUiStyle.DrawPrimaryButton(actionRect, SimTranslation.T("RSMF.StaffManager.AddRole"), canAssign, GameFont.Tiny))
                        zone.AddAssignedPawn(role.defName, pawn, maxAssigned);
                }
            }

            Widgets.EndScrollView();
            ResetText();
        }

        /// <summary>
        /// 绘制一条已分配员工记录，负责让离图或失效员工也能被移出岗位。
        /// </summary>
        private void DrawAssignedRecordRow(Rect row, Pojo.ShopRoleAssignment assignment, ShopStaffRoleDef role, int index)
        {
            if (assignment == null || role == null) return;

            bool usable = assignment.HasUsablePawnOn(zone.Map);
            Color rowFill = usable
                ? new Color(Accent.r, Accent.g, Accent.b, 0.14f)
                : new Color(0.95f, 0.55f, 0.20f, 0.10f);
            Widgets.DrawBoxSolid(row, rowFill);
            SimUiStyle.DrawBorder(row, usable ? Accent : new Color(0.95f, 0.55f, 0.20f, 0.35f));

            Pawn pawn = assignment.pawn;
            Rect portraitRect = new Rect(row.x + 8f, row.y + 8f, 64f, 64f);
            if (pawn != null && !pawn.Destroyed)
            {
                RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(96f, 96f), Rot4.South);
                GUI.color = Color.white;
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit, true);
            }
            else
            {
                Widgets.DrawBoxSolid(portraitRect, new Color(1f, 1f, 1f, 0.06f));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Dim;
                Widgets.Label(portraitRect, "?");
            }

            Rect infoRect = new Rect(portraitRect.xMax + 10f, row.y + 8f, row.width - 220f, 64f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 24f), assignment.DisplayLabel());

            Text.Font = GameFont.Tiny;
            GUI.color = usable ? Ok : Warn;
            Widgets.Label(new Rect(infoRect.x, infoRect.y + 22f, infoRect.width, 18f), usable
                ? SimTranslation.T("RSMF.StaffManager.AssignedUsable")
                : SimTranslation.T("RSMF.StaffManager.AssignedUnavailable"));

            GUI.color = Dim;
            string detail = BuildAssignmentDetail(assignment);
            Widgets.Label(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), detail.Truncate(infoRect.width));
            if (!detail.NullOrEmpty())
                TooltipHandler.TipRegion(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), detail);

            Rect selectRect = new Rect(row.xMax - 174f, row.y + 22f, 54f, 30f);
            Rect actionRect = new Rect(row.xMax - 112f, row.y + 22f, 104f, 30f);
            bool canLocate = pawn != null && pawn.Spawned && pawn.Map != null;
            if (SimUiStyle.DrawSecondaryButton(selectRect, SimTranslation.T("RSMF.Common.Locate"), canLocate, GameFont.Tiny))
            {
                Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                CameraJumper.TryJump(pawn.Position, pawn.Map);
            }

            if (SimUiStyle.DrawDangerButton(actionRect, SimTranslation.T("RSMF.StaffManager.RemoveRole"), true, GameFont.Tiny))
                zone.RemoveAssignedPawnRecord(role.defName, assignment);
        }

        /// <summary>
        /// 构建已分配员工摘要，负责在顶部摘要中标出离图记录。
        /// </summary>
        private string FormatAssignmentSummary(Pojo.ShopRoleAssignment assignment)
        {
            if (assignment == null) return SimTranslation.T("RSMF.Common.Unknown");
            string label = assignment.DisplayLabel();
            return assignment.HasUsablePawnOn(zone.Map)
                ? label
                : SimTranslation.T("RSMF.StaffManager.AssignedSummaryUnavailable", label.Named("pawn"));
        }

        /// <summary>
        /// 构建已分配员工详情，负责显示派系、编号和不可用状态。
        /// </summary>
        private string BuildAssignmentDetail(Pojo.ShopRoleAssignment assignment)
        {
            if (assignment == null) return "";
            if (assignment.HasUsablePawnOn(zone.Map))
                return SimTranslation.T("RSMF.StaffManager.AssignedCurrentMap");

            string faction = assignment.factionLabel.NullOrEmpty() ? SimTranslation.T("RSMF.Common.Unknown") : assignment.factionLabel;
            string id = assignment.pawnThingId >= 0 ? assignment.pawnThingId.ToString() : SimTranslation.T("RSMF.Common.Unknown");
            return SimTranslation.T("RSMF.StaffManager.AssignedUnavailableDetail", faction.Named("faction"), id.Named("id"));
        }

        /// <summary>
        /// 恢复 IMGUI 文本和颜色状态。
        /// </summary>
        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
