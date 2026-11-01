using System.Linq;
using RimWorld;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Services;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.GameComp
{
    //推进房费、续住、设施失效和同行退房。
    public sealed partial class HotelMapComponent
    {
        //检查住客及床位，再按已保存的每晚价格释放预付款。
        private void TickStay(HotelStay stay)
        {
            int now = Find.TickManager.TicksGame;
            var shop = ShopFor(stay);
            var desk = DeskFor(shop);
            if (shop == null || desk == null || stay.guests.Any(g => g.pawn == null || g.pawn.Dead
                || !g.pawn.Spawned || g.pawn.Map != map || g.pawn.Downed || g.pawn.InMentalState))
            {
                RequestCheckout(stay, "RSH.Text.GuestOrReceptionBecameUnavailable");
                FinishCheckout(stay);
                return;
            }
            if (stay.closing)
            {
                if (desk.GetUnservedWaitTicks(stay.closingTick, stay.checkoutServiceTicksAtStart)
                    >= HotelTuningDef.Current.receptionTimeout) FinishCheckout(stay);
                return;
            }
            if (!stay.active)
            {
                if (desk.GetUnservedWaitTicks(stay.startTick, stay.receptionServiceTicksAtStart)
                    > HotelTuningDef.Current.receptionTimeout)
                    RequestCheckout(stay, "RSH.Text.ReceptionWaitTimedOut2");
                return;
            }
            if (stay.bed == null || !stay.bed.Spawned || !shop.Cells.Contains(stay.bed.Position)
                || stay.bed.Medical || stay.bed.ForPrisoners || stay.bed.ForSlaves
                || stay.bed.OwnersForReading.Any(p => !stay.guests.Any(g => g.pawn == p))
                || stay.guests.Any(g => g.pawn.ownership.OwnedBed != stay.bed
                    || !RestUtility.CanUseBedNow(stay.bed, g.pawn, false)
                    || !g.pawn.CanReach(stay.bed, PathEndMode.OnCell, Danger.Some)))
            {
                var replacement = AvailableBeds(shop).FirstOrDefault(b => b.SleepingSlotsCount >= stay.guests.Count
                    && stay.guests.All(g => RestUtility.CanUseBedNow(b, g.pawn, false)
                        && g.pawn.CanReach(b, PathEndMode.OnCell, Danger.Some)));
                if (replacement == null)
                {
                    foreach (var guest in stay.guests) HotelQualityUtility.PenalizeMissingNeed(guest, false);
                    RequestCheckout(stay, "RSH.Text.NoUsableReplacementBed");
                    return;
                }
                stay.bed = replacement;
                foreach (var guest in stay.guests) guest.pawn.ownership.ClaimBedIfNonMedical(replacement);
            }
            HotelQualityUtility.ObserveSleep(stay);
            while (stay.earnedNights < stay.bookedNights && now >= stay.firstNightTick + stay.earnedNights * 60000)
            {
                int amount = stay.nightPrices[stay.earnedNights];
                var result = SimShopFinanceApi.CommitImmediateIncome(stay.guests[0].pawn, shop, DeskFor(shop),
                    "Hotel/" + map.uniqueID + "/" + stay.id + "/Night/" + stay.earnedNights,
                    HotelText.T("RSH.Text.HotelAccommodation"), amount);
                if (!result.success) { Log.Error("[RimSimHotel] " + result.failReason); break; }
                foreach (var guest in stay.guests) guest.escrow -= guest.prepaidNightShares[stay.earnedNights];
                stay.earnedNights++;
                SettingsFor(shop).earnedRoomRevenueToday += amount;
                AddMoneyRecord(new HotelMoneyRecord { tick = now, pawn = stay.guests[0].pawn,
                    shopZoneId = stay.shopZoneId, kind = "Room", amount = amount,
                    label = "RSH.Text.EarnedNightlyRoomRevenue" });
            }
            if (now - stay.lastQualityTick >= 60000)
            {
                stay.lastQualityTick = now;
                HotelQualityUtility.UpdateSatisfaction(stay);
            }
            if (!stay.renewalChecked && !stay.renewalRequested && now >= stay.checkoutTick - HotelTuningDef.Current.renewalLeadTicks)
            {
                stay.renewalRequested = CanRenew(stay);
                if (stay.renewalRequested)
                    stay.renewalNights = Mathf.Min(HotelTuningDef.Current.maxTotalNights - stay.bookedNights,
                        Rand.RangeInclusive(HotelTuningDef.Current.minRenewalNights, HotelTuningDef.Current.maxRenewalNights));
                if (!stay.renewalRequested) stay.renewalChecked = true;
            }
            if (now >= stay.checkoutTick) RequestCheckout(stay, "RSH.Text.ReservationHasEnded");
        }

        //统一发起同行退房，当前未结束的消费由框架正常收尾。
        public void RequestCheckout(HotelStay stay, string reason)
        {
            if (stay == null || stay.completed || stay.closing) return;
            stay.closing = true;
            stay.closingTick = Find.TickManager.TicksGame;
            stay.checkoutServiceTicksAtStart = DeskFor(ShopFor(stay))?.CustomerServiceTicks ?? 0;
            stay.closeReason = reason;
            foreach (var guest in stay.guests)
            {
                guest.status = "RSH.Text.CheckingOut";
                if (guest.pawn?.Spawned == true) SimShopCustomerApi.ReturnToLongStay(guest.pawn);
            }
        }

        //退回未开始夜晚的预付款，释放床位后才交还离图流程。
        public void FinishCheckout(HotelStay stay)
        {
            if (stay == null || stay.completed || stay.finishing) return;
            stay.finishing = true;
            var endingShop = ShopFor(stay);
            if (stay.active && endingShop != null) SettingsFor(endingShop).departuresToday += stay.guests.Count;
            int now = Find.TickManager.TicksGame;
            foreach (var guest in stay.guests)
            {
                if (guest.escrow > 0)
                {
                    int refund = guest.escrow;
                    guest.wallet += refund;
                    guest.totalSpent = Mathf.Max(0, guest.totalSpent - refund);
                    guest.escrow = 0;
                    AddMoneyRecord(new HotelMoneyRecord { tick = now, pawn = guest.pawn,
                        shopZoneId = stay.shopZoneId, kind = "Refund", amount = refund,
                        label = "RSH.Text.RefundForUnusedNights" });
                }
                if (guest.pawn?.ownership?.OwnedBed == stay.bed) guest.pawn.ownership.UnclaimBed();
                guest.status = "RSH.Text.CheckedOut";
                HotelReviewUtility.Record(this, stay, guest);
            }
            stay.closingTick = now;
            foreach (var guest in stay.guests)
                if (guest.pawn?.Spawned == true && !guest.pawn.Dead)
                    SimShopCustomerApi.EndLongStayVisit(guest.pawn, HotelText.Display(stay.closeReason));
            stay.completed = true;
            stay.active = false;
            stay.finishing = false;
        }

        //续住需要同行双方都有足够的房费和饮食储备。
        private bool CanRenew(HotelStay stay)
        {
            var shop = ShopFor(stay);
            HotelTuningDef t = HotelTuningDef.Current;
            if (!SettingsFor(shop).acceptRenewals || stay.Satisfaction < t.renewalSatisfaction
                || stay.bookedNights >= t.maxTotalNights) return false;
            int price = Quote(stay);
            int remainingDays = Mathf.Max(0, Mathf.CeilToInt((stay.checkoutTick - Find.TickManager.TicksGame) / 60000f));
            return stay.guests.Select((g, i) => g.wallet + g.account >= Share(price, stay.guests.Count, i)
                + (remainingDays + 1) * g.dailyFoodBudget + t.departureReserve).All(v => v);
        }

        //前台确认续住晚数，并保存新的成交价而不改写已订夜晚。
        private void TryRenew(HotelStay stay)
        {
            if (!stay.renewalRequested || !CanRenew(stay))
            {
                stay.renewalChecked = true;
                stay.renewalRequested = false;
                return;
            }
            int price = Quote(stay);
            int nights = Mathf.Clamp(stay.renewalNights, 1, HotelTuningDef.Current.maxTotalNights - stay.bookedNights);
            int remainingDays = Mathf.Max(0, Mathf.CeilToInt((stay.checkoutTick - Find.TickManager.TicksGame) / 60000f));
            while (nights > 1 && stay.guests.Where((g, i) => g.wallet + g.account < nights * Share(price, stay.guests.Count, i)
                + (remainingDays + nights) * g.dailyFoodBudget + HotelTuningDef.Current.departureReserve).Any()) nights--;
            if (stay.guests.Where((g, i) => g.wallet < Share(price, stay.guests.Count, i) * nights).Any()) return;
            for (int n = 0; n < nights; n++) stay.nightPrices.Add(price);
            for (int i = 0; i < stay.guests.Count; i++)
            {
                HotelGuestState guest = stay.guests[i];
                int share = Share(price, stay.guests.Count, i);
                int amount = share * nights;
                guest.wallet -= amount;
                guest.escrow += amount;
                guest.totalSpent += amount;
                AddMoneyRecord(new HotelMoneyRecord { tick = Find.TickManager.TicksGame, pawn = guest.pawn,
                    shopZoneId = stay.shopZoneId, kind = "Prepay", amount = amount,
                    label = "RSH.Text.ExtensionPrepayment" });
                for (int n = 0; n < nights; n++) guest.prepaidNightShares.Add(share);
            }
            stay.bookedNights += nights;
            stay.checkoutTick += 60000 * nights;
            stay.renewalRequested = false;
            stay.renewalChecked = false;
        }

        //只在新续住时读取当前前台报价。
        public int RenewalPaymentFor(HotelStay stay, HotelGuestState guest)
        {
            return Share(Quote(stay), stay.guests.Count, stay.guests.IndexOf(guest)) * Mathf.Max(1, stay.renewalNights);
        }

        //读取当前服务台的整床续住报价。
        private int Quote(HotelStay stay)
        {
            var shop = ShopFor(stay);
            var config = SettingsFor(shop);
            return Mathf.CeilToInt(ShopPricingUtility.ApplyCashierPrice(shop,
                stay.bed.SleepingSlotsCount > 1 ? config.doublePrice : config.singlePrice));
        }
    }
}
