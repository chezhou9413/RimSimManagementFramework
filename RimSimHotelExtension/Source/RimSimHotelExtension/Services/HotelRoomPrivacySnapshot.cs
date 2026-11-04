using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.Models;
using Verse;

namespace RimSimHotelExtension.Services
{
    //主线程建立后不再修改，异步寻路只读取房间和门口的编号。
    internal sealed class HotelRoomPrivacySnapshot
    {
        internal static readonly HotelRoomPrivacySnapshot Empty = new HotelRoomPrivacySnapshot();
        private readonly Dictionary<int, int> guestRooms = new Dictionary<int, int>();
        private readonly Dictionary<IntVec3, int> roomCells = new Dictionary<IntVec3, int>();
        private readonly Dictionary<IntVec3, int[]> doorRooms = new Dictionary<IntVec3, int[]>();

        //权限由完整快照发布，不把可变的 Room 或合同对象交给寻路线程。
        private HotelRoomPrivacySnapshot() { }

        //登记客房即保留私密性，空房也不是普通顾客的闲逛空间。
        internal static HotelRoomPrivacySnapshot Build(Map map, IReadOnlyList<HotelShopSettings> settings, IReadOnlyList<HotelStay> stays)
        {
            var result = new HotelRoomPrivacySnapshot();
            var rooms = new Dictionary<int, Room>();
            foreach (var config in settings)
                foreach (var bed in config.registeredBeds) AddBedroom(bed);
            foreach (var stay in stays)
            {
                if (stay.completed) continue;
                Room room = AddBedroom(stay.bed);
                foreach (var guest in stay.guests)
                    if (guest.pawn != null && guest.pawn.Spawned && guest.pawn.Map == map)
                        result.guestRooms[guest.pawn.thingIDNumber] = room?.ID ?? -1;
            }
            if (rooms.Count == 0) return Empty;
            foreach (var room in rooms.Values)
                foreach (var cell in room.Cells) result.roomCells[cell] = room.ID;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (thing is not Building_Door door) continue;
                var adjoining = new HashSet<int>();
                foreach (var cell in door.OccupiedRect())
                    foreach (var offset in GenAdj.CardinalDirections)
                        if (result.roomCells.TryGetValue(cell + offset, out int roomId)) adjoining.Add(roomId);
                if (adjoining.Count == 0) continue;
                int[] roomIds = adjoining.ToArray();
                foreach (var cell in door.OccupiedRect()) result.doorRooms[cell] = roomIds;
            }
            return result;

            //露天床不封锁整个地图，合同中的床仍保留对应房间权限。
            Room AddBedroom(Building_Bed bed)
            {
                Room room = bed != null && bed.Spawned && bed.Map == map ? bed.GetRoom() : null;
                if (room != null && !room.TouchesMapEdge) rooms[room.ID] = room;
                return room;
            }
        }

        //没有住宿合同的购物、用餐和温泉顾客没有任何客房通行权。
        private int OwnRoom(int pawnId) => guestRooms.TryGetValue(pawnId, out int room) ? room : -1;

        //同住一间客房的所有床位和同行者均可活动，其他顾客不能选这里作目的地。
        internal bool BlocksDestination(int pawnId, IntVec3 cell)
            => roomCells.TryGetValue(cell, out int targetRoom) && targetRoom != OwnRoom(pawnId);

        //已经在别人房间内的住客仍能走到门外，避免入住变化把人困住。
        internal bool BlocksDoor(int pawnId, IntVec3 from, IntVec3 door, int candidateRoom = -1)
        {
            if (!doorRooms.TryGetValue(door, out var rooms)) return false;
            int ownRoom = OwnRoom(pawnId);
            int fromRoom = roomCells.TryGetValue(from, out int room) ? room : -1;
            bool leavingDoor = doorRooms.TryGetValue(from, out var fromRooms) && ReferenceEquals(rooms, fromRooms);
            foreach (int roomId in rooms)
                if (roomId != ownRoom && roomId != candidateRoom && roomId != fromRoom && !leavingDoor) return true;
            return false;
        }

        //旧路径也必须遵守当前权限，只允许误入者在原房间内移动并离开。
        internal bool BlocksStep(int pawnId, IntVec3 from, IntVec3 to)
        {
            if (roomCells.TryGetValue(to, out int targetRoom) && targetRoom != OwnRoom(pawnId))
                return !roomCells.TryGetValue(from, out int fromRoom) || fromRoom != targetRoom;
            return BlocksDoor(pawnId, from, to);
        }
    }
}
