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
        private Job cachedJob;

        //自动补货不枚举地图建筑，手动调用仍可直接使用 HasJobOnThing。
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

        //判断指定专业货柜是否能为当前 Pawn 创建 Job，并缓存同 tick 结果。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return ResolveManualJob(pawn, t as Building_UniqueGoodsContainer) != null;
        }

        //返回指定专业货柜的精确搬运 Job。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Job result = ResolveManualJob(pawn, t as Building_UniqueGoodsContainer);
            cachedJob = null;
            return result;
        }

        //通过协调器解析一次专业补货，避免同一原版查询链重复执行可达性判断。
        private Job ResolveManualJob(Pawn pawn, Building_UniqueGoodsContainer storage)
        {
            if (storage == null || pawn?.Map == null)
                return null;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (cachedTick == now
                && cachedPawnId == pawn.thingIDNumber
                && cachedStorageId == storage.thingIDNumber)
                return cachedJob;

            cachedTick = now;
            cachedPawnId = pawn.thingIDNumber;
            cachedStorageId = storage.thingIDNumber;
            cachedJob = pawn.Map.GetComponent<MapComponent_RestockTaskQueue>()
                ?.TryMakeUniqueJobForStorage(pawn, storage);
            return cachedJob;
        }
    }
}
