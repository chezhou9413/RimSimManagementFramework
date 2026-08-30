using RimWorld;
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
    //类职责：管理自动售货机顾客的购买状态、全程进度期限和可靠离店。
    public partial class LordJob_VendingMachineVisit : LordJob
    {
        public CustomerKindDef customerKind;
        public string customerKindId = "";
        public int vendingMachineThingId = -1;
        public IntVec3 vendingCell;
        public int totalBudget;
        public Dictionary<int, CustomerRuntimeSettings> pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();
        public Dictionary<int, List<CustomerCartItem>> deliveredItems = new Dictionary<int, List<CustomerCartItem>>();
        private int visitStartTick = -1;
        private int lastProgressTick = -1;
        private IntVec3 lastProgressCell = IntVec3.Invalid;
        private int lastJobLoadId = -1;
        private int exitRequestedTick = -1;
        private int exitRecoveryCount;
        private int unsafeSinceTick = -1;

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
        //创建顾客到自动售货机购买后直接离开的状态图。
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
        //读写自动售货机访问状态。
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
            Scribe_Values.Look(ref visitStartTick, "visitStartTick", -1);
            Scribe_Values.Look(ref lastProgressTick, "lastProgressTick", -1);
            Scribe_Values.Look(ref lastProgressCell, "lastProgressCell", IntVec3.Invalid);
            Scribe_Values.Look(ref lastJobLoadId, "lastJobLoadId", -1);
            Scribe_Values.Look(ref exitRequestedTick, "exitRequestedTick", -1);
            Scribe_Values.Look(ref exitRecoveryCount, "exitRecoveryCount", 0);
            Scribe_Values.Look(ref unsafeSinceTick, "unsafeSinceTick", -1);

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

        //周期检查自动售货机顾客，职责是处理目标失效、无进展、异常状态和离店超时。
        public override void LordJobTick()
        {
            base.LordJobTick();
            if (Find.TickManager.TicksGame % 60 != 0) return;
            if (lord?.ownedPawns == null) return;
            int now = Find.TickManager.TicksGame;
            bool largeRaidActive = CustomerSafetyUtility.IsLargeHostileRaidActive(lord.Map);
            Building_SimContainer machine = ResolveVendingMachine();
            bool machineInvalid = !VendingMachineUtility.IsUsableVendingMachine(machine);

            for (int i = lord.ownedPawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                    continue;

                CustomerNeedUtility.StabilizeCustomerNeeds(pawn);
                if (largeRaidActive)
                {
                    ForcePawnFleeLargeRaid(pawn);
                    continue;
                }

                InitializeProgress(pawn, now);
                ObserveProgress(pawn, now);
                bool unsafePawn = pawn.Downed || pawn.InMentalState || pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) == false;
                if (unsafePawn)
                {
                    if (unsafeSinceTick < 0) unsafeSinceTick = now;
                    if (now - unsafeSinceTick >= 300)
                    {
                        CustomerExitUtility.ForceExit(pawn, "自动售货机顾客无法移动超过宽限时间");
                        continue;
                    }
                }
                else unsafeSinceTick = -1;

                int maxVisitTicks = Tool.CustomerCatalog.GetKind(customerKindId)?.shoppingBehavior?.maxTotalVisitTicks ?? 18000;
                if (machineInvalid || (maxVisitTicks > 0 && now - visitStartTick >= maxVisitTicks))
                {
                    pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, true);
                    NotifyDone();
                }

                if (exitRequestedTick >= 0 && now - lastProgressTick >= 600)
                {
                    if (exitRecoveryCount <= 0)
                    {
                        exitRecoveryCount++;
                        lastProgressTick = now;
                        pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, true);
                        lord.CurLordToil?.UpdateAllDuties();
                        SimDebugLogger.Journey("RSMF.CustomerExit", "自动售货机顾客离店无进展，重新下发离图职责", pawn, null, -1);
                        continue;
                    }

                    CustomerExitUtility.ForceExit(pawn, "自动售货机顾客重新取得离图职责后仍无进展");
                    continue;
                }

                if (exitRequestedTick < 0 && now - lastProgressTick >= 600)
                {
                    pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, true);
                    NotifyDone();
                }
            }
        }
        //为指定顾客保存运行时预算和偏好数据。
        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            if (pawnId <= 0 || settings == null) return;
            settings.EnsureDefaults();
            pawnSettings[pawnId] = settings;
        }
        //返回指定顾客预算。
        public int GetBudgetForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.budget > 0)
                return settings.budget;
            return totalBudget > 0 ? totalBudget : 1;
        }
        //返回指定顾客对商品的偏好倍率。
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
        //返回指定顾客的价格敏感度，负责让自动售货机购物使用同一套默认兼容参数。
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

        //记录自动售货机已交付商品，负责紧急离店时丢弃。
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
        //通知访问流程已经结束。
        public void NotifyDone()
        {
            if (exitRequestedTick < 0)
                exitRequestedTick = Find.TickManager?.TicksGame ?? 0;
            if (lord?.ownedPawns != null)
            {
                for (int i = 0; i < lord.ownedPawns.Count; i++)
                {
                    Pawn pawn = lord.ownedPawns[i];
                    pawn?.Map?.GetComponent<SimMapComp.CustomerArrivalManager>()?.UnregisterCustomer(pawn);
                }
            }
            lord?.ReceiveMemo("VendingMachine_Done");
        }

        //强制自动售货机顾客放弃购物并快速离开地图。
        private void ForcePawnFleeLargeRaid(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || lord == null)
                return;

            CustomerExitUtility.BeginEmergencyExit(pawn, "地图发生大规模敌对袭击，自动售货机顾客离店");
        }

        //解析目标自动售货机，职责是只检查缓存目标格而不扫描全图 Thing。
        private Building_SimContainer ResolveVendingMachine()
        {
            if (lord?.Map == null || !vendingCell.IsValid || !vendingCell.InBounds(lord.Map)) return null;
            List<Thing> things = lord.Map.thingGrid.ThingsListAt(vendingCell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building_SimContainer machine && machine.thingIDNumber == vendingMachineThingId)
                    return machine;
            }
            return null;
        }

        //初始化访问进度时钟，职责是让旧存档在当前 tick 起算而不被立即误判超时。
        private void InitializeProgress(Pawn pawn, int now)
        {
            if (visitStartTick < 0) visitStartTick = now;
            if (lastProgressTick < 0) lastProgressTick = now;
            if (!lastProgressCell.IsValid) lastProgressCell = pawn.Position;
        }

        //观察位置和 Job 变化，职责是记录自动售货机访问的有效进展。
        private void ObserveProgress(Pawn pawn, int now)
        {
            int jobLoadId = pawn.CurJob?.loadID ?? -1;
            bool moved = pawn.Position != lastProgressCell;
            bool jobChanged = jobLoadId != lastJobLoadId;
            if (!moved && !jobChanged) return;
            lastProgressCell = pawn.Position;
            lastJobLoadId = jobLoadId;
            if (!moved && exitRecoveryCount > 0)
                return;
            lastProgressTick = now;
            if (moved)
                exitRecoveryCount = 0;
        }
    }
}
