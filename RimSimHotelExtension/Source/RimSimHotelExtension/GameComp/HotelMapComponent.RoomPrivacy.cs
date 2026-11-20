using System.Collections.Concurrent;
using RimSimHotelExtension.Services;
using Verse;

namespace RimSimHotelExtension.GameComp
{
    //床位登记、合同和建筑变化时发布客房权限，日常与异步寻路直接按地图编号读取不可变快照。
    public sealed partial class HotelMapComponent
    {
        private static readonly ConcurrentDictionary<int, HotelRoomPrivacySnapshot> SnapshotsByMap
            = new ConcurrentDictionary<int, HotelRoomPrivacySnapshot>();

        private volatile HotelRoomPrivacySnapshot roomPrivacy = HotelRoomPrivacySnapshot.Empty;
        private bool roomPrivacyDirty = true;
        private int roomPrivacyBuildingsHash;
        internal HotelRoomPrivacySnapshot RoomPrivacy => roomPrivacy;

        //供主线程和 1.7 异步寻路线程无锁读取指定地图的客房权限快照，避免热路径线性扫描 MapComponent。
        internal static HotelRoomPrivacySnapshot SnapshotForMap(Map targetMap)
        {
            if (targetMap == null) return HotelRoomPrivacySnapshot.Empty;
            return SnapshotsByMap.TryGetValue(targetMap.uniqueID, out HotelRoomPrivacySnapshot snapshot) && snapshot != null
                ? snapshot : HotelRoomPrivacySnapshot.Empty;
        }

        //房间系统初始化完成后恢复床位索引与门口权限。
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RebuildRegisteredBedIndex();
            RefreshRoomPrivacy();
        }

        //地图卸载时清理静态快照引用，避免残留旧地图数据。
        public override void MapRemoved()
        {
            base.MapRemoved();
            if (map != null) SnapshotsByMap.TryRemove(map.uniqueID, out _);
        }

        //房间查询和集合构造只发生在主线程，完整结果一次性替换。
        private void RefreshRoomPrivacy()
        {
            int buildingsHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.BuildingArtificial);
            if (!roomPrivacyDirty && buildingsHash == roomPrivacyBuildingsHash) return;
            var previous = roomPrivacy;
            roomPrivacy = settings.Count == 0 && activeStays.Count == 0
                ? HotelRoomPrivacySnapshot.Empty : HotelRoomPrivacySnapshot.Build(map, settings, activeStays);
            if (roomPrivacy.IsEmpty) SnapshotsByMap.TryRemove(map.uniqueID, out _);
            else SnapshotsByMap[map.uniqueID] = roomPrivacy;
            if (!ReferenceEquals(previous, roomPrivacy)) map.reachability.ClearCache();
            roomPrivacyBuildingsHash = buildingsHash;
            roomPrivacyDirty = false;
        }
    }
}
