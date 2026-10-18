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
        private int cachedSlotIndex = -1;
        private Thing cachedSource;

        /// <summary>
        /// 返回殖民者建筑中的收藏品展台候选。
        /// </summary>
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

        /// <summary>
        /// 判断指定展台是否有可执行的填充工作。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_CollectibleDisplayStand stand))
                return false;

            return FindFillTargetCached(pawn, stand, out _, out _);
        }

        /// <summary>
        /// 为展台创建填充任务，并把槽位索引写入 job.count。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_CollectibleDisplayStand stand))
                return null;

            if (!FindFillTargetCached(pawn, stand, out int slotIndex, out Thing source))
                return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("FillCollectibleDisplayStand"), source, stand);
            job.count = slotIndex;
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }

        /// <summary>
        /// 缓存同一 tick 的槽位和来源查找结果，避免 RimWorld 工作扫描重复搜索。
        /// </summary>
        private bool FindFillTargetCached(Pawn pawn, Building_CollectibleDisplayStand stand, out int slotIndex, out Thing source)
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            int pawnId = pawn?.thingIDNumber ?? -1;
            int standId = stand?.thingIDNumber ?? -1;

            if (cachedTick == tick && cachedPawnId == pawnId && cachedStandId == standId)
            {
                slotIndex = cachedSlotIndex;
                source = cachedSource;
                return slotIndex >= 0 && source != null && !source.Destroyed;
            }

            cachedTick = tick;
            cachedPawnId = pawnId;
            cachedStandId = standId;
            cachedSlotIndex = -1;
            cachedSource = null;

            if (stand != null && stand.TryFindFillTarget(pawn, out cachedSlotIndex, out cachedSource))
            {
                slotIndex = cachedSlotIndex;
                source = cachedSource;
                return true;
            }

            slotIndex = -1;
            source = null;
            return false;
        }
    }
}
