using System.Linq;
using RimWorld;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimHotelExtension.GameComp
{
    //所有原版床及沿用原版床逻辑的模组床共用出租登记和用途约束。
    public sealed partial class HotelMapComponent
    {
        //保留床型差异，体型和种族是否适配由入住时的原版检查决定。
        public static bool SupportsRentalBed(Building_Bed bed)
            => bed != null && bed.CompAssignableToPawn != null && bed.SleepingSlotsCount > 0;

        //从各旅馆配置重建已登记床位编号索引，使原版全图找床热路径保持 O(1) 零分配查询。
        internal void RebuildRegisteredBedIndex()
        {
            registeredBedIds.Clear();
            for (int i = 0; i < settings.Count; i++)
            {
                var list = settings[i]?.registeredBeds;
                if (list == null) continue;
                for (int j = list.Count - 1; j >= 0; j--)
                {
                    Building_Bed bed = list[j];
                    if (bed == null || bed.Destroyed)
                    {
                        list.RemoveAt(j);
                        continue;
                    }
                    registeredBedIds.Add(bed.thingIDNumber);
                }
            }
        }

        //只查询登记索引，不为状态显示或原版找床流程创建经营设置或 LINQ 分配。
        public bool IsRegisteredBed(Building_Bed bed)
            => bed != null && registeredBedIds.Contains(bed.thingIDNumber);

        //只读判断指定商店是否拥有已登记的在图床位，避免界面可见性判断每帧遍历全图建筑。
        public bool HasRegisteredBedInShop(Zone_Shop shop)
        {
            if (shop == null) return false;
            for (int i = 0; i < settings.Count; i++)
            {
                var config = settings[i];
                if (config == null || config.shopZoneId != shop.ID || config.registeredBeds == null) continue;
                for (int j = 0; j < config.registeredBeds.Count; j++)
                {
                    Building_Bed bed = config.registeredBeds[j];
                    if (bed != null && bed.Spawned && bed.Map == map && shop.ContainsCell(bed.Position))
                        return true;
                }
            }
            return false;
        }

        //预订和未完成退房的床都不能被改作其他用途。
        public bool IsBedBooked(Building_Bed bed)
        {
            if (bed == null) return false;
            if (arrivalBeds.Contains(bed.thingIDNumber)) return true;
            for (int i = 0; i < activeStays.Count; i++)
            {
                var stay = activeStays[i];
                if (!stay.completed && stay.bed == bed) return true;
            }
            return false;
        }

        //校验固定设施和床位状态，不把营业时间或暂时无人值班当成登记门槛。
        public string BedRegistrationIssue(Zone_Shop shop, Building_Bed bed, bool register)
        {
            bool alreadyRegistered = shop != null && settings.Any(s => s.shopZoneId == shop.ID && s.registeredBeds.Contains(bed));
            if (register && alreadyRegistered) return "";
            if (IsBedBooked(bed)) return HotelText.T("RSH.Bed.Booked");
            if (!register) return "";
            if (!SupportsRentalBed(bed) || !bed.Spawned || bed.Map != map || bed.Faction != Faction.OfPlayer)
                return HotelText.T("RSH.Bed.Unavailable");
            if (shop == null || shop.Map != map || !shop.ContainsCell(bed.Position))
                return HotelText.T("RSH.Bed.NeedsShop");
            if (DeskFor(shop) == null) return HotelText.T("RSH.Bed.NeedsDesk");
            if (bed.Medical) return HotelText.T("RSH.Bed.IsMedical");
            if (bed.ForPrisoners) return HotelText.T("RSH.Bed.IsPrisoner");
            if (bed.ForSlaves) return HotelText.T("RSH.Bed.IsSlave");
            if (bed.IsBurning()) return HotelText.T("RSH.Bed.IsBurning");
            if (bed.OwnersForReading.Count > 0) return HotelText.T("RSH.Bed.HasOwner");
            if (bed.CurOccupants.Any()) return HotelText.T("RSH.Bed.Occupied");
            return "";
        }

        //验证通过后才写入登记，并同步刷新相关旅馆的营业与来客快照。
        public bool RegisterBed(Zone_Shop shop, Building_Bed bed, bool register, out string reason)
        {
            reason = BedRegistrationIssue(shop, bed, register);
            if (!reason.NullOrEmpty()) return false;
            Zone_Shop previousShop = null;
            foreach (var config in settings)
            {
                if (config.registeredBeds.Remove(bed))
                    previousShop = map.zoneManager.AllZones.OfType<Zone_Shop>().FirstOrDefault(z => z.ID == config.shopZoneId);
            }
            if (register)
            {
                SettingsFor(shop).registeredBeds.Add(bed);
                registeredBedIds.Add(bed.thingIDNumber);
            }
            else
            {
                registeredBedIds.Remove(bed.thingIDNumber);
            }
            roomPrivacyDirty = true;
            if (previousShop != null && previousShop != shop) NotifyShopChanged(previousShop);
            if (shop != null) NotifyShopChanged(shop);
            return true;
        }

        //已预订的床只供该合同的住客使用，空床允许顾客进行入住适配检查。
        public bool AllowsBedUser(Building_Bed bed, Pawn pawn)
        {
            if (!IsRegisteredBed(bed)) return true;
            for (int i = 0; i < activeStays.Count; i++)
            {
                var stay = activeStays[i];
                if (stay.completed || stay.bed != bed) continue;
                for (int g = 0; g < stay.guests.Count; g++)
                    if (stay.guests[g].pawn == pawn) return true;
                return false;
            }
            return SimShopCustomerApi.GetCustomerVisit(pawn) != null;
        }
    }
}
