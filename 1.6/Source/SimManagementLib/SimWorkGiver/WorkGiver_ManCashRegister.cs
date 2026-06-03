using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 为营业中或仍有待结账顾客的商店收银台分配收银员值守工作。
    /// </summary>
    public class WorkGiver_ManCashRegister : WorkGiver_Scanner
    {
        private const int CandidateCacheTicks = 61;
        private static readonly Dictionary<int, RegisterCandidateCache> candidateCaches = new Dictionary<int, RegisterCandidateCache>();

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        /// <summary>
        /// 枚举当前地图中允许收银的玩家收银台，负责让关店后的待付款顾客仍能完成结账。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateRegisters(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        /// <summary>
        /// 判断指定 Pawn 是否能在该收银台执行收银工作，负责保留关店后的清队列服务。
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null) return false;
            SimZone.Zone_Shop shop = ShopStaffUtility.FindShopFor(register);
            if (!ShopStaffUtility.CanCashierWorkAt(shop)) return false;
            if (!ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, def))
                return false;

            // 收银台值守需要独占建筑，避免多个店员同时占用同一台收银台。
            if (!pawn.CanReserve(register, 1, -1, null, forced)) return false;

            return true;
        }

        /// <summary>
        /// 创建收银台值守 Job。
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_ManCashRegister"), t);
        }

        /// <summary>
        /// 返回短时间缓存的收银台候选，负责避免每个找工作的员工都重复全图扫描建筑。
        /// </summary>
        private static List<Thing> GetCandidateRegisters(Pawn pawn)
        {
            if (pawn?.Map?.listerBuildings == null) return EmptyThingList;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out RegisterCandidateCache cache) || cache == null)
            {
                cache = new RegisterCandidateCache();
                candidateCaches[mapId] = cache;
            }

            if (now < cache.nextRefreshTick)
                return cache.candidates;

            RefreshCandidateRegisters(pawn.Map, cache, now);
            return cache.candidates;
        }

        /// <summary>
        /// 刷新当前地图的收银台候选缓存，负责只保留仍允许收银工作的建筑。
        /// </summary>
        private static void RefreshCandidateRegisters(Map map, RegisterCandidateCache cache, int now)
        {
            cache.candidates.Clear();
            cache.nextRefreshTick = now + CandidateCacheTicks;

            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_CashRegister register = buildings[i] as Building_CashRegister;
                if (register == null || register.Destroyed || !register.Spawned) continue;
                if (!ShopStaffUtility.CanCashierWorkAt(ShopStaffUtility.FindShopFor(register))) continue;
                cache.candidates.Add(register);
            }
        }

        private static readonly List<Thing> EmptyThingList = new List<Thing>(0);

        /// <summary>
        /// 保存单张地图的收银台候选缓存，负责减少 WorkGiver 高频扫描分配。
        /// </summary>
        private class RegisterCandidateCache
        {
            public int nextRefreshTick = -1;
            public readonly List<Thing> candidates = new List<Thing>();
        }
    }
}
