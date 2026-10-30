using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;
using RimWorld;
using RimSimHotelExtension.Settings;

namespace RimSimHotelExtension.Services
{
    //连接旅馆个人钱包、框架收银和独立服务收入。
    public static class HotelMoneyUtility
    {
        //判断人物是否仍由有效住宿管理。
        public static bool IsGuest(Pawn pawn) => HotelMapComponent.For(pawn?.Map)?.GuestFor(pawn) != null;

        //入住接待完成且没有退房时才允许选择额外服务。
        public static bool CanUseServices(Pawn pawn)
        {
            HotelStay stay = HotelMapComponent.For(pawn?.Map)?.FindStay(pawn);
            return stay == null || (stay.active && !stay.closing && !HotelMapComponent.For(pawn.Map).GuestFor(pawn).foodShopping);
        }

        //返回框架已扣除当前未付款订单后的可用资金。
        public static float Available(Pawn pawn) => SimShopCustomerApi.GetAvailableFunds(pawn);

        //将票款留在活动托管中，尚不记为营业收入。
        public static bool TryDebit(Pawn pawn, int amount, out string reason)
        {
            var result = SimShopCustomerApi.TrySpendCustomerFunds(pawn, amount);
            reason = result.failReason;
            return result.success;
        }

        //把尚未赚取的服务款退回原顾客钱包。
        public static void Refund(Pawn pawn, int amount) => SimShopCustomerApi.RefundCustomerFunds(pawn, amount);

        //独立业务入账不修改顾客正在另一商店结算的账单。
        public static bool RecordServiceIncome(Pawn pawn, Zone_Shop shop, Thing receiver, string key,
            string label, int amount, out string reason)
        {
            var result = SimShopFinanceApi.CommitImmediateIncome(pawn, shop, receiver, key, label, amount);
            reason = result.failReason;
            return result.success;
        }

        //更新体验积分并限制在百分制范围内。
        public static void AddSatisfaction(Pawn pawn, float amount)
        {
            HotelGuestState guest = HotelMapComponent.For(pawn?.Map)?.GuestFor(pawn);
            if (guest != null) guest.satisfaction = Mathf.Clamp(guest.satisfaction + amount, 0, 100);
        }

        //为余下住宿预留食费，旅行账户仍可支付的部分不重复锁住现金。
        public static int SpendingFunds(HotelStay stay, HotelGuestState guest)
        {
            if (!stay.active || stay.closing || guest.foodShopping) return guest.wallet;
            RefreshDailyAllowance(stay, guest);
            int remaining = Mathf.Max(0, guest.optionalDailyLimit - guest.optionalSpent);
            int days = Mathf.Max(1, Mathf.CeilToInt((stay.checkoutTick - Find.TickManager.TicksGame) / 60000f));
            remaining = Mathf.Min(remaining, Mathf.Max(0, guest.wallet + guest.account
                - days * guest.dailyFoodBudget - HotelTuningDef.Current.departureReserve));
            int owed = Mathf.CeilToInt(SimShopCustomerApi.GetAmountOwed(guest.pawn));
            return Mathf.Min(guest.wallet, Mathf.Max(remaining, owed));
        }

        //每日将剩余可选资金摊到余下天数，已有账单仍由支付入口完整保留。
        public static void RefreshDailyAllowance(HotelStay stay, HotelGuestState guest)
        {
            int day = GenDate.DaysPassed;
            if (guest.financeDay == day) return;
            guest.financeDay = day;
            guest.optionalSpent = 0;
            int days = Mathf.Max(1, Mathf.CeilToInt((stay.checkoutTick - Find.TickManager.TicksGame) / 60000f));
            int discretionary = Mathf.Max(0, guest.wallet + guest.account - guest.dailyFoodBudget * days
                - HotelTuningDef.Current.departureReserve);
            guest.optionalDailyLimit = discretionary / days;
        }
    }
}
