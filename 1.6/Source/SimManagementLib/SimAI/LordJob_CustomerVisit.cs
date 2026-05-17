using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理一批顾客从抵达商店、浏览消费、结账到离店的群体状态和顾客运行时数据。
    /// </summary>
    public partial class LordJob_CustomerVisit : LordJob
    {
        public CustomerKindDef customerKind;
        public string customerKindId = "";
        public int targetShopZoneId = -1;
        public IntVec3 targetShopCell;
        public int totalBudget;
        public Dictionary<int, float> cartValues = new Dictionary<int, float>();
        public Dictionary<int, float> satisfactionMap = new Dictionary<int, float>();
        public Dictionary<int, List<CustomerCartItem>> cartItems = new Dictionary<int, List<CustomerCartItem>>();
        public Dictionary<int, List<CustomerServiceOrder>> serviceOrders = new Dictionary<int, List<CustomerServiceOrder>>();
        public Dictionary<int, int> consumptionActionCounts = new Dictionary<int, int>();
        public Dictionary<int, CustomerRuntimeSettings> pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();
        public Dictionary<int, int> effectiveBudgetCaps = new Dictionary<int, int>();
        public Dictionary<int, int> checkoutOrder = new Dictionary<int, int>();
        public int nextServiceOrderId = 1;
        public int nextCheckoutOrder = 1;
        public List<int> readyForCheckout = new List<int>();
        public Dictionary<int, int> browseWaitStartTick = new Dictionary<int, int>();

        // 购后 Job 队列只保留在运行时，避免把依赖地图实时对象的 Job 写入存档。
        [Unsaved] private readonly Dictionary<int, List<Job>> postCheckoutJobs = new Dictionary<int, List<Job>>();
        [Unsaved] private readonly HashSet<int> postCheckoutRequired = new HashSet<int>();

        private List<int> tmpCartItemKeys;
        private List<List<CustomerCartItem>> tmpCartItemValues;
        private List<int> tmpServiceOrderKeys;
        private List<List<CustomerServiceOrder>> tmpServiceOrderValues;
        private List<int> tmpSettingKeys;
        private List<CustomerRuntimeSettings> tmpSettingValues;
        private List<int> tmpEffectiveBudgetCapKeys;
        private List<int> tmpEffectiveBudgetCapValues;

        public LordJob_CustomerVisit()
        {
        }

        public LordJob_CustomerVisit(CustomerKindDef kind, IntVec3 shopCell, int budget)
        {
            customerKind = kind;
            customerKindId = kind?.defName ?? "";
            targetShopCell = shopCell;
            totalBudget = budget;
        }

        public LordJob_CustomerVisit(CustomerKindDef kind, int zoneId, IntVec3 shopCell, int budget)
        {
            customerKind = kind;
            customerKindId = kind?.defName ?? "";
            targetShopZoneId = zoneId;
            targetShopCell = shopCell;
            totalBudget = budget;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();

            LordToil_Travel travelToil = new LordToil_Travel(targetShopCell);
            graph.AddToil(travelToil);

            LordToil_CustomerBrowse browseToil = new LordToil_CustomerBrowse(targetShopCell);
            graph.AddToil(browseToil);

            LordToil_CustomerCheckout checkoutToil = new LordToil_CustomerCheckout(targetShopCell);
            graph.AddToil(checkoutToil);

            LordToil_ExitMap exitToil = new LordToil_ExitMap(LocomotionUrgency.Walk, canDig: false, interruptCurrentJob: true);
            graph.AddToil(exitToil);

            Transition arriveTransition = new Transition(travelToil, browseToil);
            arriveTransition.AddTrigger(new Trigger_Memo("TravelArrived"));
            graph.AddTransition(arriveTransition);

            // 所有仍在地图上的顾客都声明准备结账后，才整体切换到结账阶段。
            Transition browseToCheckout = new Transition(browseToil, checkoutToil);
            browseToCheckout.AddTrigger(new Trigger_Memo("Customer_ReadyToCheckout"));
            graph.AddTransition(browseToCheckout);

            Transition checkoutToExit = new Transition(checkoutToil, exitToil);
            checkoutToExit.AddTrigger(new Trigger_Memo("Customer_CheckoutCompleted"));
            graph.AddTransition(checkoutToExit);

            return graph;
        }
    }
}
