using SimManagementLib.Pojo;
using SimManagementLib.SimDialog;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimZone
{
    public class Zone_Shop : Zone
    {
        private List<ShopRoleAssignment> roleAssignments = new List<ShopRoleAssignment>();

        public Zone_Shop()
        {
        }

        public Zone_Shop(ZoneManager zoneManager) : base("商店区域", zoneManager)
        {
        }

        protected override Color NextZoneColor => new Color(0.9f, 0.7f, 0.2f, 0.3f);

        public bool IsValidShop()
        {
            return ComputeValidShopNow(out _);
        }

        public string GetValidationMessage()
        {
            ComputeValidShopNow(out string message);
            return message;
        }

        private bool ComputeValidShopNow(out string message)
        {
            if (Map == null || Cells == null || Cells.Count == 0)
            {
                message = "区域为空。";
                return false;
            }

            bool hasStorage = false;
            bool hasCashRegister = false;
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
                }
            }

            if (outdoorCount > 0)
            {
                message = $"存在 {outdoorCount} 个室外格，例如 {firstOutdoorCell}。";
                return false;
            }

            if (!hasStorage && !hasCashRegister)
            {
                message = "缺少货柜和收银台。";
                return false;
            }

            if (!hasStorage)
            {
                message = "缺少货柜。";
                return false;
            }

            if (!hasCashRegister)
            {
                message = "缺少收银台。";
                return false;
            }

            message = "商店正在营业中。";
            return true;
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
            text += "\n" + (IsValidShop() ? "商店正在营业中" : ("警告: " + GetValidationMessage()));
            return text;
        }

        public override void AddCell(IntVec3 c)
        {
            base.AddCell(c);
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
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref roleAssignments, "roleAssignments", LookMode.Deep);
            if (roleAssignments == null)
                roleAssignments = new List<ShopRoleAssignment>();
        }
    }
}
