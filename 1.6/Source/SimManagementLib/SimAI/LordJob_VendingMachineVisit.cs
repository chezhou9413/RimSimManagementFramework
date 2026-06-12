using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
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
        public Dictionary<int, List<CustomerCartItem>> deliveredItems = new Dictionary<int, List<CustomerCartItem>>();

        private List<int> tmpSettingKeys;
        private List<CustomerRuntimeSettings> tmpSettingValues;
        private List<int> tmpDeliveredItemKeys;
        private List<List<CustomerCartItem>> tmpDeliveredItemValues;

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
            Scribe_Collections.Look(ref deliveredItems, "deliveredItems", LookMode.Value, LookMode.Deep, ref tmpDeliveredItemKeys, ref tmpDeliveredItemValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawnSettings == null)
                    pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();
                if (deliveredItems == null)
                    deliveredItems = new Dictionary<int, List<CustomerCartItem>>();
                foreach (CustomerRuntimeSettings settings in pawnSettings.Values)
                    settings?.EnsureDefaults();
            }
        }

        // 周期性检查自动售货机顾客安全，负责在大规模敌对袭击时放弃购物并快速离图。
        public override void LordJobTick()
        {
            base.LordJobTick();
            if (Find.TickManager.TicksGame % 60 != 0) return;
            if (!CustomerSafetyUtility.IsLargeHostileRaidActive(lord?.Map)) return;
            if (lord?.ownedPawns == null) return;

            for (int i = lord.ownedPawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                    continue;

                ForcePawnFleeLargeRaid(pawn);
            }
        }

        /// <summary>
        /// 为指定顾客保存运行时预算和偏好数据。
        /// </summary>
        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            if (pawnId <= 0 || settings == null) return;
            settings.EnsureDefaults();
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
        /// 返回指定顾客的价格敏感度，负责让自动售货机购物使用同一套默认兼容参数。
        /// </summary>
        public CustomerPriceSensitivityProps GetPriceSensitivity(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null)
            {
                settings.EnsureDefaults();
                return CustomerPriceSensitivityProps.Resolve(settings.priceSensitivity);
            }

            RuntimeCustomerKind runtime = Tool.CustomerCatalog.GetKind(customerKindId);
            return CustomerPriceSensitivityProps.Resolve(runtime?.priceSensitivity);
        }

        // 记录自动售货机已交付商品，负责紧急离店时丢弃。
        public void RecordDeliveredItem(int pawnId, ThingDef def, int count)
        {
            if (pawnId <= 0 || def == null || count <= 0)
                return;

            if (deliveredItems == null)
                deliveredItems = new Dictionary<int, List<CustomerCartItem>>();
            if (!deliveredItems.TryGetValue(pawnId, out List<CustomerCartItem> list))
            {
                list = new List<CustomerCartItem>();
                deliveredItems[pawnId] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CustomerCartItem item = list[i];
                if (item == null || item.def != def)
                    continue;

                item.count += count;
                return;
            }

            list.Add(new CustomerCartItem { def = def, count = count });
        }

        /// <summary>
        /// 通知访问流程已经结束。
        /// </summary>
        public void NotifyDone()
        {
            lord?.ReceiveMemo("VendingMachine_Done");
        }

        // 强制自动售货机顾客放弃购物并快速离开地图。
        private void ForcePawnFleeLargeRaid(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || lord == null)
                return;

            int pawnId = pawn.thingIDNumber;
            List<CustomerCartItem> items = null;
            deliveredItems?.TryGetValue(pawnId, out items);
            CustomerSafetyUtility.DropDeliveredItems(pawn, items);
            deliveredItems?.Remove(pawnId);
            if (pawn.jobs != null)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false, true);

            Lord oldLord = lord;
            oldLord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            if (pawn.Spawned && !pawn.Dead && !pawn.Destroyed && pawn.Map != null)
            {
                LordMaker.MakeNewLord(
                    pawn.Faction,
                    new LordJob_ExitMapBest(LocomotionUrgency.Sprint, canDig: false, canDefendSelf: false),
                    pawn.Map,
                    new[] { pawn });
            }
        }
    }
}
