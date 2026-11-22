using System.Collections.Generic;
using RimWorld;
using SimManagementLib.SimThingClass;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 扫描有待填充槽位的收藏品展台，并为小人分配搬运收藏品的工作。
    /// </summary>
    public class WorkGiver_FillCollectibleDisplayStand : WorkGiver_Scanner
    {
        private int cachedTick = -1;
        private int cachedPawnId = -1;
        private int cachedStandId = -1;
        private bool cachedForced;
        private int cachedSlotIndex = -1;
        private Thing cachedSource;

        //使用不存入区域的 ThingHolder 分组，避免全图区域扫描同时让右键直接命中展台。
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.ThingHolder);

        //返回殖民者建筑中的收藏品展台候选。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Building> buildings = pawn?.Map?.listerBuildings?.allBuildingsColonist;
            if (buildings == null)
                yield break;

            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_CollectibleDisplayStand stand)
                    yield return stand;
            }
        }

        //判断指定展台是否有可执行的填充工作。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_CollectibleDisplayStand stand))
                return false;

            return FindFillTargetCached(pawn, stand, forced, out _, out _);
        }

        //为展台创建填充任务，并把槽位索引写入 job.count。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_CollectibleDisplayStand stand))
                return null;

            if (!FindFillTargetCached(pawn, stand, forced, out int slotIndex, out Thing source))
                return null;

            ClearCache();
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("FillCollectibleDisplayStand"), source, stand);
            job.count = slotIndex;
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }

        //只缓存同一 tick 的有效查找结果，供紧随其后的 JobOnThing 复用并避免暂停态命中旧空缓存。
        private bool FindFillTargetCached(Pawn pawn, Building_CollectibleDisplayStand stand, bool forced, out int slotIndex, out Thing source)
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            int pawnId = pawn?.thingIDNumber ?? -1;
            int standId = stand?.thingIDNumber ?? -1;

            if (cachedSlotIndex >= 0
                && cachedSource != null
                && !cachedSource.Destroyed
                && cachedTick == tick
                && cachedPawnId == pawnId
                && cachedStandId == standId
                && cachedForced == forced)
            {
                slotIndex = cachedSlotIndex;
                source = cachedSource;
                return true;
            }

            if (stand != null && stand.TryFindFillTarget(pawn, out slotIndex, out source, forced))
            {
                cachedTick = tick;
                cachedPawnId = pawnId;
                cachedStandId = standId;
                cachedForced = forced;
                cachedSlotIndex = slotIndex;
                cachedSource = source;
                return true;
            }

            ClearCache();
            slotIndex = -1;
            source = null;
            return false;
        }

        //清空展台单次工作查询缓存。
        private void ClearCache()
        {
            cachedTick = -1;
            cachedPawnId = -1;
            cachedStandId = -1;
            cachedForced = false;
            cachedSlotIndex = -1;
            cachedSource = null;
        }
    }
}
