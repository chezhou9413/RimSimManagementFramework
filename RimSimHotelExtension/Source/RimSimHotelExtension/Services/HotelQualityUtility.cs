using RimWorld;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Settings;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.Services
{
    //根据客房环境、真实休息和基本需求更新住客满意度。
    public static class HotelQualityUtility
    {
        //每日只结算一次体验，避免每个巡检Tick重复增加满意度。
        public static void UpdateSatisfaction(HotelStay stay)
        {
            Room room = stay.bed.GetRoom();
            int stage = RoomStatDefOf.Impressiveness.GetScoreStageIndex(room?.GetStat(RoomStatDefOf.Impressiveness) ?? 0);
            var scores = HotelTuningDef.Current.roomSatisfactionByStage;
            float change = scores[Mathf.Clamp(stage, 0, scores.Count - 1)];
            foreach (HotelGuestState guest in stay.guests)
            {
                guest.satisfaction = Mathf.Clamp(guest.satisfaction + change, 0, 100);
                guest.dailyFoodBudget = HotelFoodUtility.DailyFoodReserve(guest.pawn);
            }
        }

        //只有在预订床上实际睡眠并恢复足够休息时，才给予当天睡眠奖励。
        public static void ObserveSleep(HotelStay stay)
        {
            int day = GenDate.DaysPassed;
            foreach (var guest in stay.guests)
            {
                if (guest.experienceDay != day)
                {
                    guest.experienceDay = day;
                    guest.sleepRestGained = 0;
                    guest.lastSleepRest = -1;
                    guest.sleepRewarded = false;
                }
                if (guest.pawn.CurrentBed() != stay.bed || guest.pawn.Awake() || guest.pawn.needs?.rest == null)
                {
                    guest.lastSleepRest = -1;
                    continue;
                }
                float rest = guest.pawn.needs.rest.CurLevelPercentage;
                if (guest.lastSleepRest >= 0) guest.sleepRestGained += Mathf.Max(0, rest - guest.lastSleepRest);
                guest.lastSleepRest = rest;
                if (guest.sleepRewarded || guest.sleepRestGained < HotelTuningDef.Current.sleepRestGainRequired) continue;
                guest.sleepRewarded = true;
                guest.satisfaction = Mathf.Min(100, guest.satisfaction + HotelTuningDef.Current.restedSatisfactionGain);
            }
        }

        //同类无法满足的基本需求每天只扣一次体验分。
        public static void PenalizeMissingNeed(HotelGuestState guest, bool food)
        {
            int day = GenDate.DaysPassed;
            if ((food ? guest.foodPenaltyDay : guest.bedPenaltyDay) == day) return;
            if (food) guest.foodPenaltyDay = day; else guest.bedPenaltyDay = day;
            float loss = food ? HotelTuningDef.Current.missingFoodSatisfactionLoss : HotelTuningDef.Current.missingBedSatisfactionLoss;
            guest.satisfaction = Mathf.Max(0, guest.satisfaction - loss);
        }
    }
}
