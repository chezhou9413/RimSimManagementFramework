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
        private const int DispatchIntervalTicks = 121;
        private const int PawnScanSalt = 37;
        private static readonly HashSet<string> IdleJobDefNames = new HashSet<string>
        {
            "Wait",
            "Wait_MaintainPosture"
        };

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
            List<Pawn> mechs = map.mapPawns.SpawnedColonyMechs;
            for (int i = 0; i < mechs.Count; i++)
            {
                Pawn pawn = mechs[i];
                if (!ShouldTryDispatch(pawn, now)) continue;
                TryDispatchPawn(pawn);
            }
        }

        //函数职责：判断机械体当前是否适合尝试店员派工。
        private static bool ShouldTryDispatch(Pawn pawn, int now)
        {
            if (!ShopStaffUtility.IsAssignableMechanicalStaff(pawn)) return false;
            if (pawn.jobs == null || !IsIdleForShopDispatch(pawn)) return false;
            int offset = pawn.thingIDNumber >= 0 ? pawn.thingIDNumber % DispatchIntervalTicks : 0;
            return (now + PawnScanSalt + offset) % DispatchIntervalTicks == 0;
        }

        //函数职责：判断机械体是否只处于空闲等待状态，避免派工打断已有实际任务。
        private static bool IsIdleForShopDispatch(Pawn pawn)
        {
            if (pawn?.CurJob == null) return true;
            string defName = pawn.CurJobDef?.defName;
            return !string.IsNullOrEmpty(defName) && IdleJobDefNames.Contains(defName);
        }

        //函数职责：为单个机械体按已分配岗位寻找可执行工作。
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

        //函数职责：返回机械体在当前地图中被分配到岗位的商店。
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

        //函数职责：判断机械体是否在商店任意可见岗位中。
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

        //函数职责：在指定商店内按岗位顺序尝试为机械体生成工作。
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

        //函数职责：按岗位绑定的 WorkGiver 顺序尝试创建一个实际 Job。
        private static bool TryDispatchPawnForRole(Pawn pawn, ShopStaffRoleDef role)
        {
            if (role.workGivers.NullOrEmpty()) return false;

            for (int i = 0; i < role.workGivers.Count; i++)
            {
                WorkGiverDef workGiverDef = role.workGivers[i];
                WorkGiver worker = workGiverDef?.Worker;
                if (worker == null) continue;

                Job nonScanJob = worker.NonScanJob(pawn);
                if (nonScanJob != null)
                    return pawn.jobs.TryTakeOrderedJob(nonScanJob, JobTag.MiscWork);

                WorkGiver_Scanner scanner = worker as WorkGiver_Scanner;
                if (scanner == null || workGiverDef.scanThings == false) continue;
                Job job = TryMakeJobFromScanner(pawn, scanner);
                if (job == null) continue;
                return pawn.jobs.TryTakeOrderedJob(job, JobTag.MiscWork);
            }

            return false;
        }

        //函数职责：使用已有 WorkGiver 扫描器生成 Job，并复用原有可达性、预约和店铺权限判断。
        private static Job TryMakeJobFromScanner(Pawn pawn, WorkGiver_Scanner scanner)
        {
            IEnumerable<Thing> things = scanner.PotentialWorkThingsGlobal(pawn);
            if (things == null) return null;

            foreach (Thing thing in things.Where(t => t != null && !t.Destroyed))
            {
                if (!scanner.HasJobOnThing(pawn, thing, false)) continue;
                Job job = scanner.JobOnThing(pawn, thing, false);
                if (job != null)
                    return job;
            }

            return null;
        }
    }
}
