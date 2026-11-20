using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimMapComp
{
    //类职责：为已分配商店岗位的玩家机械体补充派工，让机械体不依赖殖民者工作优先级也能执行店员工作。
    public class MapComponent_MechShopStaffDispatcher : MapComponent
    {
        private const int DispatchSliceIntervalTicks = 31;
        private const int MechsPerDispatchSlice = 4;
        private const int PawnScanSalt = 37;
        private static readonly HashSet<string> IdleJobDefNames = new HashSet<string>
        {
            "Wait",
            "Wait_MaintainPosture"
        };
        private int dispatchCursor;

        //函数职责：创建当前地图的机械体店员派工组件。
        public MapComponent_MechShopStaffDispatcher(Map map) : base(map)
        {
        }

        //函数职责：周期性扫描已分配岗位的玩家机械体，并在空闲时尝试创建商店工作。
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (map?.mapPawns == null || map.zoneManager?.AllZones == null) return;

            int now = Find.TickManager?.TicksGame ?? 0;
            int mapOffset = map.uniqueID >= 0 ? map.uniqueID % DispatchSliceIntervalTicks : 0;
            if ((now + PawnScanSalt + mapOffset) % DispatchSliceIntervalTicks != 0)
                return;

            List<Pawn> mechs = map.mapPawns.SpawnedColonyMechs;
            if (mechs.Count <= 0)
            {
                dispatchCursor = 0;
                return;
            }

            int processCount = System.Math.Min(MechsPerDispatchSlice, mechs.Count);
            for (int i = 0; i < processCount; i++)
            {
                if (dispatchCursor >= mechs.Count)
                    dispatchCursor = 0;
                Pawn pawn = mechs[dispatchCursor++];
                if (!ShouldTryDispatch(pawn)) continue;
                TryDispatchPawn(pawn);
            }
        }

        //判断机械体当前是否处于工作模式、电量充足且无排队任务的空闲状态。
        private static bool ShouldTryDispatch(Pawn pawn)
        {
            if (!ShopStaffUtility.CanMechanicalStaffWorkAutomatically(pawn)) return false;
            if (pawn.jobs == null || (pawn.jobs.jobQueue != null && pawn.jobs.jobQueue.Count > 0)) return false;
            return IsIdleForShopDispatch(pawn);
        }

        //判断机械体是否只处于空闲等待状态，避免派工打断已有实际任务。
        private static bool IsIdleForShopDispatch(Pawn pawn)
        {
            if (pawn?.CurJob == null) return true;
            string defName = pawn.CurJobDef?.defName;
            return !string.IsNullOrEmpty(defName) && IdleJobDefNames.Contains(defName);
        }

        //为单个机械体按已分配岗位寻找可执行工作。
        private bool TryDispatchPawn(Pawn pawn)
        {
            List<Zone_Shop> shops = GetAssignedShops(pawn);
            for (int i = 0; i < shops.Count; i++)
            {
                Zone_Shop shop = shops[i];
                if (TryDispatchPawnInShop(pawn, shop))
                    return true;
            }

            return false;
        }

        //返回机械体在当前地图中被分配到岗位的商店。
        private List<Zone_Shop> GetAssignedShops(Pawn pawn)
        {
            List<Zone_Shop> result = new List<Zone_Shop>();
            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                Zone_Shop shop = zones[i] as Zone_Shop;
                if (shop == null) continue;
                if (HasAnyAssignedRole(shop, pawn))
                    result.Add(shop);
            }

            return result;
        }

        //判断机械体是否在商店任意可见岗位中。
        private static bool HasAnyAssignedRole(Zone_Shop shop, Pawn pawn)
        {
            List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(shop);
            for (int i = 0; i < roles.Count; i++)
            {
                ShopStaffRoleDef role = roles[i];
                if (role != null && shop.IsPawnAssignedToRole(role.defName, pawn))
                    return true;
            }

            return false;
        }

        //在指定商店内按岗位顺序尝试为机械体生成工作。
        private static bool TryDispatchPawnInShop(Pawn pawn, Zone_Shop shop)
        {
            List<ShopStaffRoleDef> roles = ShopStaffUtility.GetVisibleRoles(shop);
            for (int i = 0; i < roles.Count; i++)
            {
                ShopStaffRoleDef role = roles[i];
                if (role == null || !shop.IsPawnAssignedToRole(role.defName, pawn)) continue;
                if (TryDispatchPawnForRole(pawn, role))
                    return true;
            }

            return false;
        }

        //按岗位绑定的 WorkGiver 顺序尝试创建一个自动 Job，不设置 playerForced 避免阻塞撤岗、充电与待命判断。
        private static bool TryDispatchPawnForRole(Pawn pawn, ShopStaffRoleDef role)
        {
            if (role.workGivers.NullOrEmpty()) return false;

            for (int i = 0; i < role.workGivers.Count; i++)
            {
                WorkGiverDef workGiverDef = role.workGivers[i];
                if (workGiverDef?.defName == "RestockMegaStorage"
                    || workGiverDef?.defName == "RestockUniqueGoodsContainer")
                    continue;
                WorkGiver worker = workGiverDef?.Worker;
                if (worker == null || worker.ShouldSkip(pawn, false)) continue;

                Job nonScanJob = worker.NonScanJob(pawn);
                if (nonScanJob != null)
                    return TryStartAutomaticMechJob(pawn, nonScanJob);

                WorkGiver_Scanner scanner = worker as WorkGiver_Scanner;
                if (scanner == null || workGiverDef.scanThings == false) continue;
                Job job = TryMakeJobFromScanner(pawn, scanner);
                if (job == null) continue;
                return TryStartAutomaticMechJob(pawn, job);
            }

            return false;
        }

        //以普通自动工作方式启动机械体 Job，避免使用 TryTakeOrderedJob 强设 playerForced 或受 Shift 键干扰。
        private static bool TryStartAutomaticMechJob(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Drafted || pawn.jobs == null)
                return false;
            pawn.jobs.StartJob(
                job,
                JobCondition.InterruptForced,
                null,
                resumeCurJobAfterwards: false,
                cancelBusyStances: true,
                thinkTree: null,
                tag: JobTag.MiscWork,
                fromQueue: false,
                canReturnCurJobToPool: false,
                keepCarryingThingOverride: null,
                continueSleeping: false,
                addToJobsThisTick: true,
                preToilReservationsCanFail: true);
            return pawn.CurJob == job;
        }

        //使用已有 WorkGiver 扫描器生成 Job，复用可达性、预约和店铺权限判断。
        private static Job TryMakeJobFromScanner(Pawn pawn, WorkGiver_Scanner scanner)
        {
            IEnumerable<Thing> things = scanner.PotentialWorkThingsGlobal(pawn);
            if (things == null) return null;

            foreach (Thing thing in things)
            {
                if (thing == null || thing.Destroyed) continue;
                if (!scanner.HasJobOnThing(pawn, thing, false)) continue;
                Job job = scanner.JobOnThing(pawn, thing, false);
                if (job != null)
                    return job;
            }

            return null;
        }
    }
}
