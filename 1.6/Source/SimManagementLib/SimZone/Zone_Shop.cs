using HarmonyLib;
using RimWorld;
using SimManagementLib.Pojo;
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
    public class Zone_Shop : Zone
    {
        private static readonly FieldInfo ZoneGridField = AccessTools.Field(typeof(ZoneManager), "zoneGrid");
        private static readonly FieldInfo CellsShuffledField = AccessTools.Field(typeof(Zone), "cellsShuffled");

        private List<ShopRoleAssignment> roleAssignments = new List<ShopRoleAssignment>();
        private ShopScheduleData schedule = new ShopScheduleData();

        public Zone_Shop()
        {
        }

        public Zone_Shop(ZoneManager zoneManager) : base("商店区域", zoneManager)
        {
        }

        protected override Color NextZoneColor => new Color(0.9f, 0.7f, 0.2f, 0.3f);

        /// <summary>
        /// 判断商店区域当前是否满足营业条件。
        /// </summary>
        public bool IsValidShop()
        {
            return ComputeValidShopNow(out _);
        }

        /// <summary>
        /// 判断商店区域当前是否允许营业，要求设施有效且营业开关和日程均允许。
        /// </summary>
        public bool IsOpenNow()
        {
            if (!IsValidShop()) return false;
            return GetSchedule().IsOpenNow(Map);
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
                return "商店已手动停业。";
            if (data.useSchedule && !data.IsOpenNow(Map))
                return $"当前不在营业时间内：{data.GetScheduleSummary()}。";

            return "商店正在营业中。";
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
        }

        /// <summary>
        /// 返回商店区域当前营业条件的说明文本。
        /// </summary>
        public string GetValidationMessage()
        {
            ComputeValidShopNow(out string message);
            return message;
        }

        /// <summary>
        /// 实时扫描区域内设施和室内状态，计算商店是否可以营业。
        /// </summary>
        private bool ComputeValidShopNow(out string message)
        {
            if (Map == null || Cells == null || Cells.Count == 0)
            {
                message = "区域为空。";
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
                message = $"存在 {outdoorCount} 个室外格，例如 {firstOutdoorCell}。";
                return false;
            }

            if (!hasStorage && !hasServiceProvider && !hasCashRegister)
            {
                message = "缺少货柜、服务建筑和收银台。";
                return false;
            }

            if (!hasStorage && !hasServiceProvider)
            {
                message = "缺少货柜或服务建筑。";
                return false;
            }

            if (!hasCashRegister)
            {
                message = "缺少收银台。";
                return false;
            }

            message = "商店设施有效。";
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

        public List<Pawn> GetAssignedPawns(string roleDefName)
        {
            if (roleAssignments.NullOrEmpty() || string.IsNullOrEmpty(roleDefName))
                return new List<Pawn>();

            return roleAssignments
                .Where(a => a != null && a.roleDefName == roleDefName && a.pawn != null && !a.pawn.Destroyed && !a.pawn.Dead)
                .Select(a => a.pawn)
                .Distinct()
                .ToList();
        }

        public void AddAssignedPawn(string roleDefName, Pawn pawn, int maxCount)
        {
            if (string.IsNullOrEmpty(roleDefName) || pawn == null) return;
            if (roleAssignments == null) roleAssignments = new List<ShopRoleAssignment>();

            List<Pawn> current = GetAssignedPawns(roleDefName);
            if (current.Contains(pawn)) return;
            if (maxCount > 0 && current.Count >= maxCount) return;

            roleAssignments.Add(new ShopRoleAssignment
            {
                roleDefName = roleDefName,
                pawn = pawn
            });
        }

        public void RemoveAssignedPawn(string roleDefName, Pawn pawn)
        {
            if (string.IsNullOrEmpty(roleDefName) || pawn == null || roleAssignments == null) return;
            roleAssignments.RemoveAll(a => a == null || (a.roleDefName == roleDefName && a.pawn == pawn));
        }

        public void ClearAssignedPawns(string roleDefName)
        {
            if (string.IsNullOrEmpty(roleDefName) || roleAssignments == null) return;
            roleAssignments.RemoveAll(a => a == null || a.roleDefName == roleDefName);
        }

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
                    .Select(a => new ShopRoleAssignment
                    {
                        roleDefName = a.roleDefName,
                        pawn = a.pawn
                    })
                    .ToList() ?? new List<ShopRoleAssignment>()
            };
        }

        public void ApplyMoveableZoneSnapshot(MoveableShopZone snapshot)
        {
            if (snapshot == null) return;

            label = string.IsNullOrEmpty(snapshot.label) ? label : snapshot.label;
            color = snapshot.color;
            roleAssignments = snapshot.roleAssignments?
                .Where(a => a != null)
                .Select(a => new ShopRoleAssignment
                {
                    roleDefName = a.roleDefName,
                    pawn = a.pawn
                })
                .ToList() ?? new List<ShopRoleAssignment>();
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            text += "\n设施状态: " + (IsValidShop() ? "有效" : GetValidationMessage());
            text += "\n营业状态: " + GetOpenStatusMessage();
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
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Action
            {
                defaultLabel = "商店管理",
                defaultDesc = "查看并管理该商店区域内所有货柜的在售商品与库存状态。",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true),
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_ShopManager(this));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "店员配置",
                defaultDesc = "打开该商店的店员岗位配置面板。",
                icon = TexButton.Rename,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_ShopStaffManager(this));
                }
            };

            yield return new Command_Toggle
            {
                defaultLabel = "隐藏区域显示",
                defaultDesc = "隐藏或显示商店区域的地面颜色覆盖。只影响区域绘制，不影响商店营业、补货、收银和顾客访问。",
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
        }
    }
}
