using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 管理自动售货机顾客从抵达机器、购买付款到离图的独立访问流程。
    /// </summary>
    public class LordJob_VendingMachineVisit : LordJob
    {
        public CustomerKindDef customerKind;
        public string customerKindId = "";
        public int vendingMachineThingId = -1;
        public IntVec3 vendingCell;
        public int totalBudget;
        public Dictionary<int, CustomerRuntimeSettings> pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();

        private List<int> tmpSettingKeys;
        private List<CustomerRuntimeSettings> tmpSettingValues;

        public LordJob_VendingMachineVisit()
        {
        }

        public LordJob_VendingMachineVisit(CustomerKindDef kind, Building_SimContainer vendingMachine, int budget)
        {
            customerKind = kind;
            customerKindId = kind?.defName ?? "";
            vendingMachineThingId = vendingMachine?.thingIDNumber ?? -1;
            vendingCell = vendingMachine?.Position ?? IntVec3.Invalid;
            totalBudget = budget;
        }

        /// <summary>
        /// 创建顾客到自动售货机购买后直接离开的状态图。
        /// </summary>
        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();

            LordToil_Travel travel = new LordToil_Travel(vendingCell);
            graph.AddToil(travel);

            LordToil_VendingMachineUse use = new LordToil_VendingMachineUse(vendingCell);
            graph.AddToil(use);

            LordToil_ExitMap exit = new LordToil_ExitMap(LocomotionUrgency.Walk, canDig: false, interruptCurrentJob: true);
            graph.AddToil(exit);

            Transition arrive = new Transition(travel, use);
            arrive.AddTrigger(new Trigger_Memo("TravelArrived"));
            graph.AddTransition(arrive);

            Transition finished = new Transition(use, exit);
            finished.AddTrigger(new Trigger_Memo("VendingMachine_Done"));
            graph.AddTransition(finished);

            return graph;
        }

        /// <summary>
        /// 读写自动售货机访问状态。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref customerKind, "customerKind");
            Scribe_Values.Look(ref customerKindId, "customerKindId", "");
            Scribe_Values.Look(ref vendingMachineThingId, "vendingMachineThingId", -1);
            Scribe_Values.Look(ref vendingCell, "vendingCell");
            Scribe_Values.Look(ref totalBudget, "totalBudget", 0);
            Scribe_Collections.Look(ref pawnSettings, "pawnSettings", LookMode.Value, LookMode.Deep, ref tmpSettingKeys, ref tmpSettingValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawnSettings == null)
                pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();
        }

        /// <summary>
        /// 为指定顾客保存运行时预算和偏好数据。
        /// </summary>
        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            if (pawnId <= 0 || settings == null) return;
            pawnSettings[pawnId] = settings;
        }

        /// <summary>
        /// 返回指定顾客预算。
        /// </summary>
        public int GetBudgetForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.budget > 0)
                return settings.budget;
            return totalBudget > 0 ? totalBudget : 1;
        }

        /// <summary>
        /// 返回指定顾客对商品的偏好倍率。
        /// </summary>
        public float GetPreferenceMultiplier(int pawnId, ThingDef def)
        {
            float multiplier = 1f;
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null)
                multiplier *= settings.GetPreferenceMultiplier(def);

            RuntimeCustomerKind runtime = Tool.CustomerCatalog.GetKind(customerKindId);
            if (runtime != null)
                multiplier *= runtime.GetPreferenceMultiplier(def);

            return multiplier;
        }

        /// <summary>
        /// 通知访问流程已经结束。
        /// </summary>
        public void NotifyDone()
        {
            lord?.ReceiveMemo("VendingMachine_Done");
        }
    }
}
