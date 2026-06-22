using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //补货工作分配器，职责是把 RimWorld 找工作入口桥接到地图级补货任务队列。
    public class WorkGiver_RestockMegaStorage : WorkGiver_Scanner
    {
        private static WorkGiverDef cachedWorkGiverDef;

        //清空补货候选缓存，职责是保留调试入口对旧扫描状态的兼容清理能力。
        public static void ClearRestockCandidateCaches()
        {
            cachedWorkGiverDef = null;
        }

        //返回当前补货 WorkGiverDef，职责是避免重复查询 DefDatabase。
        private static WorkGiverDef CurrentWorkGiverDef
        {
            get
            {
                if (cachedWorkGiverDef == null)
                    cachedWorkGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RestockMegaStorage");
                return cachedWorkGiverDef;
            }
        }

        //返回空候选列表，职责是让 scanThings=false 的补货工作只走 NonScanJob 队列桥接。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            yield break;
        }

        //直接从地图补货队列领取任务，职责是避免小人找工作时现场全图扫描货柜和货源。
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<MapComponent_RestockTaskQueue>()?.TryMakeJobForPawn(pawn);
        }

        //判断指定货柜是否有补货任务，职责是兼容手动扫描调用并使用确定性强校验。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage))
                return false;

            return TryFindRestockSupply(pawn, storage, out _, out _) != null;
        }

        //为指定货柜生成补货任务，职责是兼容外部扫描入口并在派工前执行强校验。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_SimContainer storage))
                return null;

            ThingDef thingDef;
            int needed;
            Thing supply = TryFindRestockSupply(pawn, storage, out thingDef, out needed);
            if (supply == null)
                return null;

            return MakeRestockJobFromSupply(pawn, storage, supply, thingDef, needed);
        }

        //查找指定货柜可补货的货源，职责是给兼容入口提供不受预算影响的确定性结果。
        private static Thing TryFindRestockSupply(Pawn pawn, Building_SimContainer storage, out ThingDef thingDef, out int needed)
        {
            thingDef = null;
            needed = 0;
            if (!CanPawnUseStorage(pawn, storage))
                return null;

            storage.ReconcilePendingReservations();
            foreach (ThingDef activeDef in storage.ActiveDefs)
            {
                int currentNeed = storage.CountNeeded(activeDef);
                if (currentNeed <= 0)
                    continue;

                Thing supply = FindBestSupplyForDef(pawn, storage, activeDef);
                if (supply == null)
                    continue;

                thingDef = activeDef;
                needed = currentNeed;
                return supply;
            }

            return null;
        }

        //查找最近的可用货源，职责是只在兼容入口中做一次完整确定性搜索。
        private static Thing FindBestSupplyForDef(Pawn pawn, Building_SimContainer storage, ThingDef thingDef)
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

        //按已找到的货源创建补货 Job，职责是让兼容扫描入口复用同一套任务构建规则。
        private static Job MakeRestockJobFromSupply(Pawn pawn, Building_SimContainer storage, Thing supply, ThingDef thingDef, int needed)
        {
            if (!IsValidSupplyForPawn(pawn, storage, supply, thingDef))
                return null;

            int carryMax = MassUtility.CountToPickUpUntilOverEncumbered(pawn, supply);
            int amount = System.Math.Min(needed, System.Math.Min(carryMax, supply.stackCount));
            if (amount <= 0)
                return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("DepositToMegaStorage"), supply, storage);
            job.count = amount;
            job.haulMode = HaulMode.ToCellStorage;
            job.plantDefToSow = thingDef;
            return job;
        }

        //判断小人是否能给货柜补货，职责是集中执行地图、岗位和可达性校验。
        private static bool CanPawnUseStorage(Pawn pawn, Building_SimContainer storage)
        {
            if (pawn?.Map == null || storage == null || storage.Destroyed || !storage.Spawned || storage.Map != pawn.Map)
                return false;

            Zone_Shop shop = ShopStaffUtility.FindShopFor(storage);
            if (!VendingMachineUtility.IsVendingMachine(storage)
                && CurrentWorkGiverDef != null
                && !ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, CurrentWorkGiverDef))
                return false;

            return pawn.CanReach(storage, PathEndMode.Touch, Danger.Deadly);
        }

        //判断货源是否能被指定小人实际搬运，职责是避免创建无法执行的补货 Job。
        private static bool IsValidSupplyForPawn(Pawn pawn, Building_SimContainer storage, Thing supply, ThingDef thingDef)
        {
            if (pawn == null || storage == null || supply == null || thingDef == null)
                return false;
            if (supply.Destroyed || !supply.Spawned || supply.stackCount <= 0 || supply.def != thingDef)
                return false;
            if (supply.Map != pawn.Map || storage.Map != pawn.Map)
                return false;
            if (supply.IsForbidden(pawn))
                return false;
            if (supply.GetSlotGroup()?.parent is Building_SimContainer)
                return false;

            return pawn.CanReserve(supply) && pawn.CanReach(supply, PathEndMode.ClosestTouch, Danger.Deadly);
        }
    }
}
