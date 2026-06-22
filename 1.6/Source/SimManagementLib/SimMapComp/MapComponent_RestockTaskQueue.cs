using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimWorkGiver;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimMapComp
{
    //地图补货任务队列，职责是把货柜缺货事件分帧转换成可派发的补货任务。
    public class MapComponent_RestockTaskQueue : MapComponent
    {
        private const int DirtyProcessPerTick = 4;
        private const int SupplyDefProcessPerTick = 4;
        private const int ReconcileIntervalTicks = 397;
        private const int BlockedRetryTicks = 121;
        private const int DispatchedRetryTicks = 31;
        private const int MissingSupplyRetryTicks = 241;
        private const int IdleDispatchIntervalTicks = 37;
        private const int QueueRebuildVersion = 1;
        private static readonly HashSet<string> IdleJobDefNames = new HashSet<string>
        {
            "Wait",
            "Wait_Wander",
            "Wait_MaintainPosture",
            "GotoWander",
            "HaulToCell",
            "Refuel"
        };
        private readonly Queue<RestockTaskKey> dirtyQueue = new Queue<RestockTaskKey>();
        private readonly HashSet<RestockTaskKey> dirtySet = new HashSet<RestockTaskKey>();
        private readonly Dictionary<RestockTaskKey, RestockTask> readyTasks = new Dictionary<RestockTaskKey, RestockTask>();
        private readonly Dictionary<RestockTaskKey, RestockTask> blockedTasks = new Dictionary<RestockTaskKey, RestockTask>();
        private int reconcileCursor;
        private int loadedQueueVersion;
        private int lastProcessTick = -1;
        private int lastRebuildTick = -1;
        private string lastProcessReason = "";

        //创建地图补货队列组件，职责是绑定当前地图。
        public MapComponent_RestockTaskQueue(Map map) : base(map)
        {
        }

        //读写队列迁移标记，职责是在旧存档读入后暴力重建补货运行态。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadedQueueVersion, "rsmfRestockQueueVersion", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && loadedQueueVersion < QueueRebuildVersion)
                ResetAndRebuildAll("旧存档补货队列迁移");
            if (Scribe.mode == LoadSaveMode.Saving)
                loadedQueueVersion = QueueRebuildVersion;
        }

        //推进补货队列，职责是分帧处理脏货柜并定期兜底巡检。
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (loadedQueueVersion < QueueRebuildVersion)
                ResetAndRebuildAll("补货队列版本迁移");

            ProcessDirtyQueue(now);
            RetryBlockedTasks(now);
            TryDispatchIdleRestockPawns(now);
            if (now % ReconcileIntervalTicks == map.uniqueID % ReconcileIntervalTicks)
                ReconcileStorageSlice(now);
        }

        //标记整个货柜需要重新计算，职责是把外部库存或配置变化转成队列输入。
        public void MarkStorageDirty(Building_SimContainer storage, string reason)
        {
            if (!IsValidStorage(storage))
                return;

            storage.ReconcilePendingReservationsForWorkScan();
            foreach (ThingDef thingDef in storage.ActiveDefs)
            {
                RestockTaskKey key = new RestockTaskKey(storage.thingIDNumber, thingDef);
                if (storage.CountNeededForWorkScan(thingDef) > 0)
                {
                    MarkDirty(storage, thingDef, reason);
                    continue;
                }

                readyTasks.Remove(key);
                blockedTasks.Remove(key);
                dirtySet.Remove(key);
            }
        }

        //标记指定商品需要重新计算，职责是精确刷新一个货柜的单项补货状态。
        public void MarkDirty(Building_SimContainer storage, ThingDef thingDef, string reason)
        {
            if (!IsValidStorage(storage) || thingDef == null)
                return;

            RestockTaskKey key = new RestockTaskKey(storage.thingIDNumber, thingDef);
            readyTasks.Remove(key);
            blockedTasks.Remove(key);
            if (dirtySet.Add(key))
                dirtyQueue.Enqueue(key);
            lastProcessReason = reason ?? "";
        }

        //清空并重建当前地图所有补货状态，职责是供调试和旧存档迁移暴力恢复。
        public int ResetAndRebuildAll(string reason)
        {
            dirtyQueue.Clear();
            dirtySet.Clear();
            readyTasks.Clear();
            blockedTasks.Clear();
            reconcileCursor = 0;
            loadedQueueVersion = QueueRebuildVersion;
            lastRebuildTick = Find.TickManager?.TicksGame ?? 0;
            lastProcessReason = reason ?? "";
            WorkGiver_RestockMegaStorage.ClearRestockCandidateCaches();
            RestockSupplySearchStateCache.ClearSupplySearchStates();
            RestockWorkTickBudget.ClearBudgets();
            WorkGiverThingQueryCache.Clear();

            int count = 0;
            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null)
                return 0;

            for (int i = 0; i < buildings.Count; i++)
            {
                Building_SimContainer storage = buildings[i] as Building_SimContainer;
                if (!IsValidStorage(storage))
                    continue;

                storage.ReconcilePendingReservations();
                MarkStorageDirty(storage, reason);
                count++;
            }

            return count;
        }

        //尝试为指定小人生成补货 Job，职责是让 WorkGiver 只桥接队列任务。
        public Job TryMakeJobForPawn(Pawn pawn)
        {
            if (pawn?.Map != map)
                return null;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (readyTasks.Count <= 0)
                ProcessDirtyQueue(now);

            List<RestockTaskKey> keys = readyTasks.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                RestockTaskKey key = keys[i];
                if (!readyTasks.TryGetValue(key, out RestockTask task))
                    continue;

                Job job = TryMakeJobFromTask(pawn, task, now);
                if (job != null)
                    return job;
            }

            return null;
        }

        //返回诊断快照，职责是让调试日志输出队列运行态。
        public RestockQueueDebugSnapshot CreateDebugSnapshot()
        {
            return new RestockQueueDebugSnapshot
            {
                DirtyCount = dirtySet.Count,
                ReadyCount = readyTasks.Count,
                BlockedCount = blockedTasks.Count,
                LastProcessTick = lastProcessTick,
                LastRebuildTick = lastRebuildTick,
                LastReason = lastProcessReason ?? "",
                DirtyTasks = dirtyQueue.ToList(),
                ReadyTasks = readyTasks.Values.ToList(),
                BlockedTasks = blockedTasks.Values.ToList()
            };
        }

        //处理脏任务队列，职责是按预算把缺货检查和货源搜索摊到多个 tick。
        private void ProcessDirtyQueue(int now)
        {
            int processed = 0;
            while (dirtyQueue.Count > 0 && processed < DirtyProcessPerTick)
            {
                RestockTaskKey key = dirtyQueue.Dequeue();
                if (!dirtySet.Remove(key))
                    continue;
                processed++;
                ProcessDirtyKey(key, now);
            }

            if (processed > 0)
                lastProcessTick = now;
        }

        //处理单个脏键，职责是重新判断缺口并生成 ready 或 blocked 任务。
        private void ProcessDirtyKey(RestockTaskKey key, int now)
        {
            Building_SimContainer storage = FindStorage(key.StorageId);
            if (!IsValidStorage(storage) || key.ThingDef == null)
                return;

            storage.ReconcilePendingReservationsForWorkScan();
            int needed = storage.CountNeededForWorkScan(key.ThingDef);
            if (needed <= 0)
            {
                readyTasks.Remove(key);
                blockedTasks.Remove(key);
                return;
            }

            RestockTask task = new RestockTask(storage, key.ThingDef, needed, now);
            Thing supply = FindAnySupply(storage, key.ThingDef, now, out string reason);
            if (supply == null)
            {
                BlockTask(task, reason, now + MissingSupplyRetryTicks);
                return;
            }

            task.SupplyId = supply.thingIDNumber;
            task.LastCheckedTick = now;
            task.StateReason = "等待派工";
            readyTasks[key] = task;
            blockedTasks.Remove(key);
        }

        //查找地图中任意可用货源，职责是让队列先生成候选，再由具体小人强校验。
        private Thing FindAnySupply(Building_SimContainer storage, ThingDef thingDef, int now, out string reason)
        {
            reason = "";
            List<Thing> candidates = map?.listerThings?.ThingsOfDef(thingDef);
            if (candidates == null || candidates.Count <= 0)
            {
                reason = "地图没有该商品货源";
                return null;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (!IsSupplyCandidate(storage, candidate))
                    continue;
                if (!IsSupplyUsableByAnyRestocker(storage, candidate))
                    continue;

                reason = "找到货源";
                return candidate;
            }

            reason = "没有可用货源";
            return null;
        }

        //判断货源是否适合作为队列候选，职责是过滤明显无效的地图物品。
        private static bool IsSupplyCandidate(Building_SimContainer storage, Thing thing)
        {
            if (storage == null || thing == null)
                return false;
            if (thing.Destroyed || !thing.Spawned || thing.stackCount <= 0)
                return false;
            if (thing.Map != storage.Map)
                return false;
            return !(thing.GetSlotGroup()?.parent is Building_SimContainer);
        }

        //判断货源是否至少能被一个补货员工使用，职责是避免 ready 队列指向全员禁用或不可达的物品。
        private bool IsSupplyUsableByAnyRestocker(Building_SimContainer storage, Thing thing)
        {
            List<Pawn> pawns = GetRestockCandidatePawns();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!CanPawnUseStorage(pawn, storage))
                    continue;
                if (IsValidSupplyForPawn(pawn, storage, thing, thing.def))
                    return true;
            }

            return false;
        }

        //尝试用任务构建 Job，职责是在最终派工前执行小人相关强校验。
        private Job TryMakeJobFromTask(Pawn pawn, RestockTask task, int now)
        {
            Building_SimContainer storage = FindStorage(task.StorageId);
            if (!CanPawnUseStorage(pawn, storage))
            {
                task.StateReason = "当前小人不可使用或不可达货柜";
                return null;
            }

            ThingDef thingDef = task.ThingDef;
            int needed = storage.CountNeeded(thingDef);
            if (needed <= 0)
            {
                readyTasks.Remove(task.Key);
                blockedTasks.Remove(task.Key);
                return null;
            }

            Thing supply = FindThing(task.SupplyId);
            if (!IsValidSupplyForPawn(pawn, storage, supply, thingDef))
            {
                supply = FindBestSupplyForPawn(pawn, storage, thingDef);
                if (!IsValidSupplyForPawn(pawn, storage, supply, thingDef))
                {
                    task.StateReason = "当前小人没有可用货源";
                    return null;
                }
            }

            int carryMax = MassUtility.CountToPickUpUntilOverEncumbered(pawn, supply);
            int amount = System.Math.Min(needed, System.Math.Min(carryMax, supply.stackCount));
            if (amount <= 0)
            {
                MoveTaskToBlocked(task, "可搬运数量为 0", now + BlockedRetryTicks);
                return null;
            }

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("DepositToMegaStorage"), supply, storage);
            job.count = amount;
            job.haulMode = HaulMode.ToCellStorage;
            job.plantDefToSow = thingDef;
            MoveTaskToBlocked(task, "补货任务已派发，等待预约写入", now + DispatchedRetryTicks);
            return job;
        }

        //判断小人是否能给货柜补货，职责是集中执行岗位、地图和可达性校验。
        private static bool CanPawnUseStorage(Pawn pawn, Building_SimContainer storage)
        {
            if (pawn?.Map == null || storage == null || storage.Destroyed || !storage.Spawned || storage.Map != pawn.Map)
                return false;

            WorkGiverDef workGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
            if (!VendingMachineUtility.IsVendingMachine(storage)
                && workGiverDef != null
                && !ShopStaffUtility.AllowsPawnForWorkGiver(ShopStaffUtility.FindShopFor(storage), pawn, workGiverDef))
                return false;

            return pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly);
        }

        //判断货源是否能被指定小人实际搬运，职责是避免队列候选失效后派出无效 Job。
        private static bool IsValidSupplyForPawn(Pawn pawn, Building_SimContainer storage, Thing supply, ThingDef thingDef)
        {
            if (pawn == null || storage == null || supply == null || thingDef == null)
                return false;
            if (!IsSupplyCandidate(storage, supply) || supply.def != thingDef)
                return false;
            if (supply.IsForbidden(pawn))
                return false;
            return pawn.CanReserve(supply) && pawn.CanReach(supply, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        //查找指定小人可用的最近货源，职责是替换失效的队列候选并保持任务商品不变。
        private static Thing FindBestSupplyForPawn(Pawn pawn, Building_SimContainer storage, ThingDef thingDef)
        {
            List<Thing> candidates = pawn?.Map?.listerThings?.ThingsOfDef(thingDef);
            if (candidates == null || candidates.Count <= 0)
                return null;

            Thing bestThing = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (!IsValidSupplyForPawn(pawn, storage, candidate, thingDef))
                    continue;

                float distance = (candidate.Position - pawn.Position).LengthHorizontalSquared;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestThing = candidate;
            }

            return bestThing;
        }

        //把 ready 任务移动到阻塞列表，职责是记录失败原因和重试时间。
        private void MoveTaskToBlocked(RestockTask task, string reason, int retryTick)
        {
            if (task == null)
                return;

            readyTasks.Remove(task.Key);
            BlockTask(task, reason, retryTick);
        }

        //记录阻塞任务，职责是让队列稍后重新计算而不是永久丢失。
        private void BlockTask(RestockTask task, string reason, int retryTick)
        {
            task.StateReason = reason ?? "";
            task.RetryTick = retryTick;
            blockedTasks[task.Key] = task;
        }

        //重试到期的阻塞任务，职责是把临时失败重新放回脏队列。
        private void RetryBlockedTasks(int now)
        {
            if (blockedTasks.Count <= 0)
                return;

            List<RestockTaskKey> retryKeys = null;
            foreach (KeyValuePair<RestockTaskKey, RestockTask> entry in blockedTasks)
            {
                if (entry.Value.RetryTick > now)
                    continue;

                if (retryKeys == null)
                    retryKeys = new List<RestockTaskKey>();
                retryKeys.Add(entry.Key);
                if (retryKeys.Count >= SupplyDefProcessPerTick)
                    break;
            }

            if (retryKeys == null)
                return;

            for (int i = 0; i < retryKeys.Count; i++)
            {
                RestockTaskKey key = retryKeys[i];
                blockedTasks.Remove(key);
                Building_SimContainer storage = FindStorage(key.StorageId);
                if (storage != null)
                    MarkDirty(storage, key.ThingDef, "阻塞补货任务重试");
            }
        }

        //给空闲补货员工派发队列任务，职责是让压力测试中空闲员工不依赖原版工作轮询延迟。
        private void TryDispatchIdleRestockPawns(int now)
        {
            if (readyTasks.Count <= 0 || map?.mapPawns == null)
                return;

            List<Pawn> pawns = GetRestockCandidatePawns();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!ShouldTryIdleDispatch(pawn, now))
                    continue;

                Job job = TryMakeJobForPawn(pawn);
                if (job != null)
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.MiscWork);
            }
        }

        //判断小人是否适合由队列主动派工，职责是只处理可中断低优先级工作且启用补货的小人。
        private static bool ShouldTryIdleDispatch(Pawn pawn, int now)
        {
            if (!CanUseRestockWorkGiver(pawn))
                return false;
            if (pawn.jobs == null || !IsIdleForRestockDispatch(pawn))
                return false;

            int offset = pawn.thingIDNumber >= 0 ? pawn.thingIDNumber % IdleDispatchIntervalTicks : 0;
            return (now + offset) % IdleDispatchIntervalTicks == 0;
        }

        //判断小人是否处于可接管状态，职责是避免打断医疗、睡觉、制作、收银和当前补货。
        private static bool IsIdleForRestockDispatch(Pawn pawn)
        {
            if (pawn?.CurJob == null)
                return true;

            string defName = pawn.CurJobDef?.defName;
            return !string.IsNullOrEmpty(defName) && IdleJobDefNames.Contains(defName);
        }

        //判断小人是否启用了补货 WorkGiver，职责是复用原版工作开关和能力限制。
        private static bool CanUseRestockWorkGiver(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Downed || pawn.InMentalState)
                return false;

            WorkGiverDef workGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
            if (workGiverDef == null)
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

        //返回当前地图可能参与补货的员工，职责是覆盖殖民者和玩家机械体。
        private List<Pawn> GetRestockCandidatePawns()
        {
            List<Pawn> result = new List<Pawn>();
            if (map?.mapPawns == null)
                return result;

            AddRestockCandidatePawns(result, map.mapPawns.FreeColonists);
            AddRestockCandidatePawns(result, map.mapPawns.SpawnedColonyMechs);
            return result;
        }

        //追加补货候选员工，职责是过滤空值、死亡和重复对象。
        private static void AddRestockCandidatePawns(List<Pawn> result, List<Pawn> source)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                Pawn pawn = source[i];
                if (pawn == null || result.Contains(pawn))
                    continue;
                if (CanUseRestockWorkGiver(pawn))
                    result.Add(pawn);
            }
        }

        //分片巡检货柜，职责是修复漏事件造成的缺货未入队。
        private void ReconcileStorageSlice(int now)
        {
            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null || buildings.Count <= 0)
                return;

            int scanned = 0;
            while (scanned < DirtyProcessPerTick && buildings.Count > 0)
            {
                if (reconcileCursor >= buildings.Count)
                    reconcileCursor = 0;

                Building_SimContainer storage = buildings[reconcileCursor++] as Building_SimContainer;
                scanned++;
                if (!IsValidStorage(storage))
                    continue;

                storage.ReconcilePendingReservationsForWorkScan();
                foreach (ThingDef thingDef in storage.ActiveDefs)
                {
                    if (storage.CountNeededForWorkScan(thingDef) > 0)
                        MarkDirty(storage, thingDef, "补货兜底巡检");
                }
            }

            lastProcessTick = now;
        }

        //按编号查找货柜，职责是从队列持有的稳定 ID 还原地图对象。
        private Building_SimContainer FindStorage(int storageId)
        {
            List<Building> buildings = map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null)
                return null;

            for (int i = 0; i < buildings.Count; i++)
            {
                Building_SimContainer storage = buildings[i] as Building_SimContainer;
                if (storage != null && storage.thingIDNumber == storageId)
                    return storage;
            }

            return null;
        }

        //按编号查找地图物品，职责是恢复任务记录中的货源引用。
        private Thing FindThing(int thingId)
        {
            if (thingId < 0 || map?.listerThings == null)
                return null;

            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;
            }

            return null;
        }

        //判断货柜是否有效，职责是过滤离图、销毁和跨地图对象。
        private bool IsValidStorage(Building_SimContainer storage)
        {
            return storage != null && !storage.Destroyed && storage.Spawned && storage.Map == map;
        }
    }

    //补货队列诊断快照，职责是把队列内部状态安全交给日志构建器。
    public sealed class RestockQueueDebugSnapshot
    {
        public int DirtyCount;
        public int ReadyCount;
        public int BlockedCount;
        public int LastProcessTick;
        public int LastRebuildTick;
        public string LastReason;
        public List<RestockTaskKey> DirtyTasks = new List<RestockTaskKey>();
        public List<RestockTask> ReadyTasks = new List<RestockTask>();
        public List<RestockTask> BlockedTasks = new List<RestockTask>();
    }
}
