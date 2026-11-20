using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.Services
{
    //客房环境预览数据，供客房管理列表和详情页统一展示环境评分与每日满意度影响。
    public struct HotelRoomPreview
    {
        public string roleLabel;
        public string impressivenessLabel;
        public float impressiveness;
        public float cleanliness;
        public float beauty;
        public float space;
        public float temperature;
        public bool isPrivateRoom;
        public float estimatedDailyDelta;
    }

    //根据客房环境、温度、整洁度、私密性、真实休息和基本需求更新住客满意度。
    public static class HotelQualityUtility
    {
        //评估单张床所在客房的实时品质与预估每日满意度变化，供界面与入住评价共享。
        public static HotelRoomPreview EvaluateRoomPreview(Building_Bed bed)
        {
            Room room = bed != null && bed.Spawned ? bed.GetRoom() : null;
            bool indoors = room != null && !room.PsychologicallyOutdoors;
            float impressiveness = indoors ? room.GetStat(RoomStatDefOf.Impressiveness) : 0f;
            float cleanliness = indoors ? room.GetStat(RoomStatDefOf.Cleanliness) : 0f;
            float beauty = indoors ? room.GetStat(RoomStatDefOf.Beauty) : 0f;
            float space = indoors ? room.GetStat(RoomStatDefOf.Space) : 0f;
            float temperature = bed != null && bed.Spawned && bed.Map != null
                ? GenTemperature.GetTemperatureForCell(bed.Position, bed.Map) : 21f;
            int stage = RoomStatDefOf.Impressiveness.GetScoreStageIndex(impressiveness);
            var scores = HotelTuningDef.Current.roomSatisfactionByStage;
            float baseDelta = scores[Mathf.Clamp(stage, 0, scores.Count - 1)];
            int bedCount = indoors ? room.ContainedBeds.Count() : 0;
            bool isPrivate = indoors && bedCount <= 1;
            float cleanDelta = cleanliness <= -1f ? -2f : (cleanliness >= 0.4f ? 1f : 0f);
            float privacyDelta = !indoors ? -2f : (isPrivate ? 1f : (bedCount >= 3 ? -1f : 0f));
            float tempDelta = temperature >= 15f && temperature <= 27f ? 1f
                : (temperature < 6f || temperature > 34f ? -3f : 0f);

            return new HotelRoomPreview
            {
                roleLabel = indoors && room.Role != null ? room.Role.LabelCap.ToString() : HotelText.T("RSH.Text.Outdoors"),
                impressivenessLabel = indoors ? RoomStatDefOf.Impressiveness.GetScoreStage(impressiveness).label.CapitalizeFirst() : HotelText.T("RSH.Text.Outdoors"),
                impressiveness = impressiveness,
                cleanliness = cleanliness,
                beauty = beauty,
                space = space,
                temperature = temperature,
                isPrivateRoom = isPrivate,
                estimatedDailyDelta = baseDelta + cleanDelta + privacyDelta + tempDelta
            };
        }

        //每日只结算一次体验，结合房间印象、整洁度、私密性与适宜温度并保存可读说明。
        public static void UpdateSatisfaction(HotelStay stay)
        {
            if (stay?.bed == null || !stay.bed.Spawned) return;
            Room room = stay.bed.GetRoom();
            bool indoors = room != null && !room.PsychologicallyOutdoors;
            float impressiveness = indoors ? room.GetStat(RoomStatDefOf.Impressiveness) : 0f;
            int stage = RoomStatDefOf.Impressiveness.GetScoreStageIndex(impressiveness);
            var scores = HotelTuningDef.Current.roomSatisfactionByStage;
            float roomDelta = scores[Mathf.Clamp(stage, 0, scores.Count - 1)];
            float cleanliness = indoors ? room.GetStat(RoomStatDefOf.Cleanliness) : 0f;
            float cleanDelta = cleanliness <= -1f ? -2f : (cleanliness >= 0.4f ? 1f : 0f);
            int bedCount = indoors ? room.ContainedBeds.Count() : 0;
            float privacyDelta = !indoors ? -2f : (bedCount <= 1 ? 1f : (bedCount >= 3 ? -1f : 0f));

            foreach (HotelGuestState guest in stay.guests)
            {
                if (guest.pawn == null) continue;
                float tempDelta = 0f;
                if (guest.pawn.Spawned && stay.bed.Map != null)
                {
                    if (guest.pawn.ComfortableTemperatureAtCell(stay.bed.Position, stay.bed.Map)) tempDelta = 1f;
                    else if (!guest.pawn.SafeTemperatureAtCell(stay.bed.Position, stay.bed.Map)) tempDelta = -4f;
                    else tempDelta = -1.5f;
                }

                float totalDelta = roomDelta + cleanDelta + privacyDelta + tempDelta;
                guest.satisfaction = Mathf.Clamp(guest.satisfaction + totalDelta, 0, 100);
                guest.dailyFoodBudget = HotelFoodUtility.DailyFoodReserve(guest.pawn);
                guest.lastExperienceNote = BuildExperienceNote(roomDelta, cleanDelta, privacyDelta, tempDelta);
            }
        }

        //生成面向玩家的当日住宿体验摘要，说明客房环境对满意度的具体影响。
        private static string BuildExperienceNote(float roomDelta, float cleanDelta, float privacyDelta, float tempDelta)
        {
            var parts = new List<string>(4);
            parts.Add(HotelText.T("RSH.Exp.RoomImpressiveness", FormatDelta(roomDelta).Named("DELTA")));
            if (Mathf.Abs(tempDelta) > 0.01f)
                parts.Add(HotelText.T(tempDelta > 0f ? "RSH.Exp.TempGood" : "RSH.Exp.TempBad", FormatDelta(tempDelta).Named("DELTA")));
            if (Mathf.Abs(cleanDelta) > 0.01f)
                parts.Add(HotelText.T(cleanDelta > 0f ? "RSH.Exp.CleanGood" : "RSH.Exp.CleanBad", FormatDelta(cleanDelta).Named("DELTA")));
            if (Mathf.Abs(privacyDelta) > 0.01f)
                parts.Add(HotelText.T(privacyDelta > 0f ? "RSH.Exp.PrivateRoom" : "RSH.Exp.SharedRoom", FormatDelta(privacyDelta).Named("DELTA")));
            return string.Join(" · ", parts);
        }

        private static string FormatDelta(float value) => value >= 0f ? "+" + value.ToString("0.#") : value.ToString("0.#");

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
