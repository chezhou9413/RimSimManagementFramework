using System.Linq;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.Pojo;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.Services
{
    //把住宿实际累计体验作为独立评价写入框架，不覆盖旅途中其他店铺的评价。
    public static class HotelReviewUtility
    {
        //每份成功入住合同为每位离店住客保存一次明确分数的住宿评价。
        public static void Record(HotelMapComponent manager, HotelStay stay, HotelGuestState guest)
        {
            if (!stay.active || guest.reviewRecorded || guest.pawn == null || guest.pawn.Dead) return;
            var shop = manager.ShopFor(stay);
            var visit = SimShopCustomerApi.GetCustomerVisit(guest.pawn);
            int paid = guest.prepaidNightShares.Take(stay.earnedNights).Sum();
            int stars = Mathf.Clamp(Mathf.CeilToInt(guest.satisfaction / 20f), 1, 5);
            string experience = HotelText.T("RSH.Review.StaySummary", stay.earnedNights.Named("NIGHTS"),
                paid.Named("PAID"), guest.satisfaction.ToString("0").Named("SCORE"));
            var record = new CustomerReviewRecord {
                reviewId = "Hotel/" + manager.map.uniqueID + "/" + stay.id + "/" + guest.pawn.thingIDNumber,
                tickAbs = Find.TickManager.TicksAbs, gameDay = GenDate.DaysPassed,
                zoneId = stay.shopZoneId, zoneLabel = shop?.label ?? HotelText.T("RSH.Text.Hotel"),
                customerDisplayName = guest.pawn.LabelShortCap, aiNickname = guest.pawn.LabelShortCap,
                stars = stars, reviewText = experience, spentSilver = paid,
                kindId = visit?.customerKindId ?? "", raceLabel = guest.pawn.def.label,
                purchasedSummary = HotelText.T("RSH.Text.SettledAccommodationCharges") + ": " + paid,
                serviceSummary = experience, checkoutJobSummary = HotelText.Display(stay.closeReason),
                generationStatus = CustomerReviewGenerationStatus.Completed
            };
            var result = SimShopReviewApi.RecordDirectReview(record);
            if (result.success) guest.reviewRecorded = true;
            else Log.Error("[RimSimHotel] " + result.failReason);
        }
    }
}
