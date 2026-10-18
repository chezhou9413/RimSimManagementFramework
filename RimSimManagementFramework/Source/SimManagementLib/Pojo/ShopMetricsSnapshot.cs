namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存商店经营指标的一次运行时快照，供刷客、统计和界面展示读取。
    /// </summary>
    public class ShopMetricsSnapshot
    {
        public int zoneId = -1;
        public string zoneLabel = "";

        public float score;
        public float operationScore;
        public float goodsScore;
        public float serviceScore;
        public float environmentScore;

        public float reputation;
        public float satisfaction;

        public float beautyAverage;
        public float effectiveScale;
        public float beautyDemandMultiplier;
        public float scaleDemandMultiplier;
        public float scaleCapacityMultiplier;
        public int dynamicCapacity;
        public float spawnDemandFactor;
    }
}
