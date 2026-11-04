using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Jobs;
using RimSimHotelExtension.Models;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //按所属旅馆的当地作息安排固定床位睡眠。
    internal static class HotelSleepUtility
    {
        //起点包含、终点不包含，同时支持跨午夜和日间睡眠。
        internal static bool IsSleepTime(Map map, HotelShopSettings settings)
        {
            int hour = GenLocalDate.HourOfDay(map);
            return settings.sleepStartHour > settings.wakeHour
                ? hour >= settings.sleepStartHour || hour < settings.wakeHour
                : hour >= settings.sleepStartHour && hour < settings.wakeHour;
        }

        //待入住和退房中的顾客先完成前台手续。
        internal static bool ShouldSleep(HotelMapComponent manager, HotelStay stay)
            => stay.active && !stay.closing && !stay.completed && IsSleepTime(manager.map, manager.SettingsFor(stay));

        //使用原版卧床流程，但不因精力充满而提前起床。
        internal static Job MakeJob(HotelStay stay, HotelGuestState guest)
        {
            guest.foodShopping = false;
            guest.status = "RSH.Text.SleepingInTheReservedBed";
            Job sleep = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_Sleep"), stay.bed);
            sleep.forceSleep = true;
            return sleep;
        }

        //周期调度仅打断已返回住宿职责的活动，不重启同一张床的睡眠。
        internal static void EnsureSleeping(Pawn pawn, HotelStay stay, HotelGuestState guest)
        {
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState || stay.bed == null || !stay.bed.Spawned) return;
            if (pawn.jobs.curDriver is JobDriver_HotelSleep && pawn.CurJob.targetA.Thing == stay.bed) return;
            pawn.jobs.StartJob(MakeJob(stay, guest), JobCondition.InterruptForced, continueSleeping: true);
        }
    }
}
