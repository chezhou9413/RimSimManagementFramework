using System.Linq;
using System.Collections.Generic;
using Verse;

namespace RimSimHotelExtension.Settings
{
    //集中保存住宿、取款与日程的初始平衡数值。
    public sealed class HotelTuningDef : Def
    {
        public int minNights = 2;
        public int maxNights = 7;
        public int maxTotalNights = 10;
        public List<int> nightWeights = new List<int> { 30, 25, 20, 12, 8, 5 };
        public float companionChance = 0.25f;
        public int receptionOpenHour = 10;
        public int receptionCloseHour = 22;
        public int nightStartHour = 18;
        public int checkoutHour = 10;
        public int minRenewalNights = 1;
        public int maxRenewalNights = 2;
        public int renewalLeadTicks = 60000;
        public int initialSatisfaction = 70;
        public float restedSatisfactionGain = 2;
        public float sleepRestGainRequired = 0.2f;
        public float missingFoodSatisfactionLoss = 10;
        public float missingBedSatisfactionLoss = 10;
        public List<float> roomSatisfactionByStage = new List<float> { -4, -3, -2, -1, 0, 1, 2, 3, 4, 4 };
        public int sleepStartHour = 22;
        public int wakeHour = 7;
        public int singlePrice = 55;
        public int doublePrice = 90;
        public int woodSpaPrice = 30;
        public int stoneSpaPrice = 45;
        public int foodBudgetPerDay = 70;
        public int initialPocketMoney = 150;
        public int departureReserve = 20;
        public int atmFee = 2;
        public int atmDailyLimit = 250;
        public int atmTargetWallet = 300;
        public int atmUseTicks = 450;
        public int receptionTicks = 600;
        public int returnReceptionTicks = 300;
        public int receptionTimeout = 5000;
        public int activityInterval = 2500;
        public int shoppingVisitTicks = 5000;
        public int woodSpaTicks = 3000;
        public int stoneSpaTicks = 3750;
        public int spaCooldownTicks = 30000;
        public int spaQueuePatienceTicks = 2500;
        public int woodSpaCapacity = 4;
        public int stoneSpaCapacity = 6;
        public float woodSpaJoyGain = 0.35f;
        public float stoneSpaJoyGain = 0.45f;
        public float woodSpaSatisfactionGain = 3;
        public float stoneSpaSatisfactionGain = 5;
        public int renewalSatisfaction = 65;
        public float hungryThreshold = 0.45f;
        public float urgentHungerThreshold = 0.15f;
        public float sleepThreshold = 0.3f;

        //读取唯一调参数值，无配置时使用相同的安全默认值。
        public static HotelTuningDef Current => DefDatabase<HotelTuningDef>.AllDefsListForReading.FirstOrDefault() ?? Defaults;
        private static readonly HotelTuningDef Defaults = new HotelTuningDef();
    }
}
