using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimWorkGiver
{
    //为营业中或仍有顾客等待接待的收银台分配常驻员工和临时补位。
    public class WorkGiver_ManCashRegister : WorkGiver_Scanner
    {
        private const int CandidateCacheTicks = 61;
        private static readonly Dictionary<int, RegisterCandidateCache> candidateCaches = new Dictionary<int, RegisterCandidateCache>();

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;
        public override bool Prioritized => true;
        protected virtual bool WaitingCustomersOnly => false;

        //自动派工保留休息和进食所需时间，避免员工刚离岗就被同一工作立即叫回。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            return !forced && (pawn.needs?.food?.CurCategory >= HungerCategory.UrgentlyHungry
                || pawn.needs?.rest?.CurCategory >= RestCategory.VeryTired);
        }

        //同一工作优先级下先服务正在排队的柜台。
        public override float GetPriority(Pawn pawn, TargetInfo target)
            => target.Thing is Building_CashRegister register && register.HasWaitingCustomers ? 2f : 1f;

        /// <summary>
        /// 枚举当前地图中允许收银的玩家收银台，负责让关店后的待付款顾客仍能完成结账。
        /// </summary>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateRegisters(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        //有顾客排队时允许合格人员补位，空闲时仍按固定岗位安排。
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = t as Building_CashRegister;
            if (register == null) return false;
            if (pawn?.Map == null || register.Map != pawn.Map || register.Destroyed || !register.Spawned) return false;
            if (ShouldSkip(pawn, forced)) return false;
            SimZone.Zone_Shop shop = ShopStaffUtility.FindShopFor(register);
            bool waiting = register.HasWaitingCustomers;
            if (WaitingCustomersOnly && !waiting) return false;
            if (!waiting && !ShopStaffUtility.CanCashierWorkAt(shop)) return false;
            if (!ShopStaffUtility.CanPawnPerformWorkType(pawn, def.workType, !forced)
                || MissingRequiredCapacity(pawn) != null) return false;
            if (!(waiting ? ShopStaffUtility.CanRelieveCashier(shop, pawn)
                : ShopStaffUtility.AllowsPawnForWorkGiver(shop, pawn, def)))
                return false;

            //收银台值守独占建筑，已经有人接单赶来时不会再派第二名员工。
            if (!pawn.CanReserve(register, 1, -1, null, forced)) return false;

            return true;
        }

        //保存派工来源，并区分常驻班次与临时补位。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_ManCashRegister"), t);
            job.workGiverDef = def;
            //临时补位不占用完整班次，队列清空后交还员工。
            job.count = WaitingCustomersOnly || !ShopStaffUtility.AllowsPawnForWorkGiver(
                ShopStaffUtility.FindShopFor((Building_CashRegister)t), pawn, def) ? 1 : 0;
            return job;
        }

        /// <summary>
        /// 返回短时间缓存的收银台候选，负责避免每个找工作的员工都重复全图扫描建筑。
        /// </summary>
        private static List<Thing> GetCandidateRegisters(Pawn pawn)
        {
            if (pawn?.Map?.listerBuildings == null) return EmptyThingList;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!candidateCaches.TryGetValue(mapId, out RegisterCandidateCache cache) || cache == null || cache.map != pawn.Map)
            {
                cache = new RegisterCandidateCache();
                cache.map = pawn.Map;
                candidateCaches[mapId] = cache;
            }

            if (now < cache.nextRefreshTick && cache.IsForMap(pawn.Map))
                return cache.candidates;

            RefreshCandidateRegisters(pawn.Map, cache, now);
            return cache.candidates;
        }

        //缓存营业柜台和仍有待接待业务的柜台。
        private static void RefreshCandidateRegisters(Map map, RegisterCandidateCache cache, int now)
        {
            cache.map = map;
            cache.candidates.Clear();
            cache.nextRefreshTick = now + CandidateCacheTicks;

            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_CashRegister register = buildings[i] as Building_CashRegister;
                if (register == null || register.Destroyed || !register.Spawned) continue;
                if (!register.HasWaitingCustomers && !ShopStaffUtility.CanCashierWorkAt(ShopStaffUtility.FindShopFor(register))) continue;
                cache.candidates.Add(register);
            }
        }

        private static readonly List<Thing> EmptyThingList = new List<Thing>(0);

        /// <summary>
        /// 保存单张地图的收银台候选缓存，负责减少 WorkGiver 高频扫描分配。
        /// </summary>
        private class RegisterCandidateCache
        {
            public Map map;
            public int nextRefreshTick = -1;
            public readonly List<Thing> candidates = new List<Thing>();

            public bool IsForMap(Map currentMap)
            {
                return map == currentMap;
            }
        }
    }
}
