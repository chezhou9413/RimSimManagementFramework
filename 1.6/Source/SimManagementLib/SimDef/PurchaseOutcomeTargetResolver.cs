using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimDef
{
    internal static class PurchaseOutcomeTargetResolver
    {
        public static bool TryResolveTargets(
            Pawn customer,
            Zone_Shop shopZone,
            PostPurchaseTargetMode mode,
            out LocalTargetInfo targetA,
            out LocalTargetInfo targetB)
        {
            targetA = LocalTargetInfo.Invalid;
            targetB = LocalTargetInfo.Invalid;

            if (customer == null || customer.Map == null)
                return false;
            if (shopZone == null || shopZone.Map != customer.Map)
                return false;

            switch (mode)
            {
                case PostPurchaseTargetMode.None:
                    return false;
                case PostPurchaseTargetMode.ShopCenterCell:
                {
                    List<IntVec3> centerCells = shopZone.Cells?.ToList();
                    if (centerCells.NullOrEmpty())
                        return false;

                    IntVec3 picked = centerCells[centerCells.Count / 2];
                    targetA = picked;
                    return targetA.IsValid;
                }
                case PostPurchaseTargetMode.RandomStandableCellInShop:
                    if (TryFindRandomCellInShop(customer, shopZone, out IntVec3 randomCell))
                    {
                        targetA = randomCell;
                        return true;
                    }
                    return false;
                case PostPurchaseTargetMode.NearestChair:
                    if (TryFindNearestBuilding(customer, shopZone, IsChair, out Building chair))
                    {
                        targetA = chair;
                        return true;
                    }
                    return false;
                case PostPurchaseTargetMode.NearestTable:
                    if (TryFindNearestBuilding(customer, shopZone, IsTableLike, out Building table))
                    {
                        targetA = table;
                        return true;
                    }
                    return false;
                case PostPurchaseTargetMode.DiningSpot:
                    return TryFindDiningTargets(customer, shopZone, out targetA, out targetB);
                default:
                    return false;
            }
        }

        public static bool TryFindDiningTargets(
            Pawn customer,
            Zone_Shop shopZone,
            out LocalTargetInfo seatTarget,
            out LocalTargetInfo tableTarget)
        {
            seatTarget = LocalTargetInfo.Invalid;
            tableTarget = LocalTargetInfo.Invalid;

            if (customer == null || customer.Map == null || shopZone == null || shopZone.Map != customer.Map)
                return false;

            if (TryFindNearestBuilding(customer, shopZone, IsChair, out Building chair))
            {
                IntVec3 seatCell = chair.InteractionCell;
                if (!seatCell.IsValid || !seatCell.InBounds(customer.Map) || !seatCell.Standable(customer.Map))
                    seatCell = chair.Position;

                if (seatCell.InBounds(customer.Map)
                    && seatCell.Standable(customer.Map)
                    && customer.CanReach(seatCell, PathEndMode.OnCell, Danger.Deadly))
                {
                    seatTarget = seatCell;
                }
            }

            if (TryFindNearestBuilding(customer, shopZone, IsTableLike, out Building table))
                tableTarget = table;

            // 店里没有座位时，退化到可站立点，避免直接丢失后续行为。
            if (!seatTarget.IsValid && TryFindRandomCellInShop(customer, shopZone, out IntVec3 randomCell))
                seatTarget = randomCell;

            return seatTarget.IsValid;
        }

        private static bool TryFindNearestBuilding(Pawn customer, Zone_Shop shopZone, Func<ThingDef, bool> predicate, out Building result)
        {
            result = null;
            if (customer == null || customer.Map == null || shopZone == null || predicate == null)
                return false;

            float bestDist = float.MaxValue;
            foreach (IntVec3 cell in shopZone.Cells)
            {
                List<Thing> things = customer.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (!(things[i] is Building building))
                        continue;
                    if (building.Destroyed || !building.Spawned)
                        continue;
                    if (!predicate(building.def))
                        continue;
                    if (!customer.CanReach(building, PathEndMode.Touch, Danger.Deadly))
                        continue;

                    float dist = (building.Position - customer.Position).LengthHorizontalSquared;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        result = building;
                    }
                }
            }

            return result != null;
        }

        private static bool TryFindRandomCellInShop(Pawn customer, Zone_Shop shopZone, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (customer == null || customer.Map == null || shopZone == null)
                return false;

            List<IntVec3> cells = shopZone.Cells?.ToList();
            if (cells.NullOrEmpty())
                return false;

            cells.Shuffle();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(customer.Map))
                    continue;
                if (!cell.Standable(customer.Map))
                    continue;
                if (!customer.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    continue;

                result = cell;
                return true;
            }

            return false;
        }

        private static bool IsChair(ThingDef def)
        {
            return def != null && def.building != null && def.building.isSittable;
        }

        private static bool IsTableLike(ThingDef def)
        {
            if (def == null || def.building == null)
                return false;
            if (def.defName != null && def.defName.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return def.surfaceType == SurfaceType.Item && (def.Size.x > 1 || def.Size.z > 1);
        }
    }
}
