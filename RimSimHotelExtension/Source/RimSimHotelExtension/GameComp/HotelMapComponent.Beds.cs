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

        //只查询登记记录，不为状态显示或原版找床流程创建经营设置。
        public bool IsRegisteredBed(Building_Bed bed)
            => bed != null && settings.Any(s => s.registeredBeds.Contains(bed));

        //预订和未完成退房的床都不能被改作其他用途。
        public bool IsBedBooked(Building_Bed bed)
            => bed != null && (arrivalBeds.Contains(bed.thingIDNumber) || stays.Any(s => !s.completed && s.bed == bed));

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

        //验证通过后才写入登记，拒绝时保留原来的用途和所属旅馆。
        public bool RegisterBed(Zone_Shop shop, Building_Bed bed, bool register, out string reason)
        {
            reason = BedRegistrationIssue(shop, bed, register);
            if (!reason.NullOrEmpty()) return false;
            foreach (var config in settings) config.registeredBeds.Remove(bed);
            if (register) SettingsFor(shop).registeredBeds.Add(bed);
            return true;
        }

        //已预订的床只供该合同的住客使用，空床允许顾客进行入住适配检查。
        public bool AllowsBedUser(Building_Bed bed, Pawn pawn)
        {
            if (!IsRegisteredBed(bed)) return true;
            var stay = stays.FirstOrDefault(s => !s.completed && s.bed == bed);
            return stay != null ? stay.guests.Any(g => g.pawn == pawn) : SimShopCustomerApi.GetCustomerVisit(pawn) != null;
        }
    }
}
