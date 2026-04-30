using Verse;

namespace SimManagementLib.SimDef
{
    public class ShopTuningDef : Def
    {
        // runtime/cache
        public int evaluateIntervalTicks = 120;
        public float defaultReputation = 50f;
        public float defaultSatisfaction = 50f;

        // total score weights
        public float operationWeight = 0.35f;
        public float goodsWeight = 0.25f;
        public float serviceWeight = 0.20f;
        public float environmentWeight = 0.20f;

        // environment score
        public float beautyWeight = 0.60f;
        public float cleanlinessWeight = 0.25f;
        public float indoorWeight = 0.15f;
        public FloatRange beautyRange = new FloatRange(-2f, 8f);

        // operation score
        public float operationRegisterDivisor = 3.5f;
        public float operationStorageDivisor = 8f;
        public float operationCoverageDivisorCells = 14f;
        public float operationRegisterComponentWeight = 0.45f;
        public float operationStorageComponentWeight = 0.30f;
        public float operationCoverageComponentWeight = 0.25f;

        // goods score
        public float goodsVarietyTarget = 24f;
        public float goodsComboTarget = 8f;
        public float goodsVarietyWeight = 0.45f;
        public float goodsStockWeight = 0.40f;
        public float goodsComboWeight = 0.15f;

        // service score
        public float serviceFallbackSuccessRate = 0.65f;
        public float serviceFallbackAvgWaitTicks = 900f;
        public float serviceWaitPenaltyTicks = 2800f;
        public float serviceSuccessWeight = 0.45f;
        public float serviceWaitWeight = 0.35f;
        public float serviceStaffWeight = 0.20f;

        // demand stage
        public float stageBase = 0.65f;
        public FloatRange wealthRange = new FloatRange(30000f, 500000f);
        public float wealthWeight = 0.45f;
        public float colonistTarget = 20f;
        public float colonistWeight = 0.35f;
        public float shopCountTarget = 6f;
        public float shopCountWeight = 0.25f;

        // demand multipliers
        public FloatRange qualityMultiplierRange = new FloatRange(0.45f, 1.80f);
        public FloatRange reputationMultiplierRange = new FloatRange(0.55f, 1.60f);
        public FloatRange demandFactorClamp = new FloatRange(0.20f, 4.00f);

        // capacity
        public float capacityRegisterFactor = 2.5f;
        public float capacityMannedFactor = 2.0f;
        public float capacityStorageFactor = 1.0f;
        public FloatRange capacityQualityMulRange = new FloatRange(0.65f, 1.40f);
        public FloatRange capacityReputationMulRange = new FloatRange(0.75f, 1.20f);
        public int capacityMin = 2;
        public int capacityMax = 72;
        public float capacityZoneCellCapDivisor = 2f;

        // satisfaction scoring
        public float satisfactionQueueWeight = 0.25f;
        public float satisfactionBudgetWeight = 0.20f;
        public float satisfactionGoodsWeight = 0.20f;
        public float satisfactionEnvironmentWeight = 0.15f;
        public float satisfactionCompletionWeight = 0.20f;

        public float satisfactionNoBuyBase = 35f;
        public float satisfactionTimeoutNoBuy = 10f;
        public float satisfactionPaidGoods = 90f;
        public float satisfactionCompletionSuccess = 100f;
        public float satisfactionCompletionFail = 30f;
        public float satisfactionCompletionTimeout = 5f;
        public float satisfactionBudgetBase = 40f;
        public float satisfactionBudgetScale = 60f;
        public float satisfactionBudgetTargetUsage = 0.75f;

        // EMA update
        public float satisfactionEmaOldWeight = 0.80f;
        public float satisfactionEmaNewWeight = 0.20f;
        public float reputationEmaOldWeight = 0.97f;
        public float reputationEmaNewWeight = 0.03f;
    }
}
