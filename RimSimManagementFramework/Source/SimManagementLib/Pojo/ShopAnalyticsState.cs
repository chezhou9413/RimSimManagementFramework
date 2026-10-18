using Verse;

namespace SimManagementLib.Pojo
{
    //类职责：保存单个商店的持久经营统计和运行时指标缓存状态。
    public class ShopAnalyticsState : IExposable
    {
        public string label = "";
        public float reputation;
        public float satisfactionEma;
        public int successfulCheckouts;
        public int timeoutCheckouts;
        public float queueWaitTicksTotal;
        public int queueWaitSamples;
        public float lastScore;
        public float lastBeauty;
        public float lastEnvironment;

        [Unsaved]
        public int lastEvaluateTick = -1;

        [Unsaved]
        public ShopMetricsSnapshot cachedMetrics;

        [Unsaved]
        public bool metricsDirty = true;

        //读写商店经营统计，职责是仅持久化跨存档需要保留的数据。
        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref reputation, "reputation", 0f);
            Scribe_Values.Look(ref satisfactionEma, "satisfactionEma", 0f);
            Scribe_Values.Look(ref successfulCheckouts, "successfulCheckouts", 0);
            Scribe_Values.Look(ref timeoutCheckouts, "timeoutCheckouts", 0);
            Scribe_Values.Look(ref queueWaitTicksTotal, "queueWaitTicksTotal", 0f);
            Scribe_Values.Look(ref queueWaitSamples, "queueWaitSamples", 0);
            Scribe_Values.Look(ref lastScore, "lastScore", 0f);
            Scribe_Values.Look(ref lastBeauty, "lastBeauty", 0f);
            Scribe_Values.Look(ref lastEnvironment, "lastEnvironment", 0f);
        }
    }
}
