using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //普通补货适配器，职责是把原版找工作和右键入口转交给地图补货协调器。
    public class WorkGiver_RestockMegaStorage : WorkGiver_Scanner
    {
        private int cachedTick = -1;
        private int cachedPawnId = -1;
        private int cachedStorageId = -1;
        private bool cachedForced;
        private Job cachedJob;

        //声明手动命令可检查人造建筑，自动找工作由 NonScanJob 直接领取请求。
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        //保留旧调试清理入口，当前适配器缓存只存活一个 tick，无需全局状态。
        public static void ClearRestockCandidateCaches()
        {
        }

        //直接从地图协调器领取一个普通补货 Job。
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<MapComponent_RestockTaskQueue>()
                ?.TryMakeJobForPawn(pawn, RestockRequestKind.Bulk);
        }

        //判断指定普通货柜是否能为当前 Pawn 创建 Job，并缓存同 tick 结果。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return ResolveManualJob(pawn, t as Building_SimContainer, forced) != null;
        }

        //返回指定普通货柜的手动补货 Job。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Job result = ResolveManualJob(pawn, t as Building_SimContainer, forced);
            cachedJob = null;
            return result;
        }

        //通过协调器解析一次手动补货，避免 HasJob 与 JobOnThing 重复搜索货源。
        private Job ResolveManualJob(Pawn pawn, Building_SimContainer storage, bool forced)
        {
            if (storage == null || storage is Building_UniqueGoodsContainer || pawn?.Map == null)
                return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (cachedTick == now
                && cachedPawnId == pawn.thingIDNumber
                && cachedStorageId == storage.thingIDNumber
                && cachedForced == forced)
                return cachedJob;

            cachedTick = now;
            cachedPawnId = pawn.thingIDNumber;
            cachedStorageId = storage.thingIDNumber;
            cachedForced = forced;
            cachedJob = pawn.Map.GetComponent<MapComponent_RestockTaskQueue>()
                ?.TryMakeBulkJobForStorage(pawn, storage, forced);
            return cachedJob;
        }
    }
}
