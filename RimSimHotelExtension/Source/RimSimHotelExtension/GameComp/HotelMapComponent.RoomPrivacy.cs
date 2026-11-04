using RimSimHotelExtension.Services;
using Verse;

namespace RimSimHotelExtension.GameComp
{
    //床位登记、合同和建筑变化时发布客房权限，日常寻路不扫描房间或住客列表。
    public sealed partial class HotelMapComponent
    {
        private volatile HotelRoomPrivacySnapshot roomPrivacy = HotelRoomPrivacySnapshot.Empty;
        private bool roomPrivacyDirty = true;
        private int roomPrivacyBuildingsHash;
        internal HotelRoomPrivacySnapshot RoomPrivacy => roomPrivacy;

        //房间系统初始化完成后才能恢复门口权限。
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RefreshRoomPrivacy();
        }

        //房间查询和集合构造只发生在主线程，完整结果一次性替换。
        private void RefreshRoomPrivacy()
        {
            int buildingsHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.BuildingArtificial);
            if (!roomPrivacyDirty && buildingsHash == roomPrivacyBuildingsHash) return;
            var previous = roomPrivacy;
            roomPrivacy = settings.Count == 0 && activeStays.Count == 0
                ? HotelRoomPrivacySnapshot.Empty : HotelRoomPrivacySnapshot.Build(map, settings, activeStays);
            if (!ReferenceEquals(previous, roomPrivacy)) map.reachability.ClearCache();
            roomPrivacyBuildingsHash = buildingsHash;
            roomPrivacyDirty = false;
        }
    }
}
