using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimAI.CustomerVisit;
using System.Collections.Generic;
using System.Linq;
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
        internal CustomerCartState cartState = new CustomerCartState();
        internal CustomerServiceOrderState serviceOrderState = new CustomerServiceOrderState();
        internal CustomerCheckoutState checkoutState = new CustomerCheckoutState();
        internal CustomerPawnSettingsState pawnSettingsState = new CustomerPawnSettingsState();
        internal Dictionary<int, CustomerVisitSession> visitSessions = new Dictionary<int, CustomerVisitSession>();

        internal Dictionary<int, float> cartValues => cartState.cartValues;
        internal Dictionary<int, float> satisfactionMap => cartState.satisfactionMap;
        internal Dictionary<int, List<CustomerCartItem>> cartItems => cartState.cartItems;
        internal Dictionary<int, List<CustomerServiceOrder>> serviceOrders => serviceOrderState.serviceOrders;
        internal Dictionary<int, int> consumptionActionCounts => cartState.consumptionActionCounts;
        internal Dictionary<int, CustomerRuntimeSettings> pawnSettings => pawnSettingsState.pawnSettings;
        internal Dictionary<int, int> effectiveBudgetCaps => cartState.effectiveBudgetCaps;
        internal Dictionary<int, int> checkoutOrder => checkoutState.checkoutOrder;
        internal Dictionary<int, CustomerVisitSession> customerVisitSessions => visitSessions;
        internal int nextServiceOrderId
        {
            get => serviceOrderState.nextServiceOrderId;
            set => serviceOrderState.nextServiceOrderId = value;
        }
        internal int nextCheckoutOrder
        {
            get => checkoutState.nextCheckoutOrder;
            set => checkoutState.nextCheckoutOrder = value;
        }
        internal List<int> readyForCheckout => checkoutState.readyForCheckout;
        internal Dictionary<int, int> browseWaitStartTick => cartState.browseWaitStartTick;

        /// <summary>
        /// 确保拆分后的状态对象均存在，负责处理运行时反序列化后的空引用。
        /// </summary>
        internal void EnsureStateObjectsForServices()
        {
            if (cartState == null) cartState = new CustomerCartState();
            if (serviceOrderState == null) serviceOrderState = new CustomerServiceOrderState();
            if (checkoutState == null) checkoutState = new CustomerCheckoutState();
            if (pawnSettingsState == null) pawnSettingsState = new CustomerPawnSettingsState();
            if (visitSessions == null) visitSessions = new Dictionary<int, CustomerVisitSession>();
        }

        /// <summary>
        /// 确保拆分后的状态对象均存在，负责处理运行时反序列化后的空引用。
        /// </summary>
        private void EnsureStateObjects()
        {
            EnsureStateObjectsForServices();
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

            LordToil_CustomerTravel travelToil = new LordToil_CustomerTravel();
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

            Transition checkoutToNextShop = new Transition(checkoutToil, travelToil);
            checkoutToNextShop.AddTrigger(new Trigger_Memo("Customer_GoToNextShop"));
            graph.AddTransition(checkoutToNextShop, highPriority: true);

            return graph;
        }

        /// <summary>
        /// 返回当前活跃顾客，负责为单顾客和未来多顾客队伍提供统一入口。
        /// </summary>
        public Pawn FirstActivePawn()
        {
            return lord?.ownedPawns?
                .FirstOrDefault(pawn => pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned);
        }

        /// <summary>
        /// 获取或创建指定顾客的 Session，负责让 Lord、JobGiver、JobDriver 和 API 使用统一状态入口。
        /// </summary>
        public CustomerVisitSession GetOrCreateSession(Pawn pawn)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0) return null;
            EnsureStateObjects();
            if (!visitSessions.TryGetValue(pawnId, out CustomerVisitSession session) || session == null)
            {
                session = new CustomerVisitSession
                {
                    pawnId = pawnId
                };
                visitSessions[pawnId] = session;
            }
            session.Initialize(this, pawn);
            return session;
        }
    }
}
