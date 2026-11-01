using System.Linq;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //在前台、吃饭、睡觉、取款和标准跨店消费之间安排住客生活。
    public static class HotelScheduleUtility
    {
        //仅在框架长期生活职责中选下一件事，不打断正在支付的订单。
        public static Job MakeJob(Pawn pawn)
        {
            HotelMapComponent manager = HotelMapComponent.For(pawn.Map);
            HotelStay stay = manager.FindStay(pawn);
            HotelGuestState guest = manager.GuestFor(pawn);
            if (stay == null || guest == null) return null;
            HotelTuningDef t = HotelTuningDef.Current;
            int now = Find.TickManager.TicksGame;
            if (stay.closing)
                return guest.checkoutDone ? WaitForCompanion(pawn, stay) : Reception(pawn, manager, stay) ?? Wait(300);
            if (!stay.active)
                return guest.receptionDone ? WaitForCompanion(pawn, stay) : Reception(pawn, manager, stay) ?? Wait(300);
            guest.foodShopping = false;
            bool hungry = pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < t.hungryThreshold;
            bool urgent = pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < t.urgentHungerThreshold;
            int hour = GenLocalDate.HourOfDay(pawn.Map);
            if (!urgent && pawn.needs?.rest != null && (pawn.needs.rest.CurLevelPercentage < t.sleepThreshold
                || (!hungry && (hour >= t.sleepStartHour || hour < t.wakeHour) && pawn.needs.rest.CurLevelPercentage < 0.95f)))
            {
                guest.status = "RSH.Text.SleepingInTheReservedBed";
                Job sleep = JobMaker.MakeJob(JobDefOf.LayDown, stay.bed);
                sleep.expiryInterval = 1250;
                sleep.checkOverrideOnExpire = true;
                return sleep;
            }
            if (hungry)
            {
                Job meal = HotelFoodUtility.MakeInventoryMealJob(pawn);
                if (meal != null)
                {
                    guest.status = "RSH.Text.EatingPurchasedFood";
                    guest.foodSearchFailedTick = -1;
                    return meal;
                }
                guest.foodShopping = true;
                if (guest.foodSearchFailedTick >= 0 && now - guest.foodSearchFailedTick > t.receptionTimeout * 2)
                {
                    HotelQualityUtility.PenalizeMissingNeed(guest, true);
                    manager.RequestCheckout(stay, "RSH.Text.UnableToObtainAnAffordableMeal");
                    return Wait(300);
                }
            }
            else guest.foodSearchFailedTick = -1;
            HotelMoneyUtility.RefreshDailyAllowance(stay, guest);
            int required = stay.renewalRequested ? manager.RenewalPaymentFor(stay, guest)
                : Mathf.Min(t.atmTargetWallet, Mathf.Max(guest.dailyFoodBudget, guest.optionalDailyLimit - guest.optionalSpent));
            Job atm = HotelAtmUtility.MakeJob(pawn, required);
            if (atm != null) return atm;
            if (stay.renewalRequested && !hungry)
            {
                Job reception = Reception(pawn, manager, stay);
                if (reception != null) return reception;
            }
            if (now < guest.nextActivityTick) return RestNearRoom(pawn, stay, guest);
            guest.nextActivityTick = now + t.activityInterval;
            var shops = SimShopCustomerApi.FindConsumableShops(pawn, hungry);
            bool foodOnly = hungry && shops.Count > 0;
            if (hungry && shops.Count == 0)
                shops = SimShopCustomerApi.FindConsumableShops(pawn);
            if (shops.Count > 0)
            {
                var choices = shops.Where(s => s.ID != guest.lastShopId).ToList();
                var selected = (choices.Count > 0 ? choices : shops).RandomElement();
                var result = SimShopCustomerApi.BeginShopVisit(pawn, selected, foodOnly);
                if (result.success)
                {
                    guest.lastShopId = selected.ID;
                    guest.shoppingUntilTick = now + t.shoppingVisitTicks;
                    guest.status = hungry ? "RSH.Text.LookingForAMeal" : "RSH.Text.ShoppingAndLeisure";
                    if (hungry && guest.foodSearchFailedTick < 0) guest.foodSearchFailedTick = now;
                    return JobMaker.MakeJob(JobDefOf.Goto, selected.Cells[0]);
                }
            }
            if (hungry)
            {
                if (guest.foodSearchFailedTick < 0) guest.foodSearchFailedTick = now;
                HotelQualityUtility.PenalizeMissingNeed(guest, true);
                guest.status = "RSH.Text.NoFoodIsAvailableForPurchase";
                if (now - guest.foodSearchFailedTick > t.receptionTimeout * 2
                    || pawn.needs.food.CurLevelPercentage < t.urgentHungerThreshold)
                    manager.RequestCheckout(stay, "RSH.Text.NoAffordableMealsAreAvailable");
            }
            return RestNearRoom(pawn, stay, guest);
        }

        //单店停留期限只请求结账，不切断用餐或温泉持久订单。
        public static void CheckShoppingReturn(Pawn pawn)
        {
            var manager = HotelMapComponent.For(pawn?.Map);
            var guest = manager?.GuestFor(pawn);
            var stay = manager?.FindStay(pawn);
            var session = SimShopCustomerApi.GetCustomerSession(pawn);
            if (guest == null || session == null || session.Stage == CustomerVisitStage.LongStay) return;
            if (!stay.closing && Find.TickManager.TicksGame < guest.shoppingUntilTick) return;
            if (SimShopCustomerApi.HasRunningCustomerActionOrder(pawn)) return;
            if (SimShopCustomerApi.GetAmountOwed(pawn) > 0)
            {
                SimShopCustomerApi.MarkCustomerReadyForCheckout(pawn);
                return;
            }
            SimShopCustomerApi.ReturnToLongStay(pawn);
        }

        //面向服务台顾客侧生成接待任务，服务格被占用时等待而不抢占员工位置。
        private static Job Reception(Pawn pawn, HotelMapComponent manager, HotelStay stay)
        {
            var desk = manager.DeskFor(manager.ShopFor(stay));
            if (desk == null) return null;
            if (stay.active && !stay.closing && !desk.IsManned) return null;
            IntVec3 cell = CheckoutQueueCellUtility.FindServiceCell(desk, pawn);
            if (!cell.IsValid || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Some)) return null;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_Reception"), desk, cell);
            return job;
        }

        //已经办完的一位主动离开服务格，让同行者能够到前台办理。
        private static Job WaitForCompanion(Pawn pawn, HotelStay stay)
        {
            var manager = HotelMapComponent.For(pawn.Map);
            var desk = manager.DeskFor(manager.ShopFor(stay));
            IntVec3 serviceCell = desk == null ? IntVec3.Invalid : CheckoutQueueCellUtility.FindServiceCell(desk, pawn);
            if (stay.bed != null && pawn.Position.DistanceToSquared(stay.bed.Position) <= 9 && pawn.Position != serviceCell)
                return Wait(300);
            if (stay.bed != null && stay.bed.Spawned)
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(stay.bed.Position, 3f, true))
                    if (cell != pawn.Position && cell != serviceCell && (desk == null || cell != desk.InteractionCell)
                        && cell.Standable(pawn.Map) && !cell.GetThingList(pawn.Map).OfType<Pawn>().Any()
                        && pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Some))
                        return JobMaker.MakeJob(JobDefOf.Goto, cell);
            return Wait(300);
        }

        //没有下一项消费时回到客房附近短暂休息。
        private static Job RestNearRoom(Pawn pawn, HotelStay stay, HotelGuestState guest)
        {
            if (!guest.foodShopping) guest.status = "RSH.Text.RestingAtTheHotel";
            if (stay.bed != null && stay.bed.Spawned && pawn.Position.DistanceToSquared(stay.bed.Position) > 25
                && pawn.CanReach(stay.bed.InteractionCell, PathEndMode.OnCell, Danger.Some))
                return JobMaker.MakeJob(JobDefOf.Goto, stay.bed.InteractionCell);
            return Wait(300);
        }

        //有限等待让下一次生活调度能够及时检查饥饿和退房。
        private static Job Wait(int ticks)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Wait);
            job.expiryInterval = ticks;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
