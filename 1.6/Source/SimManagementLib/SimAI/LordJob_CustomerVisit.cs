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
        public CustomerCartState cartState = new CustomerCartState();
        public CustomerServiceOrderState serviceOrderState = new CustomerServiceOrderState();
        public CustomerCheckoutState checkoutState = new CustomerCheckoutState();
        public CustomerPawnSettingsState pawnSettingsState = new CustomerPawnSettingsState();

        public Dictionary<int, float> cartValues => cartState.cartValues;
        public Dictionary<int, float> satisfactionMap => cartState.satisfactionMap;
        public Dictionary<int, List<CustomerCartItem>> cartItems => cartState.cartItems;
        public Dictionary<int, List<CustomerServiceOrder>> serviceOrders => serviceOrderState.serviceOrders;
        public Dictionary<int, int> consumptionActionCounts => cartState.consumptionActionCounts;
        public Dictionary<int, CustomerRuntimeSettings> pawnSettings => pawnSettingsState.pawnSettings;
        public Dictionary<int, int> effectiveBudgetCaps => cartState.effectiveBudgetCaps;
        public Dictionary<int, int> checkoutOrder => checkoutState.checkoutOrder;
        public int nextServiceOrderId
        {
            get => serviceOrderState.nextServiceOrderId;
            set => serviceOrderState.nextServiceOrderId = value;
        }
        public int nextCheckoutOrder
        {
            get => checkoutState.nextCheckoutOrder;
            set => checkoutState.nextCheckoutOrder = value;
        }
        public List<int> readyForCheckout => checkoutState.readyForCheckout;
        public Dictionary<int, int> browseWaitStartTick => cartState.browseWaitStartTick;

        /// <summary>
        /// 确保拆分后的状态对象均存在，负责兼容旧存档和运行时反序列化后的空引用。
        /// </summary>
        private void EnsureStateObjects()
        {
            if (cartState == null) cartState = new CustomerCartState();
            if (serviceOrderState == null) serviceOrderState = new CustomerServiceOrderState();
            if (checkoutState == null) checkoutState = new CustomerCheckoutState();
            if (pawnSettingsState == null) pawnSettingsState = new CustomerPawnSettingsState();
        }

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
