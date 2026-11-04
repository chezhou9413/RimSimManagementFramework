using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Services;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.GameComp
{
    //在真实到客前预留整床，并把同行者绑定到同一份住宿合同。
    public sealed partial class HotelMapComponent
    {
        //预留具体床位，同步生成失败时由框架原样交还令牌。
        public CustomerArrivalReservation PrepareArrival(Zone_Shop shop, int availablePlaces)
        {
            if (!GetIssue(shop).NullOrEmpty() || availablePlaces < 1) return null;
            bool pair = availablePlaces >= 2 && Rand.Value < HotelTuningDef.Current.companionChance;
            if (pair && !AvailableBeds(shop).Any(b => b.SleepingSlotsCount >= 2)) pair = false;
            var beds = AvailableBeds(shop).Where(b => !pair || b.SleepingSlotsCount >= 2).ToList();
            if (beds.Count == 0) return null;
            Building_Bed bed = pair ? beds.RandomElement() : beds.OrderBy(b => b.SleepingSlotsCount).First();
            arrivalBeds.Add(bed.thingIDNumber);
            return new CustomerArrivalReservation { token = bed.thingIDNumber.ToString(), count = pair ? 2 : 1 };
        }

        //撤销还未生成顾客的床位预留。
        public void CancelArrival(CustomerArrivalReservation reservation)
        {
            if (int.TryParse(reservation?.token, out int id)) arrivalBeds.Remove(id);
        }

        //生成成功后核对真实资金，不足最低住宿期限的顾客继续作为普通散客。
        public void CompleteArrival(Zone_Shop shop, CustomerArrivalReservation reservation, List<Pawn> customers)
        {
            CancelArrival(reservation);
            if (!int.TryParse(reservation.token, out int id)) return;
            Building_Bed bed = AvailableBeds(shop).FirstOrDefault(b => b.thingIDNumber == id);
            if (bed != null) TryCreateStay(shop, bed, customers);
        }

        //允许已经逛到旅馆的单人顾客直接办理入住。
        public bool TryCheckInExisting(Pawn pawn, Zone_Shop shop)
        {
            if (FindStay(pawn) != null) return true;
            if (!GetIssue(shop).NullOrEmpty()) return false;
            Building_Bed bed = AffordableBedFor(pawn, shop, SimShopCustomerApi.GetRemainingBudget(pawn, shop));
            return bed != null && TryCreateStay(shop, bed, new List<Pawn> { pawn });
        }

        //动作选择阶段按实际空床整价核算，不用固定金额阻挡便宜客房。
        public bool CanAffordCheckIn(Pawn pawn, Zone_Shop shop, float availableBudget)
            => AffordableBedFor(pawn, shop, availableBudget) != null;

        //单人入住优先单床，也允许以整床价独占可负担的双床。
        private Building_Bed AffordableBedFor(Pawn pawn, Zone_Shop shop, float availableBudget)
        {
            HotelTuningDef t = HotelTuningDef.Current;
            HotelShopSettings config = SettingsFor(shop);
            int foodReserve = HotelFoodUtility.DailyFoodReserve(pawn);
            return AvailableBeds(shop).OrderBy(b => b.SleepingSlotsCount).FirstOrDefault(b =>
                availableBudget >= t.minNights * (Mathf.CeilToInt(ShopPricingUtility.ApplyCashierPrice(shop,
                    b.SleepingSlotsCount > 1 ? config.doublePrice : config.singlePrice)) + foodReserve) + t.departureReserve
                && RestUtility.CanUseBedNow(b, pawn, false) && HotelBedReachabilityUtility.CanReach(pawn, b));
        }

        //根据已锁定的整床报价和每个人真实预算建立待接待合同。
        private bool TryCreateStay(Zone_Shop shop, Building_Bed bed, List<Pawn> customers)
        {
            if (customers.Count == 0 || customers.Count > bed.SleepingSlotsCount
                || customers.Any(p => p?.ownership == null || FindStay(p) != null
                    || !RestUtility.CanUseBedNow(bed, p, false)
                    || !HotelBedReachabilityUtility.CanReach(p, bed))) return false;
            HotelTuningDef t = HotelTuningDef.Current;
            HotelShopSettings config = SettingsFor(shop);
            int price = Mathf.CeilToInt(ShopPricingUtility.ApplyCashierPrice(shop,
                bed.SleepingSlotsCount > 1 ? config.doublePrice : config.singlePrice));
            int[] budgets = customers.Select(p => Mathf.FloorToInt(SimShopCustomerApi.GetRemainingBudget(p, shop))).ToArray();
            int nights = PickNights(t);
            while (nights >= t.minNights && customers.Where((p, i) => budgets[i] < nights *
                (Share(price, customers.Count, i) + HotelFoodUtility.DailyFoodReserve(p)) + t.departureReserve).Any()) nights--;
            if (nights < t.minNights) return false;
            int now = Find.TickManager.TicksGame;
            int firstNight = now - GenLocalDate.DayTick(map) + t.nightStartHour * 2500;
            HotelStay stay = new HotelStay { id = nextId++, shopZoneId = shop.ID, bed = bed, startTick = now,
                receptionServiceTicksAtStart = DeskFor(shop).CustomerServiceTicks,
                lastQualityTick = now,
                bookedNights = nights, nightlyPrice = price, firstNightTick = firstNight,
                checkoutTick = firstNight + nights * 60000 + (t.checkoutHour - t.nightStartHour) * 2500 };
            for (int i = 0; i < nights; i++) stay.nightPrices.Add(price);
            for (int i = 0; i < customers.Count; i++)
            {
                int share = Share(price, customers.Count, i);
                int pocket = Mathf.Min(budgets[i], share * nights + t.initialPocketMoney);
                var guest = new HotelGuestState { pawn = customers[i], nightShare = share, wallet = pocket,
                    account = budgets[i] - pocket, satisfaction = t.initialSatisfaction,
                    dailyFoodBudget = HotelFoodUtility.DailyFoodReserve(customers[i]),
                    status = "RSH.Text.CheckingIn" };
                for (int n = 0; n < nights; n++) guest.prepaidNightShares.Add(share);
                stay.guests.Add(guest);
            }
            stays.Add(stay);
            IndexStay(stay);
            RefreshRoomPrivacy();
            foreach (HotelGuestState guest in stay.guests) SimShopCustomerApi.ReturnToLongStay(guest.pawn);
            return true;
        }

        //按单一权重表生成住宿意向，资金不足由合同校验缩短。
        private static int PickNights(HotelTuningDef t)
        {
            var weights = t.nightWeights;
            int roll = Rand.Range(0, weights.Sum());
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return Mathf.Clamp(i + t.minNights, t.minNights, t.maxNights);
            }
            return t.minNights;
        }

        //将整床金额分成整数份额，奇数尾差固定归首位顾客。
        private static int Share(int price, int count, int index) => price / count + (index < price % count ? 1 : 0);

        //前台完成接待后才扣取预付款和认领床位。
        public void CompleteReception(Pawn pawn)
        {
            HotelStay stay = FindStay(pawn);
            HotelGuestState guest = GuestFor(pawn);
            if (stay == null || guest == null) return;
            if (stay.closing)
            {
                guest.checkoutDone = true;
                if (stay.guests.All(g => g.checkoutDone)) FinishCheckout(stay);
                return;
            }
            if (stay.active)
            {
                if (stay.renewalRequested) TryRenew(stay);
                return;
            }
            guest.receptionDone = true;
            guest.status = "RSH.Text.WaitingForCompanions";
            if (!stay.guests.All(g => g.receptionDone)) return;
            if (stay.bed == null || !stay.bed.Spawned || stay.bed.OwnersForReading.Count > 0
                || stay.bed.Medical || stay.bed.ForPrisoners || stay.bed.ForSlaves
                || ShopFor(stay)?.Cells.Contains(stay.bed.Position) != true
                || stay.guests.Any(g => g.wallet < g.prepaidNightShares.Sum()
                    || !RestUtility.CanUseBedNow(stay.bed, g.pawn, false)
                    || !g.pawn.CanReach(stay.bed, PathEndMode.OnCell, Danger.Some)))
            {
                RequestCheckout(stay, "RSH.Text.BedOrFundsAreNoLongerAvailable");
                return;
            }
            foreach (HotelGuestState item in stay.guests)
            {
                int payment = item.prepaidNightShares.Sum();
                item.wallet -= payment;
                item.escrow = payment;
                item.totalSpent += payment;
                AddMoneyRecord(new HotelMoneyRecord { tick = Find.TickManager.TicksGame, pawn = item.pawn,
                    shopZoneId = stay.shopZoneId, kind = "Prepay", amount = payment,
                    label = "RSH.Text.AccommodationPrepayment" });
                item.pawn.ownership.ClaimBedIfNonMedical(stay.bed);
                item.status = "RSH.Text.Staying";
                item.nextActivityTick = Find.TickManager.TicksGame + 300;
            }
            stay.active = true;
            foreach (var item in stay.guests) HotelMoneyUtility.RefreshDailyAllowance(stay, item);
            SettingsFor(ShopFor(stay)).arrivalsToday += stay.guests.Count;
        }
    }
}
