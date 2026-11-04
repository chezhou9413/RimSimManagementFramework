using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //在自己的客房和旅馆公共空间安排短暂散步或桌边休息。
    public static class HotelLeisureUtility
    {
        //休闲仅使用旅馆范围内的空位，不取用商品，也不占其他客房和前台通道。
        public static Job MakeJob(Pawn pawn, HotelStay stay, HotelGuestState guest)
        {
            var manager = HotelMapComponent.For(pawn.Map);
            Zone_Shop shop = manager.ShopFor(stay);
            if (shop == null || stay.bed?.Spawned != true) return null;
            var desk = manager.DeskFor(shop);
            IntVec3 serviceCell = desk == null ? IntVec3.Invalid : CheckoutQueueCellUtility.FindServiceCell(desk, pawn);
            Room bedroom = stay.bed.GetRoom();
            var cells = new List<IntVec3>(64);
            var shopCells = shop.Cells;
            int start = shopCells.Count == 0 ? 0 : Rand.Range(0, shopCells.Count);
            //区域内抽样并检查身边空位，休闲搜索不随旅馆面积增长。
            for (int i = 0, count = System.Math.Min(32, shopCells.Count); i < count; i++)
            {
                IntVec3 cell = shopCells[(start + i) % shopCells.Count];
                if (IsCandidate(cell)) cells.Add(cell);
            }
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 3f, false))
                if (!cells.Contains(cell) && IsCandidate(cell)) cells.Add(cell);
            cells.Shuffle();

            //候选位置始终属于本店，并保留房间、预约和前台通道限制。
            bool IsCandidate(IntVec3 cell) => cell.InBounds(pawn.Map)
                && pawn.Map.zoneManager.ZoneAt(cell) == shop
                && (desk == null || cell != desk.InteractionCell)
                && IsLeisureCell(cell, pawn, bedroom, serviceCell);

            if (Rand.Chance(0.5f))
            {
                foreach (IntVec3 cell in cells)
                {
                    Building chair = cell.GetEdifice(pawn.Map);
                    if (chair?.def.building?.isSittable != true) continue;
                    var throne = chair.TryGetComp<CompAssignableToPawn_Throne>();
                    if (throne != null && !throne.AssignedPawns.Contains(pawn)) continue;
                    foreach (IntVec3 direction in GenAdj.CardinalDirections)
                    {
                        IntVec3 tableCell = cell + direction;
                        if (!tableCell.InBounds(pawn.Map) || pawn.Map.zoneManager.ZoneAt(tableCell) != shop) continue;
                        Building table = tableCell.GetEdifice(pawn.Map);
                        if (table?.def.surfaceType != SurfaceType.Eat
                            || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                        guest.status = "RSH.Text.RelaxingAtTheHotel";
                        return MakeLeisureJob(cell, table);
                    }
                }
            }

            foreach (IntVec3 cell in cells)
            {
                if (cell == pawn.Position || cell.GetEdifice(pawn.Map) != null
                    || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                guest.status = "RSH.Text.WalkingAroundTheHotel";
                return MakeLeisureJob(cell, null);
            }
            return null;
        }

        //避开危险、门口、排队格及其他人的卧室。
        private static bool IsLeisureCell(IntVec3 cell, Pawn pawn, Room bedroom, IntVec3 serviceCell)
        {
            if (cell.Fogged(pawn.Map) || cell.GetTerrain(pawn.Map).avoidWander
                || PawnUtility.KnownDangerAt(cell, pawn.Map, pawn)
                || (serviceCell.IsValid && cell.DistanceToSquared(serviceCell) <= 4)
                || !CheckoutQueueCellUtility.IsWaitingCellUsable(cell, pawn.Map, pawn, serviceCell)) return false;
            Building building = cell.GetEdifice(pawn.Map);
            if (building != null && building.def.building?.isSittable != true) return false;
            Room room = cell.GetRoom(pawn.Map);
            return room != null && (room == bedroom || !room.ContainedBeds.Any());
        }

        //步行到目的地后停留片刻，并设置期限让生活调度继续检查基本需求。
        private static Job MakeLeisureJob(IntVec3 cell, Thing table)
        {
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_Relax"), cell);
            if (table != null) job.SetTarget(TargetIndex.B, table);
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = 900;
            return job;
        }
    }
}
