using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理顾客前往当前目标商店的旅行阶段，负责支持跨店后动态更新目标。
    /// </summary>
    public class LordToil_CustomerTravel : LordToil
    {
        public override IntVec3 FlagLoc
        {
            get
            {
                Pawn pawn = FirstActivePawn();
                LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
                return ResolveTravelTargetCell(pawn, visit);
            }
        }

        public override bool AllowSatisfyLongNeeds => false;

        /// <summary>
        /// 给顾客分配前往当前目标商店的职责。
        /// </summary>
        public override void UpdateAllDuties()
        {
            LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                IntVec3 dest = ResolveTravelTargetCell(pawn, visit);
                PawnDuty duty = new PawnDuty(DutyDefOf.TravelOrLeave, dest)
                {
                    maxDanger = Danger.Deadly,
                    locomotion = LocomotionUrgency.Sprint
                };
                pawn.mindState.duty = duty;
            }
        }

        /// <summary>
        /// 周期性检查顾客是否到达当前目标店，负责推进到浏览阶段。
        /// </summary>
        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 205 != 0) return;
            LordJob_CustomerVisit visit = lord?.LordJob as LordJob_CustomerVisit;
            if (visit == null) return;

            bool allArrived = true;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                if (!HasArrivedAtCurrentShop(pawn, visit))
                {
                    allArrived = false;
                    break;
                }
            }

            if (allArrived)
            {
                for (int i = 0; i < lord.ownedPawns.Count; i++)
                {
                    Pawn pawn = lord.ownedPawns[i];
                    if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                    visit.GetOrCreateSession(pawn)?.NotifyArrived(visit, pawn);
                }
                lord.ReceiveMemo("TravelArrived");
            }
        }

        /// <summary>
        /// 返回当前活跃顾客，负责为旗帜位置提供目标查询对象。
        /// </summary>
        private Pawn FirstActivePawn()
        {
            return (lord?.LordJob as LordJob_CustomerVisit)?.FirstActivePawn();
        }

        /// <summary>
        /// 返回顾客进入商店的真实旅行目标，负责优先选择可到达的货架或店内站立格。
        /// </summary>
        private static IntVec3 ResolveTravelTargetCell(Pawn pawn, LordJob_CustomerVisit visit)
        {
            if (pawn?.Map == null || visit == null) return IntVec3.Invalid;
            Zone_Shop shop = visit.GetCurrentShop(pawn);
            if (shop == null)
                return visit.GetCurrentShopCell(pawn);

            if (TryFindReachableStorageCell(pawn, shop, out IntVec3 storageCell))
                return storageCell;
            if (TryFindReachableShopCell(pawn, shop, out IntVec3 shopCell))
                return shopCell;
            return visit.GetCurrentShopCell(pawn);
        }

        /// <summary>
        /// 判断顾客是否已经到达当前商店，负责避免动态目标格或货柜交互格导致顾客长期卡在旅行阶段。
        /// </summary>
        private static bool HasArrivedAtCurrentShop(Pawn pawn, LordJob_CustomerVisit visit)
        {
            if (pawn?.Map == null || visit == null) return false;
            Zone_Shop shop = visit.GetCurrentShop(pawn);
            if (shop == null) return false;

            if (shop.Cells.Contains(pawn.Position))
                return true;

            List<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(shop)
                .Where(item => item != null && !item.Destroyed && item.Spawned)
                .Where(item => pawn.CanReach(item, PathEndMode.Touch, Danger.Deadly))
                .ToList();
            for (int i = 0; i < storages.Count; i++)
            {
                Building_SimContainer storage = storages[i];
                if (storage.OccupiedRect().ExpandedBy(1).Contains(pawn.Position))
                    return true;
            }

            IntVec3 dest = ResolveTravelTargetCell(pawn, visit);
            return dest.IsValid && pawn.Position == dest;
        }

        /// <summary>
        /// 查找可到达货架旁的旅行目标格，负责让顾客优先真正进到商品附近。
        /// </summary>
        private static bool TryFindReachableStorageCell(Pawn pawn, Zone_Shop shop, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            List<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(shop)
                .Where(item => item != null && !item.Destroyed && item.Spawned)
                .Where(item => pawn.CanReach(item, PathEndMode.Touch, Danger.Deadly))
                .ToList();
            if (storages.NullOrEmpty()) return false;

            Building_SimContainer selectedStorage = storages
                .OrderBy(item => (item.Position - pawn.Position).LengthHorizontalSquared)
                .FirstOrDefault();
            cell = selectedStorage?.InteractionCell ?? IntVec3.Invalid;
            if (cell.IsValid && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                return true;

            cell = IntVec3.Invalid;
            return false;
        }

        /// <summary>
        /// 查找商店区内可到达站立格，负责为空店或无货架商店提供进入目标。
        /// </summary>
        private static bool TryFindReachableShopCell(Pawn pawn, Zone_Shop shop, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            List<IntVec3> cells = shop.Cells
                .Where(c => c.IsValid && c.Standable(pawn.Map))
                .Where(c => pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(c => (c - pawn.Position).LengthHorizontalSquared)
                .ToList();
            if (cells.NullOrEmpty()) return false;

            cell = cells[0];
            return true;
        }
    }
}
