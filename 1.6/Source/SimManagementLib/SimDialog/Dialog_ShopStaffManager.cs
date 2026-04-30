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

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 12f, rect.y, rect.width - 220f, rect.height), $"{zone.label.CapitalizeFirst()} 的店员配置");

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = zone.IsValidShop() ? Ok : Warn;
            Widgets.Label(new Rect(rect.x + 240f, rect.y + 2f, rect.width - 252f, rect.height), zone.IsValidShop() ? "商店状态: 有效" : $"商店状态: {zone.GetValidationMessage()}");

            ResetText();
        }

        private void DrawRoleList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.10f));

            List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(zone);
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 24f);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, "岗位列表");

            Rect infoRect = new Rect(rect.x + 10f, titleRect.yMax + 2f, rect.width - 20f, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = Dim;
            Widgets.Label(infoRect, "岗位会根据店铺内已有建筑和岗位 Def 自动显示。");

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

                List<Pawn> assigned = zone.GetAssignedPawns(role.defName);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 10f, row.y + 4f, row.width - 20f, 24f), role.LabelCap);

                Text.Font = GameFont.Tiny;
                GUI.color = Dim;
                Widgets.Label(new Rect(row.x + 10f, row.y + 28f, row.width - 20f, 20f), role.description.NullOrEmpty() ? "无描述" : role.description);

                GUI.color = assigned.Count > 0 ? Ok : Warn;
                Widgets.Label(new Rect(row.x + 10f, row.y + 48f, row.width - 20f, 20f), $"已分配: {assigned.Count}/{(role.MaxAssignedPawns <= 0 ? "无限" : role.MaxAssignedPawns.ToString())}");

                if (Widgets.ButtonInvisible(row))
                    selectedRoleDefName = role.defName;
            }

            Widgets.EndScrollView();
            ResetText();
        }

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
                Widgets.Label(rect, "请选择一个岗位");
                ResetText();
                return;
            }

            Rect titleRect = new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(titleRect, $"{role.LabelCap} 候选店员");

            Rect searchRect = new Rect(rect.x + 12f, titleRect.yMax + 6f, 220f, 28f);
            searchText = Widgets.TextField(searchRect, searchText);

            Rect summaryRect = new Rect(searchRect.xMax + 12f, searchRect.y, rect.width - 256f, 28f);
            List<Pawn> assigned = zone.GetAssignedPawns(role.defName);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = assigned.Count > 0 ? Ok : Warn;
            Widgets.Label(summaryRect, $"当前分配: {string.Join("、", assigned.Select(p => p.LabelShortCap).DefaultIfEmpty("未指定"))}");

            IEnumerable<Pawn> pawns = ShopStaffUtility.GetAssignablePawns(zone.Map);
            if (!string.IsNullOrEmpty(searchText))
                pawns = pawns.Where(p => p.LabelShortCap.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0);

            List<Pawn> list = pawns.ToList();
            Rect outRect = new Rect(rect.x + 10f, summaryRect.yMax + 10f, rect.width - 20f, rect.height - 64f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, list.Count * 88f));
            Widgets.BeginScrollView(outRect, ref pawnScroll, viewRect);

            for (int i = 0; i < list.Count; i++)
            {
                Pawn pawn = list[i];
                bool isAssigned = assigned.Contains(pawn);
                ShopStaffUtility.StaffEligibility eligibility = ShopStaffUtility.EvaluateEligibility(pawn, role);
                bool canAdd = role.MaxAssignedPawns <= 0 || assigned.Count < role.MaxAssignedPawns || isAssigned;
                bool canAssign = eligibility.Eligible && canAdd;
                Rect row = new Rect(0f, i * 88f, viewRect.width, 80f);
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
                string curJob = pawn.CurJobDef?.LabelCap.RawText ?? "空闲";
                Widgets.Label(new Rect(infoRect.x, infoRect.y + 22f, infoRect.width, 18f), $"当前工作: {curJob}");
                GUI.color = eligibility.Eligible ? Dim : Warn;
                Widgets.Label(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), eligibility.Eligible ? $"地图: {pawn.Map?.info?.parent?.LabelCap ?? pawn.Map?.ToString() ?? "未知"}" : $"不可指派: {eligibility.Reason.Truncate(infoRect.width)}");
                if (!eligibility.Eligible && !eligibility.Reason.NullOrEmpty())
                    TooltipHandler.TipRegion(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), eligibility.Reason);

                Rect selectRect = new Rect(row.xMax - 174f, row.y + 22f, 54f, 30f);
                Rect actionRect = new Rect(row.xMax - 112f, row.y + 22f, 104f, 30f);

                if (SimUiStyle.DrawSecondaryButton(selectRect, "定位", pawn.Spawned, GameFont.Tiny))
                {
                    Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
                    CameraJumper.TryJump(pawn.Position, pawn.Map);
                }

                if (isAssigned)
                {
                    if (SimUiStyle.DrawDangerButton(actionRect, "移出岗位", true, GameFont.Tiny))
                        zone.RemoveAssignedPawn(role.defName, pawn);
                }
                else
                {
                    if (SimUiStyle.DrawPrimaryButton(actionRect, "加入岗位", canAssign, GameFont.Tiny))
                        zone.AddAssignedPawn(role.defName, pawn, role.MaxAssignedPawns);
                }
            }

            Widgets.EndScrollView();
            ResetText();
        }

        private static void ResetText()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
