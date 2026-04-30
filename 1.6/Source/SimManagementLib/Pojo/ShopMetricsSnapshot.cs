namespace SimManagementLib.Pojo
{
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
        public int dynamicCapacity;
        public float spawnDemandFactor;
    }
}
