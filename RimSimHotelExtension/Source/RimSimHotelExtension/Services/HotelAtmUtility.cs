using System.Linq;
using RimWorld;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //在住客账户和钱包之间移动有限资金，只有手续费计入商店收入。
    public static class HotelAtmUtility
    {
        //有余额且今日仍有额度时，选择最近可用且能预约的机器。
        public static Job MakeJob(Pawn pawn, int minimumNeeded = 0)
        {
            var manager = HotelMapComponent.For(pawn.Map);
            var guest = manager.GuestFor(pawn);
            if (guest == null) return null;
            ResetAllowance(guest);
            var machine = pawn.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelAtm>()
                .Where(a => a.Available && pawn.CanReserveAndReach(a, PathEndMode.InteractionCell, Danger.Some)
                    && SimShopServiceApi.FindShop(pawn.Map, a.Position) != null)
                .OrderBy(a => pawn.Position.DistanceToSquared(a.Position)).FirstOrDefault();
            if (machine == null) return null;
            var config = manager.SettingsFor(SimShopServiceApi.FindShop(pawn.Map, machine.Position));
            if (guest.withdrawnToday >= config.atmDailyLimit || guest.account <= config.atmFee) return null;
            if (guest.wallet >= Mathf.Min(HotelTuningDef.Current.atmTargetWallet,
                Mathf.Max(minimumNeeded, HotelTuningDef.Current.foodBudgetPerDay))) return null;
            guest.status = "RSH.Text.WithdrawingTravelFunds";
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_UseAtm"), machine);
        }

        //在单次主线程步骤中扣旅行账户、增钱包并提交手续费收入。
        public static bool Withdraw(Pawn pawn, Building_HotelAtm machine, int jobId, out string reason)
        {
            reason = "";
            var manager = HotelMapComponent.For(pawn.Map);
            var guest = manager.GuestFor(pawn);
            var shop = SimShopServiceApi.FindShop(pawn.Map, machine.Position);
            if (guest == null || shop == null || !machine.Available) return false;
            ResetAllowance(guest);
            var config = manager.SettingsFor(shop);
            int fee = Mathf.Max(0, config.atmFee);
            int amount = Mathf.Min(HotelTuningDef.Current.atmTargetWallet - guest.wallet,
                config.atmDailyLimit - guest.withdrawnToday, guest.account - fee);
            if (amount <= 0)
            {
                reason = HotelText.T("RSH.Text.AccountBalanceOrTodaySWithdrawalAllowanceIs");
                guest.status = "RSH.Text.AccountBalanceOrTodaySWithdrawalAllowanceIs";
                return false;
            }
            var result = SimShopFinanceApi.CommitImmediateIncome(pawn, shop, machine,
                "HotelATM/" + pawn.Map.uniqueID + "/" + pawn.thingIDNumber + "/" + jobId,
                HotelText.T("RSH.Text.ATMWithdrawalFee"), fee);
            if (!result.success) { reason = result.failReason; guest.status = reason; return false; }
            guest.account -= amount + fee;
            guest.wallet += amount;
            guest.withdrawnToday += amount;
            guest.totalSpent += fee;
            guest.optionalSpent += fee;
            manager.AddMoneyRecord(new HotelMoneyRecord { tick = Find.TickManager.TicksGame, pawn = pawn,
                shopZoneId = shop.ID, kind = "withdrawal", amount = amount, fee = fee,
                label = "RSH.Text.TravelAccountWithdrawal" });
            guest.status = "RSH.Text.WithdrawalCompleted";
            return true;
        }

        //新的一天只恢复操作额度，不给旅行账户充值。
        private static void ResetAllowance(HotelGuestState guest)
        {
            int day = GenDate.DaysPassed;
            if (guest.withdrawalDay == day) return;
            guest.withdrawalDay = day;
            guest.withdrawnToday = 0;
        }
    }
}
