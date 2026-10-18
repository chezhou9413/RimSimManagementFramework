using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //保存经过校验的具体步骤；闭包只由玩家批准后的主线程执行器调用。
    internal sealed class AdvisorPreparedStep
    {
        internal AdvisorAction action;
        internal string resource;
        internal string before;
        internal string preview;
        internal Action apply;
        internal List<AdvisorPreparedStep> children;
        internal bool alreadySatisfied;
    }

    //把有限操作翻译成现有游戏入口，准备方案期间不写入殖民地。
    internal static partial class AdvisorActions
    {
        //解析目标并生成固定审批文本，执行前会重新准备并比较状态。
        internal static AdvisorPreparedStep Prepare(AdvisorAction a)
        {
            if (a == null) throw new InvalidOperationException("操作为空。");
            Map map = AdvisorQueries.Map(AdvisorQueries.Required(a.mapId, "mapId"));
            if (!map.IsPlayerHome) throw new InvalidOperationException("顾问操作仅限玩家基地地图。");
            AdvisorPreparedStep step;
            switch (a.kind)
            {
                case "schedule": step = Schedule(a, map); break;
                case "rename_shop": step = Rename(a, map); break;
                case "assign": step = Assign(a, map); break;
                case "work_priority": step = Priority(a, map); break;
                case "work_mode": step = WorkMode(a); break;
                case "shelf": case "shelf_category": step = Shelf(a, map); break;
                case "unique_stock": case "unique_price": step = UniqueShelf(a, map); break;
                case "create_shop": step = CreateShop(a, map); break;
                case "blueprint": step = Blueprint(a, map); break;
                case "build_rect": step = BuildRectangle(a, map); break;
                case "order": step = NativeOrder(a, map); break;
                case "order_target": case "order_cell": step = NativeOrderUnit(a, map); break;
                default: throw new InvalidOperationException("不支持的操作：" + a.kind);
            }
            step.action = a;
            step.preview = "地图 " + AdvisorQueries.Q(map.Parent.Label) + " (#" + map.uniqueID + ")\n" + step.preview;
            return step;
        }

        //更改每日营业日程，不调整游戏世界时间。
        private static AdvisorPreparedStep Schedule(AdvisorAction a, Map map)
        {
            Zone_Shop shop = AdvisorQueries.Shop(map.uniqueID, AdvisorQueries.Required(a.shopId, "shopId"));
            if (!a.enabled.HasValue || a.hours == null || a.hours.Length != 24 || a.hours.Any(c => c != '0' && c != '1'))
                throw new InvalidOperationException("营业日程需要 enabled 和 24 位 0/1 hours。");
            string before = shop.GetSchedule().manualOpen + "/" + shop.GetSchedule().useSchedule + "/" + AdvisorQueries.Hours(shop);
            var next = shop.GetSchedule().Clone();
            next.manualOpen = a.enabled.Value;
            next.useSchedule = true;
            for (int i = 0; i < 24; i++) next.openHours[i] = a.hours[i] == '1';
            return new AdvisorPreparedStep { resource = "schedule:" + shop.ID, before = before,
                preview = "营业日程：" + AdvisorQueries.Q(shop.label) + " (#" + shop.ID + ")\n"
                    + shop.GetSchedule().GetStatusText(map) + " / " + shop.GetSchedule().GetScheduleSummary()
                    + " → " + (next.manualOpen ? "营业" : "停业") + " / " + next.GetScheduleSummary(),
                apply = () => shop.ApplySchedule(next) };
        }

        //只修改店铺显示名称。
        private static AdvisorPreparedStep Rename(AdvisorAction a, Map map)
        {
            Zone_Shop shop = AdvisorQueries.Shop(map.uniqueID, AdvisorQueries.Required(a.shopId, "shopId"));
            if (string.IsNullOrWhiteSpace(a.name) || a.name.Length > 80) throw new InvalidOperationException("店铺名称需要 1~80 字符。");
            return new AdvisorPreparedStep { resource = "name:" + shop.ID, before = shop.label,
                preview = "店铺 #" + shop.ID + " 名称：" + AdvisorQueries.Q(shop.label) + " → " + AdvisorQueries.Q(a.name), apply = () => shop.label = a.name };
        }

        //按框架岗位资格和上限安排员工，并明确展示关联工作优先级变化。
        private static AdvisorPreparedStep Assign(AdvisorAction a, Map map)
        {
            Zone_Shop shop = AdvisorQueries.Shop(map.uniqueID, AdvisorQueries.Required(a.shopId, "shopId"));
            Pawn pawn = StaffPawn(map, a.pawnId);
            var role = DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail(a.role ?? "");
            if (role == null || !ShopStaffUtility.RoleMatchesShop(shop, role) || !a.enabled.HasValue)
                throw new InvalidOperationException("岗位 role=" + a.role + " 不可用，或缺少 enabled。该店真实岗位 ID："
                    + string.Join(",", ShopStaffUtility.GetVisibleRoles(shop).Select(r => r.defName)) + "；请查询 staff 的实时资格再分配。");
            var current = shop.GetAssignedPawns(role.defName);
            int max = ShopStaffUtility.GetMaxAssignedPawns(shop, role);
            if (a.enabled.Value)
            {
                var eligibility = ShopStaffUtility.EvaluateEligibility(shop, pawn, role);
                if (!eligibility.Eligible) throw new InvalidOperationException(eligibility.Reason);
                if (!current.Contains(pawn) && max > 0 && current.Count >= max) throw new InvalidOperationException("岗位已满。");
            }
            //其他员工加入同一岗位不使此人的方案失效；人数上限仍在每次 Prepare 中检查。
            string before = "assigned=" + current.Contains(pawn) + "/" + AdvisorQueries.WorkPriorities(pawn);
            string changes = "";
            if (a.enabled.Value && !current.Contains(pawn) && pawn.workSettings != null)
            {
                changes = string.Join("；", role.workGivers.Where(w => w.workType != null && !pawn.WorkTypeIsDisabled(w.workType))
                    .Select(w => w.workType).Distinct().Where(w => pawn.workSettings.GetPriority(w) == 0).Select(w => w.labelShort + " 0 → 3"));
                if (role.workGivers.Any(w => w.defName == "RestockMegaStorage")) changes += "；补货 → 1，普通搬运 → 4（框架岗位规则）";
            }
            return new AdvisorPreparedStep { resource = "pawn:" + pawn.thingIDNumber, before = before,
                preview = "岗位：" + AdvisorQueries.Q(shop.label) + " / " + role.LabelCap + " / " + pawn.LabelShortCap + " (#" + pawn.thingIDNumber
                    + ")\n已分配=" + current.Contains(pawn) + " → " + a.enabled.Value + (changes.Length > 0 ? "\n关联工作：" + changes : ""),
                apply = () => { if (a.enabled.Value) shop.AddAssignedPawn(role.defName, pawn, max); else shop.RemoveAssignedPawn(role.defName, pawn); } };
        }

        //只更改指定员工的单项原版工作优先级。
        private static AdvisorPreparedStep Priority(AdvisorAction a, Map map)
        {
            Pawn pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.thingIDNumber == AdvisorQueries.Required(a.pawnId, "pawnId"))
                ?? throw new InvalidOperationException("殖民者不在此地图。");
            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(a.role ?? "");
            int priority = AdvisorQueries.Required(a.priority, "priority");
            if (work == null || pawn.workSettings == null || pawn.WorkTypeIsDisabled(work) || priority < 0 || priority > 4)
                throw new InvalidOperationException("工作类型不可用或优先级不在 0~4。");
            if (!Find.PlaySettings.useWorkPriorities && priority != 0 && priority != 3)
                throw new InvalidOperationException("尚未启用手动工作优先级。请先单独提交 work_mode enabled=true，再设置 1/2/4 级工作。");
            int before = pawn.workSettings.GetPriority(work);
            return new AdvisorPreparedStep { resource = "pawn:" + pawn.thingIDNumber, before = before.ToString(),
                preview = "工作优先级：" + pawn.LabelShortCap + " (#" + pawn.thingIDNumber + ") / " + work.labelShort + "：" + before + " → " + priority,
                apply = () => pawn.workSettings.SetPriority(work, priority) };
        }

        //通过原版入口通知所有殖民者，避免只改变界面开关而继续使用旧的工作排序。
        private static AdvisorPreparedStep WorkMode(AdvisorAction a)
        {
            if (!a.enabled.HasValue) throw new InvalidOperationException("work_mode 缺少 enabled。");
            return new AdvisorPreparedStep { resource = "work-mode", before = Find.PlaySettings.useWorkPriorities.ToString(),
                preview = "全局手动工作优先级：" + Find.PlaySettings.useWorkPriorities + " → " + a.enabled.Value,
                apply = () => { Find.PlaySettings.useWorkPriorities = a.enabled.Value;
                    foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                        if (pawn.Faction == Faction.OfPlayer) pawn.workSettings?.Notify_UseWorkPrioritiesChanged(); } };
        }

        //只允许操作当前地图上框架认定可分配的员工。
        private static Pawn StaffPawn(Map map, int? id) => ShopStaffUtility.GetAssignablePawns(map)
            .FirstOrDefault(p => p.thingIDNumber == AdvisorQueries.Required(id, "pawnId")) ?? throw new InvalidOperationException("员工不存在或不可分配。");

        //定位玩家物品；普通无阵营物品仅能作为专业货源使用。
        private static Thing Target(Map map, int? id, bool requirePlayer = true)
        {
            Thing thing = map.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == AdvisorQueries.Required(id, "targetId"));
            if (thing == null || !thing.Spawned || thing.Destroyed || (requirePlayer && thing.Faction != Faction.OfPlayer))
                throw new InvalidOperationException("目标不存在、已离图或不属于玩家。");
            return thing;
        }
    }
}
