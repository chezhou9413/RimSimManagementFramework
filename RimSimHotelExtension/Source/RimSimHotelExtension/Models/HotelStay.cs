using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Buildings;
using Verse;

namespace RimSimHotelExtension.Models
{
    //保存整床入住单，同行住客分别活动并在同一时间离店。
    public sealed class HotelStay : IExposable
    {
        public int id;
        public int shopZoneId;
        public Building_HotelBed bed;
        public List<HotelGuestState> guests = new List<HotelGuestState>();
        public int startTick;
        public int checkoutTick;
        public int firstNightTick;
        public int bookedNights;
        public int earnedNights;
        public int nightlyPrice;
        public List<int> nightPrices = new List<int>();
        public bool active;
        public bool closing;
        public bool completed;
        public bool finishing;
        public int closingTick;
        public bool renewalChecked;
        public bool renewalRequested;
        public int renewalNights;
        public int lastQualityTick;
        public string closeReason = "";
        public int escrow => guests.Sum(g => g.escrow);
        public int TotalWallet => guests.Sum(g => g.wallet);
        public int TotalAccount => guests.Sum(g => g.account);
        public float Satisfaction => guests.Count == 0 ? 0 : guests.Average(g => g.satisfaction);

        //保存入住合同和费用释放进度，重复读档不会重复收房费。
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref shopZoneId, "shopZoneId");
            Scribe_References.Look(ref bed, "bed");
            Scribe_Collections.Look(ref guests, "guests", LookMode.Deep);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref checkoutTick, "checkoutTick");
            Scribe_Values.Look(ref firstNightTick, "firstNightTick");
            Scribe_Values.Look(ref bookedNights, "bookedNights");
            Scribe_Values.Look(ref earnedNights, "earnedNights");
            Scribe_Values.Look(ref nightlyPrice, "nightlyPrice");
            Scribe_Collections.Look(ref nightPrices, "nightPrices", LookMode.Value);
            Scribe_Values.Look(ref active, "active");
            Scribe_Values.Look(ref closing, "closing");
            Scribe_Values.Look(ref completed, "completed");
            Scribe_Values.Look(ref closingTick, "closingTick");
            Scribe_Values.Look(ref renewalChecked, "renewalChecked");
            Scribe_Values.Look(ref renewalRequested, "renewalRequested");
            Scribe_Values.Look(ref renewalNights, "renewalNights");
            Scribe_Values.Look(ref lastQualityTick, "lastQualityTick");
            Scribe_Values.Look(ref closeReason, "closeReason", "");
        }
    }
}
