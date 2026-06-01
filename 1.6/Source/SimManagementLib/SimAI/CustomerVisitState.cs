using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 保存单个顾客的跨店访问状态，负责记录当前店铺、已访问店铺和跨店消费进度。
    /// </summary>
    public class CustomerVisitState : IExposable
    {
        public int pawnId = -1;
        public int currentShopZoneId = -1;
        public IntVec3 currentShopCell = IntVec3.Invalid;
        public int currentShopVisitStartTick = -1;
        public int totalVisitStartTick = -1;
        public float totalSpentAcrossShops;
        public float desiredSpendRatio = -1f;
        public int currentShopConsumptionActions;
        public int currentShopBrowseAttempts;
        public int currentShopNoProgressBrowseAttempts;
        public bool currentShopMinimumBrowseDone;
        public List<int> visitedShopZoneIds = new List<int>();

        /// <summary>
        /// 读写顾客跨店访问状态，负责兼容旧存档缺少集合字段的情况。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId", -1);
            Scribe_Values.Look(ref currentShopZoneId, "currentShopZoneId", -1);
            Scribe_Values.Look(ref currentShopCell, "currentShopCell", IntVec3.Invalid);
            Scribe_Values.Look(ref currentShopVisitStartTick, "currentShopVisitStartTick", -1);
            Scribe_Values.Look(ref totalVisitStartTick, "totalVisitStartTick", -1);
            Scribe_Values.Look(ref totalSpentAcrossShops, "totalSpentAcrossShops", 0f);
            Scribe_Values.Look(ref desiredSpendRatio, "desiredSpendRatio", -1f);
            Scribe_Values.Look(ref currentShopConsumptionActions, "currentShopConsumptionActions", 0);
            Scribe_Values.Look(ref currentShopBrowseAttempts, "currentShopBrowseAttempts", 0);
            Scribe_Values.Look(ref currentShopNoProgressBrowseAttempts, "currentShopNoProgressBrowseAttempts", 0);
            Scribe_Values.Look(ref currentShopMinimumBrowseDone, "currentShopMinimumBrowseDone", false);
            Scribe_Collections.Look(ref visitedShopZoneIds, "visitedShopZoneIds", LookMode.Value);
            if (visitedShopZoneIds == null)
                visitedShopZoneIds = new List<int>();
        }
    }
}
