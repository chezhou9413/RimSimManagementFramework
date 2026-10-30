using System.Collections.Generic;
using RimSimHotelExtension.Buildings;
using Verse;

namespace RimSimHotelExtension.Models
{
    //保存单家旅馆的经营开关、床位及成交前报价。
    public sealed class HotelShopSettings : IExposable
    {
        public int shopZoneId;
        public bool enabled = true;
        public List<Building_HotelBed> registeredBeds = new List<Building_HotelBed>();
        public int singlePrice = 55;
        public int doublePrice = 90;
        public int woodSpaPrice = 30;
        public int stoneSpaPrice = 45;
        public bool spaEnabled = true;
        public int atmFee = 2;
        public int atmDailyLimit = 250;
        public bool acceptRenewals = true;
        public int metricsDay = -1;
        public int arrivalsToday;
        public int departuresToday;
        public int earnedRoomRevenueToday;

        //保存经营设置，床位仅保留原建筑引用。
        public void ExposeData()
        {
            Scribe_Values.Look(ref shopZoneId, "shopZoneId");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Collections.Look(ref registeredBeds, "registeredBeds", LookMode.Reference);
            Scribe_Values.Look(ref singlePrice, "singlePrice", 55);
            Scribe_Values.Look(ref doublePrice, "doublePrice", 90);
            Scribe_Values.Look(ref woodSpaPrice, "woodSpaPrice", 30);
            Scribe_Values.Look(ref stoneSpaPrice, "stoneSpaPrice", 45);
            Scribe_Values.Look(ref spaEnabled, "spaEnabled", true);
            Scribe_Values.Look(ref atmFee, "atmFee", 2);
            Scribe_Values.Look(ref atmDailyLimit, "atmDailyLimit", 250);
            Scribe_Values.Look(ref acceptRenewals, "acceptRenewals", true);
            Scribe_Values.Look(ref metricsDay, "metricsDay", -1);
            Scribe_Values.Look(ref arrivalsToday, "arrivalsToday");
            Scribe_Values.Look(ref departuresToday, "departuresToday");
            Scribe_Values.Look(ref earnedRoomRevenueToday, "earnedRoomRevenueToday");
        }
    }
}
