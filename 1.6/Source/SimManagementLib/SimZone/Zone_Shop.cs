using HarmonyLib;
using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimDialog;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimZone
{
    /// <summary>
    /// 表示玩家划定的商店区域，负责营业条件验证、店员岗位绑定和商店管理入口。
    /// </summary>
    public partial class Zone_Shop : Zone
    {
        private static readonly FieldInfo ZoneGridField = AccessTools.Field(typeof(ZoneManager), "zoneGrid");
        private static readonly FieldInfo CellsShuffledField = AccessTools.Field(typeof(Zone), "cellsShuffled");

        private List<ShopRoleAssignment> roleAssignments = new List<ShopRoleAssignment>();
        private ShopScheduleData schedule = new ShopScheduleData();

        public Zone_Shop()
        {
        }

        public Zone_Shop(ZoneManager zoneManager) : base(SimTranslation.T("RSMF.Zone.ShopArea"), zoneManager)
        {
        }

        protected override Color NextZoneColor => new Color(0.9f, 0.7f, 0.2f, 0.3f);

        /// <summary>
        /// 判断商店区域当前是否满足营业条件。
        /// </summary>
        public bool IsValidShop()
        {
            return GetCachedValidShopNow(out _);
        }

        /// <summary>
        /// 判断商店区域当前是否允许营业，要求设施有效且营业开关和日程均允许。
        /// </summary>
        public bool IsOpenNow()
        {
            return GetCachedOpenNow();
        }

        /// <summary>
        /// 返回商店当前是否能接待顾客和安排店员工作的说明文本。
        /// </summary>
        public string GetOpenStatusMessage()
        {
            if (!ComputeValidShopNow(out string validationMessage))
                return validationMessage;

            ShopScheduleData data = GetSchedule();
            if (!data.manualOpen)
                return SimTranslation.T("RSMF.Zone.OpenStatus.ManuallyClosed");
            if (data.useSchedule && !data.IsOpenNow(Map))
                return SimTranslation.T("RSMF.Zone.OpenStatus.OutOfSchedule", data.GetScheduleSummary().Named("schedule"));

            return SimTranslation.T("RSMF.Zone.OpenStatus.Open");
        }

        /// <summary>
        /// 返回商店营业日程数据，并在旧存档缺失时创建默认配置。
        /// </summary>
        public ShopScheduleData GetSchedule()
        {
            if (schedule == null)
                schedule = new ShopScheduleData();
            return schedule;
        }

        /// <summary>
        /// 用指定日程覆盖商店当前营业设置。
        /// </summary>
        public void ApplySchedule(ShopScheduleData newSchedule)
        {
            GetSchedule().CopyFrom(newSchedule);
            InvalidateShopRuntimeCache();
        }

        /// <summary>
        /// 返回商店区域当前营业条件的说明文本。
        /// </summary>
        public string GetValidationMessage()
        {
            GetCachedValidShopNow(out string message);
            return message;
        }

        /// <summary>
        /// 实时扫描区域内设施和室内状态，计算商店是否可以营业。
        /// </summary>
        private bool ComputeValidShopNow(out string message)
        {
            if (Map == null || Cells == null || Cells.Count == 0)
            {
                message = SimTranslation.T("RSMF.Zone.Validation.Empty");
                return false;
            }

            RepairEmbeddedFacilityCoverage();

            bool hasStorage = false;
            bool hasCashRegister = false;
            bool hasServiceProvider = false;
            int outdoorCount = 0;
            IntVec3 firstOutdoorCell = IntVec3.Invalid;

            foreach (IntVec3 cell in Cells)
            {
                Room room = cell.GetRoom(Map);
                bool indoors = Map.roofGrid.Roofed(cell) || (room != null && !room.PsychologicallyOutdoors);
                if (!indoors)
                {
                    outdoorCount++;
                    if (!firstOutdoorCell.IsValid)
                        firstOutdoorCell = cell;
                }

                List<Thing> things = Map.thingGrid.ThingsListAt(cell);
                foreach (Thing thing in things)
                {
                    if (thing is Building_SimContainer) hasStorage = true;
                    if (thing is Building_CashRegister) hasCashRegister = true;
                    if (ShopServiceUtility.HasEnabledService(thing)) hasServiceProvider = true;
                }
            }

            if (outdoorCount > 0)
            {
                message = SimTranslation.T("RSMF.Zone.Validation.OutdoorCells", outdoorCount.Named("count"), firstOutdoorCell.Named("cell"));
                return false;
            }

            if (!hasStorage && !hasServiceProvider && !hasCashRegister)
            {
                message = SimTranslation.T("RSMF.Zone.Validation.MissingStorageServiceAndRegister");
                return false;
            }

            if (!hasStorage && !hasServiceProvider)
            {
                message = SimTranslation.T("RSMF.Zone.Validation.MissingStorageOrService");
                return false;
            }

            if (!hasCashRegister)
            {
                message = SimTranslation.T("RSMF.Zone.Validation.MissingCashRegister");
                return false;
            }

            message = SimTranslation.T("RSMF.Zone.Validation.Valid");
            return true;
        }

        /// <summary>
        /// 修复先划商店区再建造设施时可能被旧区划重叠逻辑切掉的设施占用格。
        /// </summary>
        private void RepairEmbeddedFacilityCoverage()
        {
            if (Map == null || Cells == null || Cells.Count == 0) return;

            HashSet<Thing> candidates = new HashSet<Thing>();
            foreach (IntVec3 cell in Cells)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        IntVec3 near = new IntVec3(cell.x + dx, 0, cell.z + dz);
                        if (!near.InBounds(Map)) continue;

                        List<Thing> things = Map.thingGrid.ThingsListAt(near);
                        for (int i = 0; i < things.Count; i++)
                        {
                            Thing thing = things[i];
                            if (thing is Building_SimContainer || thing is Building_CashRegister || ShopServiceUtility.HasEnabledService(thing))
                                candidates.Add(thing);
                        }
                    }
                }
            }

            bool changed = false;
            foreach (Thing thing in candidates)
            {
                if (!ShouldRepairFacilityCoverage(thing)) continue;

                CellRect rect = thing.OccupiedRect();
                for (int z = rect.minZ; z <= rect.maxZ; z++)
                {
                    for (int x = rect.minX; x <= rect.maxX; x++)
                    {
                        IntVec3 cell = new IntVec3(x, 0, z);
                        if (!cell.InBounds(Map)) continue;
                        Zone zone = Map.zoneManager.ZoneAt(cell);
                        if (zone != null && zone != this) continue;

                        if (EnsureShopCell(cell, false))
                            changed = true;
                    }
                }
            }

            if (changed)
                CheckContiguous();
        }

        /// <summary>
        /// 判断设施占用格是否像是被商店区域包围或部分覆盖，需要重新纳入商店区。
        /// </summary>
        private bool ShouldRepairFacilityCoverage(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.Map != Map) return false;
            if (!(thing is Building_SimContainer) && !(thing is Building_CashRegister) && !ShopServiceUtility.HasEnabledService(thing)) return false;

            bool hasShopCell = false;
            bool hasMissingCell = false;
            CellRect rect = thing.OccupiedRect();
            for (int z = rect.minZ; z <= rect.maxZ; z++)
            {
                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(Map)) continue;

                    Zone zone = Map.zoneManager.ZoneAt(cell);
                    if (zone == this)
                        hasShopCell = true;
                    else if (zone == null)
                        hasMissingCell = true;
                    else
                        return false;
                }
            }

            if (!hasMissingCell) return false;
            if (hasShopCell) return true;

            return CountAdjacentShopCells(rect) >= GetRequiredAdjacentShopCells(rect);
        }

        /// <summary>
        /// 统计设施占用矩形周围紧邻的商店格数量，用于区分内部洞和区域外设施。
        /// </summary>
        private int CountAdjacentShopCells(CellRect rect)
        {
            int count = 0;
            for (int z = rect.minZ - 1; z <= rect.maxZ + 1; z++)
            {
                for (int x = rect.minX - 1; x <= rect.maxX + 1; x++)
                {
                    bool insideRect = x >= rect.minX && x <= rect.maxX && z >= rect.minZ && z <= rect.maxZ;
                    if (insideRect) continue;

                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(Map)) continue;
                    if (Map.zoneManager.ZoneAt(cell) == this)
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 根据设施占用面积返回自动修复所需的最小邻接商店格数量。
        /// </summary>
        private static int GetRequiredAdjacentShopCells(CellRect rect)
        {
            int area = rect.Area;
            if (area <= 1) return 4;
            if (area <= 3) return 4;
            return 6;
        }

        /// <summary>
        /// 返回指定岗位当前仍在本地图可工作的员工，负责给 WorkGiver 权限判断和工作分配使用。
        /// </summary>
        public List<Pawn> GetAssignedPawns(string roleDefName)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName))
                return new List<Pawn>();

            NormalizeRoleAssignments();
            return roleAssignments
                .Where(a => a != null && a.roleDefName == roleDefName && a.HasUsablePawnOn(Map))
                .Select(a => a.pawn)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// 返回指定岗位的分配记录，负责让 UI 显示和移除已经离图或引用失效的历史员工。
        /// </summary>
        public List<ShopRoleAssignment> GetAssignedPawnRecords(string roleDefName, bool includeUnavailable)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName))
                return new List<ShopRoleAssignment>();

            NormalizeRoleAssignments();
            return roleAssignments
                .Where(a => a != null && a.roleDefName == roleDefName && (includeUnavailable || a.HasUsablePawnOn(Map)))
                .ToList();
        }

        /// <summary>
        /// 把员工加入指定岗位，并负责启用该岗位关联的原版工作类型。
        /// </summary>
        public void AddAssignedPawn(string roleDefName, Pawn pawn, int maxCount)
        {
            if (string.IsNullOrEmpty(roleDefName) || pawn == null) return;
            if (roleAssignments == null) roleAssignments = new List<ShopRoleAssignment>();
            NormalizeRoleAssignments();

            List<Pawn> current = GetAssignedPawns(roleDefName);
            if (current.Contains(pawn)) return;
            ShopRoleAssignment existing = roleAssignments.FirstOrDefault(a => a != null && a.roleDefName == roleDefName && a.MatchesPawn(pawn));
            if (existing != null)
            {
                existing.CapturePawn(pawn);
                ActivateRoleWorkTypes(roleDefName, pawn);
                return;
            }
            if (maxCount > 0 && current.Count >= maxCount) return;

            ShopRoleAssignment assignment = new ShopRoleAssignment
            {
                roleDefName = roleDefName
            };
            assignment.CapturePawn(pawn);
            roleAssignments.Add(assignment);
            ActivateRoleWorkTypes(roleDefName, pawn);
        }

        /// <summary>
        /// 从指定岗位移除员工。
        /// </summary>
        public void RemoveAssignedPawn(string roleDefName, Pawn pawn)
        {
            if (string.IsNullOrEmpty(roleDefName) || pawn == null || roleAssignments == null) return;
            roleAssignments.RemoveAll(a => a == null || (a.roleDefName == roleDefName && a.MatchesPawn(pawn)));
        }

        /// <summary>
        /// 从指定岗位移除一条员工分配记录，负责处理 Pawn 已离图或引用失效时的清理入口。
        /// </summary>
        public void RemoveAssignedPawnRecord(string roleDefName, ShopRoleAssignment assignment)
        {
            if (string.IsNullOrEmpty(roleDefName) || assignment == null || roleAssignments == null) return;
            roleAssignments.RemoveAll(a => a == null || (a.roleDefName == roleDefName && ReferenceEquals(a, assignment)));
        }

        /// <summary>
        /// 清空指定岗位的员工分配。
        /// </summary>
        public void ClearAssignedPawns(string roleDefName)
        {
            if (string.IsNullOrEmpty(roleDefName) || roleAssignments == null) return;
            roleAssignments.RemoveAll(a => a == null || a.roleDefName == roleDefName);
        }

        /// <summary>
        /// 启用岗位关联的工作类型，负责避免已分配员工因为工作优先级为零而不扫描岗位工作。
        /// </summary>
        private static void ActivateRoleWorkTypes(string roleDefName, Pawn pawn)
        {
            if (pawn?.workSettings == null || string.IsNullOrEmpty(roleDefName)) return;
            ShopStaffRoleDef role = DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail(roleDefName);
            if (role?.workGivers.NullOrEmpty() != false) return;

            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            for (int i = 0; i < role.workGivers.Count; i++)
            {
                WorkTypeDef workType = role.workGivers[i]?.workType;
                if (workType == null || pawn.WorkTypeIsDisabled(workType)) continue;
                if (!pawn.workSettings.WorkIsActive(workType))
                    pawn.workSettings.SetPriority(workType, 3);
            }
        }

        /// <summary>
        /// 激活当前店铺已分配员工的岗位工作类型，负责兼容已有存档中的岗位分配。
        /// </summary>
        private void ActivateAssignedRoleWorkTypes()
        {
            if (roleAssignments.NullOrEmpty()) return;
            NormalizeRoleAssignments();
            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment?.pawn == null || !assignment.HasUsablePawnOn(Map)) continue;
                ActivateRoleWorkTypes(assignment.roleDefName, assignment.pawn);
            }
        }

        /// <summary>
        /// 整理岗位分配记录，负责移除空记录并为旧存档补齐员工显示快照。
        /// </summary>
        private void NormalizeRoleAssignments()
        {
            if (roleAssignments == null)
            {
                roleAssignments = new List<ShopRoleAssignment>();
                return;
            }

            roleAssignments.RemoveAll(a => a == null || string.IsNullOrEmpty(a.roleDefName));
            for (int i = 0; i < roleAssignments.Count; i++)
            {
                ShopRoleAssignment assignment = roleAssignments[i];
                if (assignment?.pawn != null)
                    assignment.CapturePawn(assignment.pawn);
            }
        }

        /// <summary>
        /// 创建商店区域搬迁快照，负责保存区划格、员工分配和营业日程。
        /// </summary>
        public MoveableShopZone CreateMoveableZoneSnapshot()
        {
            return new MoveableShopZone
            {
                zoneId = ID,
                label = label,
                color = color,
                cells = Cells?.ToList() ?? new List<IntVec3>(),
                roleAssignments = roleAssignments?
                    .Where(a => a != null)
                    .Select(a => a.Clone())
                    .ToList() ?? new List<ShopRoleAssignment>(),
                schedule = schedule?.Clone() ?? new ShopScheduleData()
            };
        }

        /// <summary>
        /// 应用商店区域搬迁快照，负责恢复区划显示、员工分配和营业日程。
        /// </summary>
        public void ApplyMoveableZoneSnapshot(MoveableShopZone snapshot)
        {
            if (snapshot == null) return;

            label = string.IsNullOrEmpty(snapshot.label) ? label : snapshot.label;
            color = snapshot.color;
            roleAssignments = snapshot.roleAssignments?
                .Where(a => a != null)
                .Select(a => a.Clone())
                .ToList() ?? new List<ShopRoleAssignment>();
            schedule = snapshot.schedule?.Clone() ?? new ShopScheduleData();
            NormalizeRoleAssignments();
            InvalidateShopRuntimeCache();
            ShopStaffUtility.NotifyShopChanged(this);
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            text += "\n" + SimTranslation.T("RSMF.Zone.Inspect.FacilityStatus", (IsValidShop() ? SimTranslation.T("RSMF.Common.Valid") : GetValidationMessage()).Named("status"));
            text += "\n" + SimTranslation.T("RSMF.Zone.Inspect.OpenStatus", GetOpenStatusMessage().Named("status"));
            return text;
        }

        /// <summary>
        /// 将格子加入商店区域，并允许商店区覆盖普通建筑占用格。
        /// </summary>
        public override void AddCell(IntVec3 c)
        {
            EnsureShopCell(c, true);
        }

        /// <summary>
        /// 确保指定格子属于当前商店区域，负责让划区和设施覆盖修复在已有建筑上保持幂等且不刷红字。
        /// </summary>
        private bool EnsureShopCell(IntVec3 c, bool notifyHomeArea)
        {
            if (Map == null || !c.InBounds(Map))
                return false;

            Zone existingZone = Map.zoneManager.ZoneAt(c);
            if (existingZone != null && existingZone != this)
                return false;

            bool addedToCells = false;
            if (!cells.Contains(c))
            {
                cells.Add(c);
                addedToCells = true;
            }

            bool gridChanged = AddShopZoneGridCell(c);
            if (addedToCells || gridChanged)
                Map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.Zone);
            if (addedToCells && notifyHomeArea)
                AutoHomeAreaMaker.Notify_ZoneCellAdded(c, this);
            if (addedToCells)
                CellsShuffledField?.SetValue(this, false);

            if (addedToCells || gridChanged)
            {
                InvalidateShopRuntimeCache();
                ShopStaffUtility.NotifyShopChanged(this);
            }

            return addedToCells || gridChanged;
        }

        /// <summary>
        /// 将商店区域写入原版区划网格，用于绕过原版不可覆盖建筑检查后的格子登记。
        /// </summary>
        private bool AddShopZoneGridCell(IntVec3 c)
        {
            Zone[] zoneGrid = ZoneGridField?.GetValue(zoneManager) as Zone[];
            if (zoneGrid == null)
            {
                Log.Error("商店区域无法写入区划网格。");
                return false;
            }

            int index = Map.cellIndices.CellToIndex(c);
            if (zoneGrid[index] == this)
                return false;

            zoneGrid[index] = this;
            return true;
        }

        public override void RemoveCell(IntVec3 c)
        {
            base.RemoveCell(c);
            InvalidateShopRuntimeCache();
            ShopStaffUtility.NotifyShopChanged(this);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.Gizmo.ShopManagement.Label"),
                defaultDesc = SimTranslation.T("RSMF.Gizmo.ShopManagement.Desc"),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true),
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_ShopManager(this));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.Gizmo.ShopStaff.Label"),
                defaultDesc = SimTranslation.T("RSMF.Gizmo.ShopStaff.Desc"),
                icon = TexButton.Rename,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_ShopStaffManager(this));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "套餐传输",
                defaultDesc = "导出或导入当前商店区域的套餐 Base64。",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true),
                action = delegate
                {
                    Dialog_ShopManager.OpenShopMenuTransferOptions(this);
                }
            };

            yield return new Command_Toggle
            {
                defaultLabel = SimTranslation.T("RSMF.Gizmo.ToggleZoneVisibility.Label"),
                defaultDesc = SimTranslation.T("RSMF.Gizmo.ToggleZoneVisibility.Desc"),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/ShowZones", false) ?? TexButton.Rename,
                isActive = () => Hidden,
                toggleAction = delegate
                {
                    Hidden = !Hidden;
                }
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref roleAssignments, "roleAssignments", LookMode.Deep);
            Scribe_Deep.Look(ref schedule, "schedule");
            if (roleAssignments == null)
                roleAssignments = new List<ShopRoleAssignment>();
            if (schedule == null)
                schedule = new ShopScheduleData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeRoleAssignments();
                ActivateAssignedRoleWorkTypes();
            }
        }
    }
}
