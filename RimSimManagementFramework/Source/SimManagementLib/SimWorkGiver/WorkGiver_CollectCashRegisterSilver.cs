using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //为带现金库存的经营建筑分配取走已结算银币的服务工作。
    public class WorkGiver_CollectCashRegisterSilver : WorkGiver_Scanner
    {
        private const int CandidateCacheTicks = 97;
        private static readonly Dictionary<int, CashBuildingCandidateCache> candidateCaches = new Dictionary<int, CashBuildingCandidateCache>();
        private static readonly List<Thing> EmptyThingList = new List<Thing>(0);
        private static JobDef cachedCollectSilverJobDef;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        //自动取现遵守殖民者作息表和机械体工作状态。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            if (forced) return false;
            return ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn);
        }

        //返回按地图缓存的待取现经营建筑，避免每次工作扫描全图遍历所有殖民地建筑。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateBuildings(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        //判断指定 Pawn 是否能从该建筑取走达到阈值的可提取银币，多店铺时自动遵循本店收银岗位名单。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t == null || t.Destroyed || !t.Spawned || pawn?.Map == null || t.Map != pawn.Map) return false;
            ThingComp_CashStorage cash = t.TryGetComp<ThingComp_CashStorage>();
            if (cash == null || !cash.ShouldAutoWithdraw()) return false;
            Zone_Shop shop = t is Building_CashRegister register
                ? ShopStaffUtility.FindShopFor(register)
                : ShopDataUtility.FindShopZone(t.Map, t.Position);
            if (!forced)
            {
                if (t is Building_CashRegister && !ShopStaffUtility.IsShopOpenForWork(shop)) return false;
                if (shop != null && !ShopStaffUtility.AllowsPawnForCashier(shop, pawn)) return false;
            }
            if (!pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly)) return false;
            return true;
        }

        //创建从经营建筑取出一批银币的 Job。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            ThingComp_CashStorage cash = t?.TryGetComp<ThingComp_CashStorage>();
            if (cash == null) return null;

            int desired = cash.AutoWithdrawAmount;
            if (desired <= 0) return null;

            JobDef jobDef = cachedCollectSilverJobDef ??= DefDatabase<JobDef>.GetNamed("Sim_CollectCashRegisterSilver");
            Job job = JobMaker.MakeJob(jobDef, t);
            job.count = desired;
            return job;
        }

        //读取或刷新当前地图中带现金库存的建筑缓存。
        private static List<Thing> GetCandidateBuildings(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map?.listerBuildings == null) return EmptyThingList;

            int mapId = map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out CashBuildingCandidateCache cache) || cache == null || cache.map != map)
            {
                cache = new CashBuildingCandidateCache { map = map };
                candidateCaches[mapId] = cache;
            }

            if (now < cache.nextRefreshTick && cache.map == map)
                return cache.candidates;

            cache.map = map;
            cache.candidates.Clear();
            cache.nextRefreshTick = now + CandidateCacheTicks;

            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building == null || building.Destroyed || !building.Spawned) continue;
                ThingComp_CashStorage cash = building.GetComp<ThingComp_CashStorage>();
                if (cash != null && cash.ShouldAutoWithdraw())
                    cache.candidates.Add(building);
            }
            return cache.candidates;
        }

        //保存单张地图的可取现经营建筑候选缓存。
        private sealed class CashBuildingCandidateCache
        {
            public Map map;
            public int nextRefreshTick = -1;
            public readonly List<Thing> candidates = new List<Thing>();
        }
    }
}
