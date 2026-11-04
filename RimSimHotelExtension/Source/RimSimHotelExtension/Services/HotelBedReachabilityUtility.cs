using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //订房和换房只检查候选房间的通路，不提前授予实际移动权限。
    internal static class HotelBedReachabilityUtility
    {
        [ThreadStatic] private static Pawn candidatePawn;
        [ThreadStatic] private static int candidateRoom;

        //临时权限仅对同步检查所在的线程和顾客生效，异步寻路不可见。
        internal static int CandidateRoomFor(Pawn pawn) => candidatePawn == pawn ? candidateRoom : -1;

        //候选客房可以进入，途经其他私人客房仍然禁止；检查结果不污染普通可达性缓存。
        internal static bool CanReach(Pawn pawn, Building_Bed bed)
        {
            Map map = pawn.Map;
            int roomId = bed.GetRoom()?.ID ?? -1;
            Pawn previousPawn = candidatePawn;
            int previousRoom = candidateRoom;
            map.reachability.ClearCacheFor(pawn);
            try
            {
                candidatePawn = pawn;
                candidateRoom = roomId;
                return pawn.CanReach(bed, PathEndMode.OnCell, Danger.Some);
            }
            finally
            {
                candidatePawn = previousPawn;
                candidateRoom = previousRoom;
                map.reachability.ClearCacheFor(pawn);
            }
        }
    }
}
