using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimWorkGiver;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace SimManagementLib.Debug
{
    //补货诊断报告构建器，职责是汇总当前地图货柜、货源、员工和岗位限制状态。
    public static class RestockDebugReportBuilder
    {
        private const int MaxStorageCount = 80;
        private const int MaxGoodsPerStorage = 24;
        private const int MaxSupplyPerDef = 12;
        private const int MaxPawnCount = 40;

        //构建当前地图补货诊断文本，职责是给日志和剪贴板提供完整排查材料。
        public static string Build(Map map)
        {
            StringBuilder sb = new StringBuilder();
            AppendHeader(sb, map);
            if (map == null)
            {
                sb.AppendLine("当前没有地图。");
                return sb.ToString();
            }

            List<Building_SimContainer> storages = GetStorages(map);
            List<Pawn> pawns = GetRelevantPawns(map);
            AppendSummary(sb, map, storages, pawns);
            AppendQueueDiagnostics(sb, map);
            AppendPawnDiagnostics(sb, pawns);
            AppendStorageDiagnostics(sb, storages, pawns);
            return sb.ToString().TrimEnd();
        }

        //写入报告头部，职责是记录生成时刻和地图上下文。
        private static void AppendHeader(StringBuilder sb, Map map)
        {
            sb.AppendLine("[RSMF 补货诊断]");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Tick: " + (Find.TickManager?.TicksGame ?? 0));
            sb.AppendLine("Map: " + (map != null ? map.uniqueID.ToString() : "无"));
            sb.AppendLine();
        }

        //写入总体统计，职责是快速判断地图上是否存在缺货货柜和可用员工。
        private static void AppendSummary(StringBuilder sb, Map map, List<Building_SimContainer> storages, List<Pawn> pawns)
        {
            int storageNeedingRestock = 0;
            int totalMissing = 0;
            int restockEnabledPawns = 0;
            int idleRestockPawns = 0;
            int activeRestockJobs = 0;
            WorkGiverDef restockGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
            for (int i = 0; i < storages.Count; i++)
            {
                int missing = CountStorageMissing(storages[i]);
                if (missing <= 0)
                    continue;

                storageNeedingRestock++;
                totalMissing += missing;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (CanUseWorkGiver(pawn, restockGiver))
                {
                    restockEnabledPawns++;
                    if (IsIdleForRestock(pawn))
                        idleRestockPawns++;
                }

                if (pawn?.CurJobDef?.defName == "DepositToMegaStorage")
                    activeRestockJobs++;
            }

            sb.AppendLine("[总览]");
            sb.AppendLine("殖民地货柜数: " + storages.Count);
            sb.AppendLine("需要补货货柜数: " + storageNeedingRestock);
            sb.AppendLine("总缺口数量: " + totalMissing);
            sb.AppendLine("候选员工数: " + pawns.Count);
            sb.AppendLine("可执行补货员工数: " + restockEnabledPawns);
            sb.AppendLine("空闲补货员工数: " + idleRestockPawns);
            sb.AppendLine("当前补货 Job 数: " + activeRestockJobs);
            sb.AppendLine("建筑总数: " + (map?.listerBuildings?.allBuildingsColonist?.Count ?? 0));
            sb.AppendLine();
        }

        //写入补货队列诊断，职责是展示事件驱动补货任务的 dirty、ready 和 blocked 状态。
        private static void AppendQueueDiagnostics(StringBuilder sb, Map map)
        {
            RestockQueueDebugSnapshot snapshot = map?.GetComponent<MapComponent_RestockTaskQueue>()?.CreateDebugSnapshot();
            sb.AppendLine("[补货队列]");
            if (snapshot == null)
            {
                sb.AppendLine("没有地图补货队列组件。");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("dirty=" + snapshot.DirtyCount
                + " ready=" + snapshot.ReadyCount
                + " blocked=" + snapshot.BlockedCount
                + " lastProcessTick=" + snapshot.LastProcessTick
                + " lastRebuildTick=" + snapshot.LastRebuildTick
                + " lastReason=" + snapshot.LastReason);
            AppendQueueTaskSamples(sb, "ready", snapshot.ReadyTasks);
            AppendQueueTaskSamples(sb, "blocked", snapshot.BlockedTasks);
            sb.AppendLine();
        }

        //写入队列任务样本，职责是限制日志长度同时保留排查关键字段。
        private static void AppendQueueTaskSamples(StringBuilder sb, string label, List<RestockTask> tasks)
        {
            if (tasks == null || tasks.Count <= 0)
                return;

            int count = Math.Min(tasks.Count, 20);
            for (int i = 0; i < count; i++)
            {
                RestockTask task = tasks[i];
                sb.AppendLine("  " + label
                    + " storage=" + task.StorageId
                    + " def=" + (task.ThingDef?.defName ?? "null")
                    + " need=" + task.NeededCount
                    + " supply=" + task.SupplyId
                    + " retry=" + task.RetryTick
                    + " reason=" + task.StateReason);
            }

            if (tasks.Count > count)
                sb.AppendLine("  " + label + " 剩余省略: " + (tasks.Count - count));
        }

        //写入员工诊断，职责是检查员工是否启用了补货相关工作并记录当前 Job。
        private static void AppendPawnDiagnostics(StringBuilder sb, List<Pawn> pawns)
        {
            WorkTypeDef restocking = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Restocking");
            WorkTypeDef hauling = WorkTypeDefOf.Hauling;
            WorkGiverDef restockGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");

            sb.AppendLine("[员工]");
            if (pawns.Count <= 0)
            {
                sb.AppendLine("没有可工作的殖民者或玩家机械体。");
                sb.AppendLine();
                return;
            }

            for (int i = 0; i < pawns.Count && i < MaxPawnCount; i++)
            {
                Pawn pawn = pawns[i];
                sb.AppendLine("Pawn: " + DescribePawn(pawn));
                sb.AppendLine("  Restocking: " + DescribeWorkType(pawn, restocking));
                sb.AppendLine("  Hauling: " + DescribeWorkType(pawn, hauling));
                sb.AppendLine("  可执行补货 WorkGiver: " + CanUseWorkGiver(pawn, restockGiver));
            }

            if (pawns.Count > MaxPawnCount)
                sb.AppendLine("剩余员工省略: " + (pawns.Count - MaxPawnCount));
            sb.AppendLine();
        }

        //写入货柜诊断，职责是逐个列出缺口、预约、货源和员工可达性。
        private static void AppendStorageDiagnostics(StringBuilder sb, List<Building_SimContainer> storages, List<Pawn> pawns)
        {
            sb.AppendLine("[货柜]");
            if (storages.Count <= 0)
            {
                sb.AppendLine("当前地图没有 Building_SimContainer。");
                return;
            }

            List<Building_SimContainer> ordered = storages
                .OrderByDescending(CountStorageMissing)
                .ThenBy(storage => storage.thingIDNumber)
                .ToList();

            int count = Math.Min(ordered.Count, MaxStorageCount);
            for (int i = 0; i < count; i++)
                AppendSingleStorage(sb, ordered[i], pawns, i + 1);

            if (ordered.Count > MaxStorageCount)
                sb.AppendLine("剩余货柜省略: " + (ordered.Count - MaxStorageCount));
        }

        //写入单个货柜诊断，职责是定位该货柜为什么需要或无法补货。
        private static void AppendSingleStorage(StringBuilder sb, Building_SimContainer storage, List<Pawn> pawns, int index)
        {
            storage.ReconcilePendingReservations();
            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            bool vending = VendingMachineUtility.IsVendingMachine(storage);
            int totalMissing = CountStorageMissing(storage);

            sb.AppendLine("货柜 #" + index + ": " + storage.LabelShortCap + " id=" + storage.thingIDNumber);
            sb.AppendLine("  位置: " + storage.Position + " def=" + storage.def?.defName + " faction=" + (storage.Faction?.Name ?? "无"));
            sb.AppendLine("  商店: " + (shop?.label ?? "无") + " shopId=" + (shop?.ID.ToString() ?? "无") + " openForWork=" + ShopStaffUtility.IsShopOpenForWork(shop) + " vending=" + vending);
            sb.AppendLine("  总库存: " + storage.CountTotalStored() + "/" + storage.MaxTotalCapacity + " pendingIn=" + storage.CountTotalPendingIn(true));
            sb.AppendLine("  总缺口: " + totalMissing);
            if (!string.IsNullOrEmpty(storage.LastPendingReservationDebug))
                sb.AppendLine("  最近预约校正: " + storage.LastPendingReservationDebug);

            AppendStoragePawnAccess(sb, storage, shop, pawns);
            AppendStorageGoods(sb, storage, pawns);
            sb.AppendLine();
        }

        //写入员工到货柜的访问状态，职责是区分岗位限制、可达性和当前工作阻塞。
        private static void AppendStoragePawnAccess(StringBuilder sb, Building_SimContainer storage, Zone_Shop shop, List<Pawn> pawns)
        {
            WorkGiverDef restockGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
            int usable = 0;
            int reachable = 0;
            List<string> samples = new List<string>();

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                bool allowed = VendingMachineUtility.IsVendingMachine(storage)
                    || restockGiver == null
                    || ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, restockGiver);
                bool canReach = pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly);
                if (allowed)
                    usable++;
                if (allowed && canReach)
                    reachable++;
                if (samples.Count < 8)
                    samples.Add(pawn.LabelShortCap + ": allowed=" + allowed + " reach=" + canReach + " job=" + (pawn.CurJobDef?.defName ?? "无"));
            }

            sb.AppendLine("  员工访问: allowed=" + usable + "/" + pawns.Count + " reachable=" + reachable + "/" + pawns.Count);
            for (int i = 0; i < samples.Count; i++)
                sb.AppendLine("    " + samples[i]);
        }

        //写入货柜商品诊断，职责是列出每个配置商品的目标、库存、阈值、缺口和货源。
        private static void AppendStorageGoods(StringBuilder sb, Building_SimContainer storage, List<Pawn> pawns)
        {
            List<ThingDef> defs = storage.ActiveDefs.ToList();
            sb.AppendLine("  商品配置数: " + defs.Count);
            int count = Math.Min(defs.Count, MaxGoodsPerStorage);
            for (int i = 0; i < count; i++)
            {
                ThingDef thingDef = defs[i];
                int stored = storage.CountStored(thingDef);
                int pending = storage.CountPending(thingDef, true);
                int target = storage.GetTargetCount(thingDef);
                int threshold = storage.GetRestockThreshold(thingDef);
                int needed = storage.CountNeeded(thingDef);
                sb.AppendLine("    " + thingDef.defName + ": stored=" + stored + " pending=" + pending + " target=" + target + " threshold=" + threshold + " needed=" + needed);
                if (needed > 0)
                    AppendSupplyDiagnostics(sb, storage, thingDef, pawns);
            }

            if (defs.Count > MaxGoodsPerStorage)
                sb.AppendLine("    剩余商品省略: " + (defs.Count - MaxGoodsPerStorage));
        }

        //写入指定商品的货源诊断，职责是检查地图货源是否存在、被禁用、被预约或不可达。
        private static void AppendSupplyDiagnostics(StringBuilder sb, Building_SimContainer storage, ThingDef thingDef, List<Pawn> pawns)
        {
            Map map = storage.Map;
            List<Thing> supplies = map?.listerThings?.ThingsOfDef(thingDef);
            int totalStacks = supplies?.Count ?? 0;
            int validStacks = 0;
            int reservedOrUnreachable = 0;
            List<string> samples = new List<string>();

            if (supplies != null)
            {
                for (int i = 0; i < supplies.Count; i++)
                {
                    Thing thing = supplies[i];
                    if (thing == null || thing.Destroyed || !thing.Spawned || thing.stackCount <= 0)
                        continue;
                    if (IsInsideSimContainer(thing))
                        continue;

                    Pawn bestPawn = FindFirstPawnThatCanUseSupply(pawns, thing);
                    bool forbiddenForAll = IsForbiddenForAll(pawns, thing);
                    if (bestPawn != null)
                    {
                        validStacks++;
                    }
                    else
                    {
                        reservedOrUnreachable++;
                    }

                    if (samples.Count < MaxSupplyPerDef)
                    {
                        samples.Add("货源 id=" + thing.thingIDNumber
                            + " pos=" + thing.Position
                            + " stack=" + thing.stackCount
                            + " forbiddenAll=" + forbiddenForAll
                            + " usablePawn=" + (bestPawn?.LabelShortCap ?? "无"));
                    }
                }
            }

            sb.AppendLine("      货源: stacks=" + totalStacks + " validForSomePawn=" + validStacks + " blockedOrUnreachable=" + reservedOrUnreachable);
            for (int i = 0; i < samples.Count; i++)
                sb.AppendLine("        " + samples[i]);
        }

        //获取当前地图所有商店货柜，职责是统一过滤无效建筑。
        private static List<Building_SimContainer> GetStorages(Map map)
        {
            List<Building_SimContainer> result = new List<Building_SimContainer>();
            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null)
                return result;

            for (int i = 0; i < buildings.Count; i++)
            {
                Building_SimContainer storage = buildings[i] as Building_SimContainer;
                if (storage != null && !storage.Destroyed && storage.Spawned)
                    result.Add(storage);
            }

            return result;
        }

        //获取当前地图可能参与补货的员工，职责是覆盖殖民者和玩家控制机械体。
        private static List<Pawn> GetRelevantPawns(Map map)
        {
            if (map?.mapPawns == null)
                return new List<Pawn>();

            return map.mapPawns.FreeColonists
                .Concat(map.mapPawns.SpawnedColonyMechs.Where(ShopStaffUtility.IsAssignableMechanicalStaff))
                .Where(pawn => pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Destroyed)
                .Distinct()
                .OrderBy(pawn => pawn.LabelShortCap)
                .ToList();
        }

        //统计货柜总补货缺口，职责是给总览和排序使用。
        private static int CountStorageMissing(Building_SimContainer storage)
        {
            if (storage == null)
                return 0;

            int total = 0;
            foreach (ThingDef thingDef in storage.ActiveDefs)
                total += storage.CountNeeded(thingDef);
            return total;
        }

        //描述 Pawn 当前状态，职责是给员工列表提供紧凑信息。
        private static string DescribePawn(Pawn pawn)
        {
            return pawn.LabelShortCap
                + " id=" + pawn.thingIDNumber
                + " pos=" + pawn.Position
                + " faction=" + (pawn.Faction?.Name ?? "无")
                + " job=" + (pawn.CurJobDef?.defName ?? "无")
                + " downed=" + pawn.Downed
                + " mental=" + pawn.InMentalState;
        }

        //描述指定工作类型在 Pawn 上的启用状态和优先级。
        private static string DescribeWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (workType == null)
                return "无 Def";
            if (pawn.WorkTypeIsDisabled(workType))
                return "disabled";
            if (pawn.workSettings == null)
                return "无 workSettings";
            bool active = pawn.workSettings.WorkIsActive(workType);
            int priority = active ? pawn.workSettings.GetPriority(workType) : 0;
            return "active=" + active + " priority=" + priority;
        }

        //判断 Pawn 是否可以使用指定 WorkGiver，职责是检查工作类型开关和能力限制。
        private static bool CanUseWorkGiver(Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (pawn == null || workGiverDef == null)
                return false;
            if (workGiverDef.workType != null && pawn.WorkTypeIsDisabled(workGiverDef.workType))
                return false;
            if (pawn.workSettings != null && workGiverDef.workType != null && !pawn.workSettings.WorkIsActive(workGiverDef.workType))
                return false;
            if (workGiverDef.requiredCapacities != null)
            {
                for (int i = 0; i < workGiverDef.requiredCapacities.Count; i++)
                {
                    PawnCapacityDef capacity = workGiverDef.requiredCapacities[i];
                    if (capacity != null && pawn.health?.capacities?.CapableOf(capacity) == false)
                        return false;
                }
            }

            return true;
        }

        //判断 Pawn 是否处于补货主动派工可接管的空闲状态，职责是区分队列无任务和员工都在忙。
        private static bool IsIdleForRestock(Pawn pawn)
        {
            if (pawn?.CurJob == null)
                return true;

            string defName = pawn.CurJobDef?.defName;
            return defName == "Wait" || defName == "Wait_Wander" || defName == "Wait_MaintainPosture";
        }

        //查找第一个能使用货源的员工，职责是诊断货源是否实际可被补货任务拿起。
        private static Pawn FindFirstPawnThatCanUseSupply(List<Pawn> pawns, Thing thing)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (thing.IsForbidden(pawn))
                    continue;
                if (!pawn.CanReserve(thing))
                    continue;
                if (!pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
                    continue;
                return pawn;
            }

            return null;
        }

        //判断货源是否对所有员工禁用，职责是区分禁用和预约寻路问题。
        private static bool IsForbiddenForAll(List<Pawn> pawns, Thing thing)
        {
            if (pawns.Count <= 0)
                return false;

            for (int i = 0; i < pawns.Count; i++)
            {
                if (!thing.IsForbidden(pawns[i]))
                    return false;
            }

            return true;
        }

        //判断物品是否已经在本框架货柜内部，职责是排除虚拟库存中的非地图货源。
        private static bool IsInsideSimContainer(Thing thing)
        {
            return thing?.GetSlotGroup()?.parent is Building_SimContainer;
        }
    }
}
