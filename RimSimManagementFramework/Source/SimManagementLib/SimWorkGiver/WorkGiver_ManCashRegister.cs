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
        public const int HandoverLeadTicks = 1250;
        private static readonly Dictionary<int, RegisterCandidateCache> candidateCaches = new Dictionary<int, RegisterCandidateCache>();
        private static JobDef cachedManRegisterJobDef;

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;
        public override bool Prioritized => true;
        protected virtual bool WaitingCustomersOnly => false;

        //自动派工遵守殖民者日程表、机械体工作模式及基本生存需求，避免非上班或充电时段被叫去收银。
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return true;
            if (forced) return false;
            if (ShopStaffUtility.IsPawnOffDutyForAutomaticWork(pawn)) return true;
            return pawn.needs?.food?.CurCategory >= HungerCategory.UrgentlyHungry
                || pawn.needs?.rest?.CurCategory >= RestCategory.VeryTired;
        }

        //优先赶去需要无缝交接的本店柜台，其次是本店排队/营业柜台，最后才考虑未指定员工的柜台。
        public override float GetPriority(Pawn pawn, TargetInfo target)
        {
            if (!(target.Thing is Building_CashRegister register)) return 0f;
            SimZone.Zone_Shop shop = ShopStaffUtility.FindShopFor(register);
            bool assigned = ShopStaffUtility.IsAssignedCashier(shop, pawn);
            bool waiting = register.HasWaitingCustomers;
            if (assigned && register.NeedsHandoverFor(pawn)) return 4.5f;
            if (assigned) return waiting ? 4f : 3f;
            return waiting ? 2f : 1f;
        }

        //枚举当前地图中允许收银的玩家收银台，让关店后的待付款顾客仍能完成结账。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> candidates = GetCandidateRegisters(pawn);
            for (int i = 0; i < candidates.Count; i++)
                yield return candidates[i];
        }

        //自动派工严格遵守店铺收银岗位与 24 小时排班，并在主班临近下班或需离开时允许接班员提前接力预约。
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
            if (!register.CanAcceptStaffReservation(pawn, out bool isHandover))
                return false;

            //已经饥饿、疲惫或自己也即将下班的员工不参与接班，防止两人互相交接打乒乓。
            if (!forced && isHandover)
            {
                if (pawn.needs?.food?.CurCategory >= HungerCategory.Hungry
                    || pawn.needs?.rest?.CurCategory >= RestCategory.Tired
                    || ShopStaffUtility.IsCashierShiftEndingSoon(shop, pawn, HandoverLeadTicks))
                    return false;
            }

            bool permitted = forced
                ? ShopStaffUtility.CanRelieveCashier(shop, pawn)
                : (isHandover
                    ? ShopStaffUtility.AllowsPawnForCashierHandover(shop, pawn, HandoverLeadTicks)
                    : ShopStaffUtility.AllowsPawnForCashier(shop, pawn));
            if (!permitted)
                return false;

            //使用 2 槽共享预约：平时由 CanAcceptStaffReservation 保持独占，交接时允许接班员提前出发无缝顶替。
            if (!pawn.CanReserve(register, Building_CashRegister.MaxStaffReservations, 0, null, forced))
                return false;

            return true;
        }

        //保存派工来源与交接标记，营业内按排班稳守值班，关店收尾或手动跨岗补位在队列清空后离开。
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_CashRegister register = (Building_CashRegister)t;
            SimZone.Zone_Shop shop = ShopStaffUtility.FindShopFor(register);
            register.CanAcceptStaffReservation(pawn, out bool isHandover);
            JobDef jobDef = cachedManRegisterJobDef ??= DefDatabase<JobDef>.GetNamed("Sim_ManCashRegister");
            Job job = JobMaker.MakeJob(jobDef, register, isHandover ? register : LocalTargetInfo.Invalid);
            job.workGiverDef = def;
            bool shortRelief = (forced && !ShopStaffUtility.AllowsPawnForCashierRoleIgnoringShift(shop, pawn))
                || !ShopStaffUtility.IsShopOpenForWork(shop);
            job.count = shortRelief ? 1 : 0;
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
