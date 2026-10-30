using Verse;
using System.Collections.Generic;

namespace RimSimHotelExtension.Models
{
    //保存住客个人资金和跨活动日程，旅行资金不会按天补充。
    public sealed class HotelGuestState : IExposable
    {
        public Pawn pawn;
        public int wallet;
        public int account;
        public int escrow;
        public int nightShare;
        public List<int> prepaidNightShares = new List<int>();
        public int withdrawnToday;
        public int withdrawalDay = -1;
        public float satisfaction = 70;
        public string status = "RSH.Text.CheckingIn";
        public int lastSpaTick = -60000;
        public int lastSpaDay = -1;
        public int nextActivityTick;
        public int shoppingUntilTick;
        public int lastShopId = -1;
        public int foodSearchFailedTick = -1;
        public bool receptionDone;
        public bool checkoutDone;
        public bool reviewRecorded;
        public bool foodShopping;
        public int dailyFoodBudget = 70;
        public int financeDay = -1;
        public int optionalDailyLimit;
        public int optionalSpent;
        public int experienceDay = -1;
        public float sleepRestGained;
        public float lastSleepRest = -1;
        public bool sleepRewarded;
        public int foodPenaltyDay = -1;
        public int bedPenaltyDay = -1;
        public float totalSpent;

        //保存个人余额及当前活动，读取后仍使用同一名人物。
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref wallet, "wallet");
            Scribe_Values.Look(ref account, "account");
            Scribe_Values.Look(ref escrow, "escrow");
            Scribe_Values.Look(ref nightShare, "nightShare");
            Scribe_Collections.Look(ref prepaidNightShares, "prepaidNightShares", LookMode.Value);
            Scribe_Values.Look(ref withdrawnToday, "withdrawnToday");
            Scribe_Values.Look(ref withdrawalDay, "withdrawalDay", -1);
            Scribe_Values.Look(ref satisfaction, "satisfaction", 70);
            Scribe_Values.Look(ref status, "status", "RSH.Text.CheckingIn");
            Scribe_Values.Look(ref lastSpaTick, "lastSpaTick", -60000);
            Scribe_Values.Look(ref lastSpaDay, "lastSpaDay", -1);
            Scribe_Values.Look(ref nextActivityTick, "nextActivityTick");
            Scribe_Values.Look(ref shoppingUntilTick, "shoppingUntilTick");
            Scribe_Values.Look(ref lastShopId, "lastShopId", -1);
            Scribe_Values.Look(ref foodSearchFailedTick, "foodSearchFailedTick", -1);
            Scribe_Values.Look(ref receptionDone, "receptionDone");
            Scribe_Values.Look(ref checkoutDone, "checkoutDone");
            Scribe_Values.Look(ref reviewRecorded, "reviewRecorded");
            Scribe_Values.Look(ref foodShopping, "foodShopping");
            Scribe_Values.Look(ref dailyFoodBudget, "dailyFoodBudget", 70);
            Scribe_Values.Look(ref financeDay, "financeDay", -1);
            Scribe_Values.Look(ref optionalDailyLimit, "optionalDailyLimit");
            Scribe_Values.Look(ref optionalSpent, "optionalSpent");
            Scribe_Values.Look(ref experienceDay, "experienceDay", -1);
            Scribe_Values.Look(ref sleepRestGained, "sleepRestGained");
            Scribe_Values.Look(ref lastSleepRest, "lastSleepRest", -1);
            Scribe_Values.Look(ref sleepRewarded, "sleepRewarded");
            Scribe_Values.Look(ref foodPenaltyDay, "foodPenaltyDay", -1);
            Scribe_Values.Look(ref bedPenaltyDay, "bedPenaltyDay", -1);
            Scribe_Values.Look(ref totalSpent, "totalSpent");
        }
    }
}
