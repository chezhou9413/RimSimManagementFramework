using RimWorld;
using System;
using System.Collections.Generic;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 声明店铺岗位，负责把岗位显示条件、工作分配器、相关工作和人数上限交给管理窗口与岗位工具使用。
    /// </summary>
    public class ShopStaffRoleDef : Def
    {
        public int maxAssignedPawns = 1;
        public List<ThingDef> requiredThingDefs = new List<ThingDef>();
        public List<WorkGiverDef> workGivers = new List<WorkGiverDef>();
        public List<JobDef> jobDefs = new List<JobDef>();
        public List<Type> requiredThingClasses = new List<Type>();
        public Type workerClass = typeof(ShopStaffRoleWorker);

        [Unsaved] private ShopStaffRoleWorker workerInt;

        /// <summary>
        /// 返回岗位运行时策略对象，负责让外部模组自定义岗位可见性、人数和候选人规则。
        /// </summary>
        public ShopStaffRoleWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    try
                    {
                        Type type = workerClass ?? typeof(ShopStaffRoleWorker);
                        workerInt = (ShopStaffRoleWorker)Activator.CreateInstance(type);
                        workerInt.def = this;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SimShop.Staff] 岗位 {defName} 初始化 Worker 失败: {ex}");
                        workerInt = new ShopStaffRoleWorker { def = this };
                    }
                }

                return workerInt;
            }
        }

        /// <summary>
        /// 返回岗位默认最大分配人数，负责兼容旧 XML 字段。
        /// </summary>
        public int MaxAssignedPawns => Math.Max(0, maxAssignedPawns);
    }

    /// <summary>
    /// 提供店铺岗位的可继承策略，负责控制岗位显示、人数上限、员工资格和工作权限。
    /// </summary>
    public class ShopStaffRoleWorker
    {
        public ShopStaffRoleDef def;

        /// <summary>
        /// 判断岗位是否应在指定商店显示，默认使用岗位 Def 上的建筑类型条件。
        /// </summary>
        public virtual bool CanShow(Zone_Shop zone)
        {
            if (zone?.Map == null || def == null) return false;
            if (def.requiredThingDefs.NullOrEmpty() && def.requiredThingClasses.NullOrEmpty()) return true;

            foreach (IntVec3 cell in zone.Cells)
            {
                List<Thing> things = zone.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed) continue;

                    if (!def.requiredThingDefs.NullOrEmpty() && def.requiredThingDefs.Contains(thing.def))
                        return true;

                    if (!def.requiredThingClasses.NullOrEmpty())
                    {
                        for (int c = 0; c < def.requiredThingClasses.Count; c++)
                        {
                            if (def.requiredThingClasses[c] != null && def.requiredThingClasses[c].IsInstanceOfType(thing))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 返回指定商店中岗位允许分配的人数上限，默认使用 Def 上配置的人数。
        /// </summary>
        public virtual int GetMaxAssignedPawns(Zone_Shop zone)
        {
            return def?.MaxAssignedPawns ?? 0;
        }

        /// <summary>
        /// 判断员工是否满足岗位的额外资格，默认不追加限制。
        /// </summary>
        public virtual bool CanAssignPawn(Zone_Shop zone, Pawn pawn, out string reason)
        {
            reason = "";
            return true;
        }

        /// <summary>
        /// 判断员工是否允许执行岗位绑定的工作分配器，默认不追加限制。
        /// </summary>
        public virtual bool AllowsPawnForWorkGiver(Zone_Shop zone, Pawn pawn, WorkGiverDef workGiverDef)
        {
            return true;
        }
    }
}
