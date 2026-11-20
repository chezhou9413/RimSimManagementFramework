using System.Linq;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Spa;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //在前台、入房安顿、吃饭、睡觉、取款、泡温泉和标准跨店消费之间安排住客生活。
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
            if (HotelSleepUtility.ShouldSleep(manager, stay)) return HotelSleepUtility.MakeJob(stay, guest);
            guest.foodShopping = false;
            bool hungry = pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < t.hungryThreshold;
            bool urgent = pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < t.urgentHungerThreshold;
            if (!urgent && pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < t.sleepThreshold)
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
            }
            else guest.foodSearchFailedTick = -1;

            //新办理入住的住客先回自己客房安顿一会，再外出活动，增强住店体验。
            if (!guest.roomSettled && !hungry)
            {
                Job settle = HotelLeisureUtility.MakeSettleInRoomJob(pawn, stay, guest);
                if (settle != null) return settle;
            }

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

            //饥饿时优先立即寻找餐饮店铺或餐厅；找不到时不在原地发呆，也不因纯旅馆或夜间打烊直接弃租。
            if (hungry)
            {
                if (guest.foodSearchFailedTick < 0 || now >= guest.nextActivityTick)
                {
                    var foodShops = SimShopCustomerApi.FindConsumableShops(pawn, true);
                    if (foodShops.Count > 0)
                    {
                        Zone_Shop home = manager.ShopFor(stay);
                        Zone_Shop selected = SelectVisitShop(pawn, guest, home, foodShops, true);
                        var result = SimShopCustomerApi.BeginShopVisit(pawn, selected, true);
                        if (result.success)
                        {
                            guest.lastShopId = selected.ID;
                            guest.nextActivityTick = now + t.activityInterval;
                            guest.shoppingUntilTick = now + t.shoppingVisitTicks;
                            guest.foodSearchFailedTick = -1;
                            guest.status = "RSH.Text.LookingForAMeal";
                            var arrival = pawn.Map.GetComponent<CustomerArrivalManager>();
                            IntVec3 entry = arrival != null && arrival.TryGetShopEntryCell(selected.ID, out IntVec3 cell)
                                ? cell : selected.Cells[0];
                            return JobMaker.MakeJob(JobDefOf.Goto, entry);
                        }
                    }

                    if (urgent)
                    {
                        EatTravelRation(pawn, guest);
                        guest.nextActivityTick = now + 900;
                        return RestNearRoom(pawn, stay, guest);
                    }

                    if (guest.foodSearchFailedTick < 0) guest.foodSearchFailedTick = now;
                    guest.nextActivityTick = now + 900;
                }
                return RestNearRoom(pawn, stay, guest);
            }

            if (now < guest.nextActivityTick) return RestNearRoom(pawn, stay, guest);

            //临近入睡前一小时不再发起新的非餐饮跨店行程，让住客自然留在旅馆休息或泡汤。
            Zone_Shop homeHotel = manager.ShopFor(stay);
            bool nearBedtime = IsNearBedtime(pawn.Map, manager.SettingsFor(stay));
            guest.nextActivityTick = now + t.activityInterval;

            var shops = SimShopCustomerApi.FindConsumableShops(pawn, false);
            if (nearBedtime)
                shops = shops.Where(s => s == homeHotel).ToList();

            if (shops.Count > 0)
            {
                Zone_Shop selected = SelectVisitShop(pawn, guest, homeHotel, shops, false);
                var result = SimShopCustomerApi.BeginShopVisit(pawn, selected, false);
                if (result.success)
                {
                    guest.lastShopId = selected.ID;
                    guest.shoppingUntilTick = now + t.shoppingVisitTicks;
                    guest.status = selected == homeHotel ? "RSH.Text.EnjoyingHotelFacilities" : "RSH.Text.ShoppingAndLeisure";
                    var arrival = pawn.Map.GetComponent<CustomerArrivalManager>();
                    IntVec3 entry = arrival != null && arrival.TryGetShopEntryCell(selected.ID, out IntVec3 cell)
                        ? cell : selected.Cells[0];
                    return JobMaker.MakeJob(JobDefOf.Goto, entry);
                }
            }
            return RestNearRoom(pawn, stay, guest);
        }

        //周围没有营业餐饮店或餐厅且极度饥饿时，住客吃自带干粮垫肚，避免在纯旅馆或夜间打烊时发呆弃租。
        private static void EatTravelRation(Pawn pawn, HotelGuestState guest)
        {
            if (pawn?.needs?.food != null)
                pawn.needs.food.CurLevel = Mathf.Max(pawn.needs.food.CurLevel, pawn.needs.food.MaxLevel * 0.65f);
            guest.foodShopping = false;
            guest.foodSearchFailedTick = -1;
            if (pawn?.Spawned == true && !pawn.Dead)
                ShopBubbleUtility.ShowTextBubble(pawn, HotelText.T("RSH.Bubble.TravelRation"), new Color(0.95f, 0.86f, 0.62f));
        }

        //判断是否进入入睡前一小时的睡前放松时段。
        private static bool IsNearBedtime(Map map, HotelShopSettings settings)
        {
            int preSleepHour = (settings.sleepStartHour + 23) % 24;
            return GenLocalDate.HourOfDay(map) == preSleepHour;
        }

        //优先选择本旅馆内可用温泉，避免重复光顾刚离开的商店。
        private static Zone_Shop SelectVisitShop(Pawn pawn, HotelGuestState guest, Zone_Shop homeHotel,
            System.Collections.Generic.List<Zone_Shop> shops, bool hungry)
        {
            if (!hungry && homeHotel != null && shops.Contains(homeHotel)
                && SpaMapComponent.For(pawn.Map)?.CanBathe(pawn) == true
                && (pawn.needs?.joy?.CurLevel ?? 0.5f) < 0.78f && Rand.Chance(0.45f))
                return homeHotel;
            var choices = shops.Where(s => s.ID != guest.lastShopId).ToList();
            return (choices.Count > 0 ? choices : shops).RandomElement();
        }

        //到入睡时间或单店停留期限后先结清消费，再回床睡觉。
        public static void CheckShoppingReturn(Pawn pawn)
        {
            var manager = HotelMapComponent.For(pawn?.Map);
            var guest = manager?.GuestFor(pawn);
            var stay = manager?.FindStay(pawn);
            var session = SimShopCustomerApi.GetCustomerSession(pawn);
            if (guest == null || session == null || session.Stage == CustomerVisitStage.Leaving) return;
            bool sleepTime = HotelSleepUtility.ShouldSleep(manager, stay);
            if (session.Stage == CustomerVisitStage.LongStay)
            {
                if (sleepTime) HotelSleepUtility.EnsureSleeping(pawn, stay, guest);
                return;
            }
            if (!stay.closing && !sleepTime && Find.TickManager.TicksGame < guest.shoppingUntilTick) return;
            if (SimShopCustomerApi.HasRunningCustomerActionOrder(pawn)) return;
            if (SimShopCustomerApi.GetAmountOwed(pawn) > 0)
            {
                SimShopCustomerApi.MarkCustomerReadyForCheckout(pawn);
                return;
            }
            if (SimShopCustomerApi.ReturnToLongStay(pawn).success && sleepTime)
                HotelSleepUtility.EnsureSleeping(pawn, stay, guest);
        }

        //服务格可达即可排队，队首在真正接受服务时才预约位置。
        private static Job Reception(Pawn pawn, HotelMapComponent manager, HotelStay stay)
        {
            var desk = manager.DeskFor(manager.ShopFor(stay));
            if (desk == null) return null;
            if (stay.active && !stay.closing && !desk.IsManned) return null;
            IntVec3 cell = CheckoutQueueCellUtility.FindServiceCell(desk, pawn);
            if (!cell.IsValid || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)) return null;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_Reception"), desk);
            job.SetTarget(TargetIndex.C, cell);
            return job;
        }

        //已经办完的一位主动离开服务格，让同行者能够到前台办理。
        private static Job WaitForCompanion(Pawn pawn, HotelStay stay)
        {
            var manager = HotelMapComponent.For(pawn.Map);
            var desk = manager.DeskFor(manager.ShopFor(stay));
            IntVec3 serviceCell = desk == null ? IntVec3.Invalid : CheckoutQueueCellUtility.FindServiceCell(desk, pawn);
            if (stay.bed != null && pawn.Position.DistanceToSquared(stay.bed.Position) <= 9 && pawn.Position != serviceCell
                && !pawn.Position.IsForbidden(pawn))
                return Wait(300);
            if (stay.bed != null && stay.bed.Spawned)
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(stay.bed.Position, 3f, true))
                    if (cell != pawn.Position && cell != serviceCell && (desk == null || cell != desk.InteractionCell)
                        && cell.InBounds(pawn.Map) && !cell.IsForbidden(pawn)
                        && cell.Standable(pawn.Map) && !cell.GetThingList(pawn.Map).OfType<Pawn>().Any()
                        && pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Some))
                        return JobMaker.MakeJob(JobDefOf.Goto, cell);
            return Wait(300);
        }

        //消费或找餐间隙安排旅馆内散步、就座或赏景，不再因寻找餐饮而在空地发呆站着不动。
        private static Job RestNearRoom(Pawn pawn, HotelStay stay, HotelGuestState guest)
        {
            guest.status = "RSH.Text.RestingAtTheHotel";
            return HotelLeisureUtility.MakeJob(pawn, stay, guest) ?? Wait(300);
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
