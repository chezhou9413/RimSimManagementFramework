using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Jobs;
using RimSimHotelExtension.Models;
using RimWorld;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //在自己的客房和旅馆公共空间安排入房安顿、就座小憩、欣赏陈设与室内漫步。
    public static class HotelLeisureUtility
    {
        //刚办完入住后优先前往自己预订的客房安顿，营造办卡入房的住店节奏。
        public static Job MakeSettleInRoomJob(Pawn pawn, HotelStay stay, HotelGuestState guest)
        {
            if (pawn?.Map == null || stay?.bed?.Spawned != true) return null;
            Room bedroom = stay.bed.GetRoom();
            var manager = HotelMapComponent.For(pawn.Map);
            Zone_Shop shop = manager.ShopFor(stay);
            var desk = manager.DeskFor(shop);
            IntVec3 serviceCell = desk == null ? IntVec3.Invalid : CheckoutQueueCellUtility.FindServiceCell(desk, pawn);

            if (bedroom != null && !bedroom.TouchesMapEdge)
            {
                foreach (IntVec3 cell in bedroom.Cells.InRandomOrder())
                {
                    if (!IsRoomSeatOrStandCell(cell, pawn, serviceCell, desk)) continue;
                    Building chair = cell.GetEdifice(pawn.Map);
                    if (chair?.def.building?.isSittable != true || !CanUseSeat(pawn, chair)) continue;
                    if (!pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                    guest.status = "RSH.Text.SettlingIntoRoom";
                    return MakeLeisureJob(cell, stay.bed, JobDriver_HotelRelax.ModeSettleInRoom);
                }
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(stay.bed.Position, 3f, true))
            {
                if (!IsRoomSeatOrStandCell(cell, pawn, serviceCell, desk)) continue;
                if (bedroom != null && cell.GetRoom(pawn.Map) != bedroom) continue;
                if (cell.GetEdifice(pawn.Map) != null) continue;
                if (!pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                guest.status = "RSH.Text.SettlingIntoRoom";
                return MakeLeisureJob(cell, stay.bed, JobDriver_HotelRelax.ModeSettleInRoom);
            }

            guest.roomSettled = true;
            return null;
        }

        //休闲仅使用旅馆公共区和自己客房的空位，支持就座、赏景、同行聚拢和闲逛。
        public static Job MakeJob(Pawn pawn, HotelStay stay, HotelGuestState guest)
        {
            var manager = HotelMapComponent.For(pawn.Map);
            Zone_Shop shop = manager.ShopFor(stay);
            if (shop == null || stay.bed?.Spawned != true) return null;
            var desk = manager.DeskFor(shop);
            IntVec3 serviceCell = desk == null ? IntVec3.Invalid : CheckoutQueueCellUtility.FindServiceCell(desk, pawn);
            Room bedroom = stay.bed.GetRoom();
            var cells = CollectCandidateCells(pawn, shop, stay, bedroom, desk, serviceCell);
            if (cells.Count == 0) return null;

            //优先在桌边、沙发或扶手椅坐下休息，营造大堂与客房的起居氛围。
            if (Rand.Chance(0.62f))
            {
                Job seatJob = TryMakeSeatJob(pawn, shop, bedroom, guest, cells);
                if (seatJob != null) return seatJob;
            }

            //其次驻足欣赏旅馆中的雕塑、绿植或休闲摆设。
            if (Rand.Chance(0.45f))
            {
                Job decorJob = TryMakeDecorJob(pawn, guest, cells);
                if (decorJob != null) return decorJob;
            }

            foreach (IntVec3 cell in cells)
            {
                if (cell == pawn.Position || cell.GetEdifice(pawn.Map) != null
                    || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                guest.status = "RSH.Text.WalkingAroundTheHotel";
                return MakeLeisureJob(cell, null, JobDriver_HotelRelax.ModeWalk);
            }
            return null;
        }

        //收集客房、大堂与同行者附近的候选格，保持常数级抽样开销。
        private static List<IntVec3> CollectCandidateCells(Pawn pawn, Zone_Shop shop, HotelStay stay,
            Room bedroom, Building desk, IntVec3 serviceCell)
        {
            var cells = new List<IntVec3>(64);
            var shopCells = shop.Cells;
            int start = shopCells.Count == 0 ? 0 : Rand.Range(0, shopCells.Count);
            for (int i = 0, count = System.Math.Min(40, shopCells.Count); i < count; i++)
            {
                IntVec3 cell = shopCells[(start + i) % shopCells.Count];
                if (IsCandidate(cell)) cells.Add(cell);
            }

            //抽样自己的客房与同行伙伴附近，让双人住客更常在同房间活动。
            Pawn companion = stay.guests.Select(g => g.pawn).FirstOrDefault(p => p != null && p != pawn
                && p.Spawned && p.Map == pawn.Map && shop.ContainsCell(p.Position));
            if (companion != null)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(companion.Position, 3f, false))
                    if (!cells.Contains(cell) && IsCandidate(cell)) cells.Add(cell);
            }
            if (bedroom != null && !bedroom.TouchesMapEdge)
            {
                int taken = 0;
                foreach (IntVec3 cell in bedroom.Cells.InRandomOrder())
                {
                    if (taken >= 10) break;
                    if (!cells.Contains(cell) && IsCandidate(cell)) { cells.Add(cell); taken++; }
                }
            }
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 3f, false))
                if (!cells.Contains(cell) && IsCandidate(cell)) cells.Add(cell);
            cells.Shuffle();
            return cells;

            bool IsCandidate(IntVec3 cell) => cell.InBounds(pawn.Map)
                && pawn.Map.zoneManager.ZoneAt(cell) == shop
                && (desk == null || cell != desk.InteractionCell)
                && IsLeisureCell(cell, pawn, bedroom, serviceCell);
        }

        //选择桌边椅或独立扶手椅/沙发，区分客房内休息与大堂休憩。
        private static Job TryMakeSeatJob(Pawn pawn, Zone_Shop shop, Room bedroom, HotelGuestState guest, List<IntVec3> cells)
        {
            IntVec3 fallbackSeat = IntVec3.Invalid;
            Thing fallbackFocus = null;
            int fallbackMode = JobDriver_HotelRelax.ModeLobbySeat;

            foreach (IntVec3 cell in cells)
            {
                Building chair = cell.GetEdifice(pawn.Map);
                if (chair?.def.building?.isSittable != true || !CanUseSeat(pawn, chair)) continue;
                bool inOwnRoom = bedroom != null && cell.GetRoom(pawn.Map) == bedroom;
                int mode = inOwnRoom ? JobDriver_HotelRelax.ModeRoomSeat : JobDriver_HotelRelax.ModeLobbySeat;

                Thing focus = FindNearbyInterest(pawn.Map, shop, cell);
                if (focus != null && pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None))
                {
                    guest.status = inOwnRoom ? "RSH.Text.RelaxingInOwnRoom" : "RSH.Text.RelaxingInLobby";
                    return MakeLeisureJob(cell, focus, mode);
                }
                if (!fallbackSeat.IsValid && pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None))
                {
                    fallbackSeat = cell;
                    fallbackFocus = chair;
                    fallbackMode = mode;
                }
            }

            if (!fallbackSeat.IsValid) return null;
            guest.status = fallbackMode == JobDriver_HotelRelax.ModeRoomSeat
                ? "RSH.Text.RelaxingInOwnRoom" : "RSH.Text.RelaxingInLobby";
            return MakeLeisureJob(fallbackSeat, fallbackFocus, fallbackMode);
        }

        //在旅馆公共区或自己客房寻找雕塑、高美观绿植或娱乐设施供住客驻足观赏。
        private static Job TryMakeDecorJob(Pawn pawn, HotelGuestState guest, List<IntVec3> cells)
        {
            foreach (IntVec3 cell in cells)
            {
                if (cell.GetEdifice(pawn.Map) != null) continue;
                Thing decor = FindNearbyDecor(pawn.Map, cell);
                if (decor == null || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                guest.status = "RSH.Text.AdmiringHotelDecor";
                return MakeLeisureJob(cell, decor, JobDriver_HotelRelax.ModeAdmireDecor);
            }
            return null;
        }

        //查找座椅旁的餐桌、电视或娱乐设施作为朝向目标。
        private static Thing FindNearbyInterest(Map map, Zone_Shop shop, IntVec3 seatCell)
        {
            foreach (IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 adjacent = seatCell + dir;
                if (!adjacent.InBounds(map) || map.zoneManager.ZoneAt(adjacent) != shop) continue;
                Building b = adjacent.GetEdifice(map);
                if (b == null) continue;
                if (b.def.surfaceType == SurfaceType.Eat || b.def.building?.joyKind != null) return b;
            }
            return null;
        }

        //查找紧邻格内的高美观艺术品、盆栽或娱乐摆设。
        private static Thing FindNearbyDecor(Map map, IntVec3 standCell)
        {
            foreach (IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 adjacent = standCell + dir;
                if (!adjacent.InBounds(map)) continue;
                List<Thing> things = adjacent.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t == null || t.Destroyed) continue;
                    if (t is Building_Art || t.def.building?.joyKind != null || t.GetStatValue(StatDefOf.Beauty) >= 6f)
                        return t;
                }
            }
            return null;
        }

        private static bool CanUseSeat(Pawn pawn, Building chair)
        {
            var throne = chair.TryGetComp<CompAssignableToPawn_Throne>();
            return throne == null || throne.AssignedPawns.Contains(pawn);
        }

        private static bool IsRoomSeatOrStandCell(IntVec3 cell, Pawn pawn, IntVec3 serviceCell, Building desk)
        {
            if (!cell.InBounds(pawn.Map) || cell.Fogged(pawn.Map) || cell.GetTerrain(pawn.Map).avoidWander) return false;
            if (desk != null && cell == desk.InteractionCell) return false;
            if (PawnUtility.KnownDangerAt(cell, pawn.Map, pawn)) return false;
            return CheckoutQueueCellUtility.IsWaitingCellUsable(cell, pawn.Map, pawn, serviceCell);
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
        private static Job MakeLeisureJob(IntVec3 cell, Thing focus, int mode)
        {
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_Relax"), cell);
            if (focus != null) job.SetTarget(TargetIndex.B, focus);
            job.count = mode;
            job.locomotionUrgency = LocomotionUrgency.Walk;
            job.expiryInterval = 950;
            return job;
        }
    }
}
