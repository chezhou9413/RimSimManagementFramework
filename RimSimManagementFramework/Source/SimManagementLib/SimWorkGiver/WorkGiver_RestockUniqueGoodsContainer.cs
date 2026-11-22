using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //专业补货适配器，职责是把原版工作入口转交给精确来源请求队列。
    public sealed class WorkGiver_RestockUniqueGoodsContainer : WorkGiver_Scanner
    {
        private int cachedTick = -1;
        private int cachedPawnId = -1;
        private int cachedStorageId = -1;
        private bool cachedForced;
        private Job cachedJob;

        //声明手动命令可检查人造建筑，自动找工作由 NonScanJob 直接领取请求。
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        //自动补货不枚举地图建筑，手动调用通过 PotentialWorkThingRequest 进入 HasJobOnThing。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            yield break;
        }

        //直接从地图协调器领取一个专业补货 Job。
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<MapComponent_RestockTaskQueue>()
                ?.TryMakeJobForPawn(pawn, RestockRequestKind.Unique);
        }

        //判断指定专业货柜是否能为当前 Pawn 创建 Job，并缓存同 tick 有效结果。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return ResolveManualJob(pawn, t as Building_UniqueGoodsContainer, forced) != null;
        }

        //返回指定专业货柜的精确搬运 Job。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Job result = ResolveManualJob(pawn, t as Building_UniqueGoodsContainer, forced);
            ClearManualCache();
            return result;
        }

        //通过协调器解析一次专业补货，只缓存有效 Job 避免暂停态二次右键命中空缓存。
        private Job ResolveManualJob(Pawn pawn, Building_UniqueGoodsContainer storage, bool forced)
        {
            if (storage == null || pawn?.Map == null)
                return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (cachedJob != null
                && cachedTick == now
                && cachedPawnId == pawn.thingIDNumber
                && cachedStorageId == storage.thingIDNumber
                && cachedForced == forced)
                return cachedJob;

            Job job = pawn.Map.GetComponent<MapComponent_RestockTaskQueue>()
                ?.TryMakeUniqueJobForStorage(pawn, storage, forced);
            if (job == null)
            {
                ClearManualCache();
                return null;
            }

            cachedTick = now;
            cachedPawnId = pawn.thingIDNumber;
            cachedStorageId = storage.thingIDNumber;
            cachedForced = forced;
            cachedJob = job;
            return job;
        }

        //清空手动专业补货单次查询缓存。
        private void ClearManualCache()
        {
            cachedTick = -1;
            cachedPawnId = -1;
            cachedStorageId = -1;
            cachedForced = false;
            cachedJob = null;
        }
    }
}
