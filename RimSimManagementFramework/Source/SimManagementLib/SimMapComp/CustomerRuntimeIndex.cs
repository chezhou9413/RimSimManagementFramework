using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimMapComp
{
    //类职责：维护地图内顾客、所属 Lord、目标和阶段的 O(1) 索引，并排除正在离店的容量占用。
    internal sealed class CustomerRuntimeIndex
    {
        private readonly Map map;
        private readonly Dictionary<int, CustomerRuntimeRecord> records = new Dictionary<int, CustomerRuntimeRecord>();
        private readonly Dictionary<int, int> shopCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, int> vendingCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, int> pendingCheckoutCounts = new Dictionary<int, int>();
        private readonly List<int> recordIds = new List<int>();
        private int reconcileCursor;

        public CustomerCheckoutQueueRegistry CheckoutQueue { get; }
        public int TotalCount => records.Count;

        //构建阶段统计，职责是供调试报告读取而不重新扫描地图 Pawn 或 Lord。
        public string BuildStageDiagnostics()
        {
            Dictionary<CustomerVisitStage, int> counts = new Dictionary<CustomerVisitStage, int>();
            foreach (CustomerRuntimeRecord record in records.Values)
            {
                counts.TryGetValue(record.Stage, out int count);
                counts[record.Stage] = count + 1;
            }
            List<string> parts = new List<string>();
            foreach (CustomerVisitStage stage in Enum.GetValues(typeof(CustomerVisitStage)))
            {
                if (counts.TryGetValue(stage, out int count) && count > 0)
                    parts.Add(stage + "=" + count);
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "无";
        }

        //返回最老无进展时长，职责是从已索引 Session 读取而不扫描地图对象。
        public int GetOldestNoProgressTicks()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int oldest = 0;
            foreach (CustomerRuntimeRecord record in records.Values)
            {
                if (record?.Visit == null || record.Pawn == null) continue;
                CustomerVisitSession session = record.Visit.GetOrCreateSession(record.Pawn);
                if (session?.LastProgressTick >= 0)
                    oldest = Math.Max(oldest, now - session.LastProgressTick);
            }
            return oldest;
        }

        //创建地图顾客索引，职责是从当前 Lord 一次性重建而不恢复易失缓存。
        public CustomerRuntimeIndex(Map map)
        {
            this.map = map;
            CheckoutQueue = new CustomerCheckoutQueueRegistry(map);
            RebuildFromLords();
        }

        //返回指定商店未进入离店阶段的顾客数。
        public int CountActiveForShop(int shopId)
        {
            return shopId >= 0 && shopCounts.TryGetValue(shopId, out int count) ? count : 0;
        }

        //返回指定自动售货机仍处于访问阶段的顾客数。
        public int CountActiveForVendingMachine(int thingId)
        {
            return thingId >= 0 && vendingCounts.TryGetValue(thingId, out int count) ? count : 0;
        }

        //返回指定商店处于等待结账或结账阶段的顾客数。
        public int CountPendingCheckoutForShop(int shopId)
        {
            return shopId >= 0 && pendingCheckoutCounts.TryGetValue(shopId, out int count) ? count : 0;
        }

        //登记普通商店顾客，职责是用 Pawn ID 幂等覆盖旧记录并同步容量计数。
        public void RegisterShopCustomer(Pawn pawn, LordJob_CustomerVisit visit)
        {
            if (pawn == null || visit == null) return;
            CustomerVisitSession session = visit.GetOrCreateSession(pawn);
            Register(new CustomerRuntimeRecord
            {
                Pawn = pawn,
                Visit = visit,
                ShopId = session?.CurrentShopZoneId ?? visit.targetShopZoneId,
                Stage = session?.Stage ?? CustomerVisitStage.Arriving,
                VendingThingId = -1
            });
        }

        //登记自动售货机顾客，职责是幂等维护机器容量计数。
        public void RegisterVendingCustomer(Pawn pawn, LordJob_VendingMachineVisit visit)
        {
            if (pawn == null || visit == null) return;
            Register(new CustomerRuntimeRecord
            {
                Pawn = pawn,
                VendingVisit = visit,
                ShopId = -1,
                VendingThingId = visit.vendingMachineThingId,
                Stage = CustomerVisitStage.Arriving
            });
        }

        //移除指定顾客，职责是立即释放商店或售货机容量及结账票据。
        public void Unregister(Pawn pawn)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0 || !records.TryGetValue(pawnId, out CustomerRuntimeRecord record)) return;
            RemoveCount(record);
            records.Remove(pawnId);
            recordIds.Remove(pawnId);
            CheckoutQueue.ReleasePawn(pawnId);
        }

        //分片核对顾客阶段和存活状态，职责是确保最多 120 tick 覆盖全部索引记录。
        public void Tick()
        {
            CheckoutQueue.Tick();
            int count = recordIds.Count;
            if (count == 0) return;
            int budget = Math.Max(4, (count + 119) / 120);
            while (budget-- > 0 && recordIds.Count > 0)
            {
                if (reconcileCursor >= recordIds.Count) reconcileCursor = 0;
                int pawnId = recordIds[reconcileCursor];
                if (!records.TryGetValue(pawnId, out CustomerRuntimeRecord record))
                {
                    recordIds.RemoveAt(reconcileCursor);
                    continue;
                }

                if (!RefreshRecord(record))
                {
                    RemoveCount(record);
                    records.Remove(pawnId);
                    recordIds.RemoveAt(reconcileCursor);
                    CheckoutQueue.ReleasePawn(pawnId);
                    continue;
                }

                reconcileCursor++;
            }
        }

        //从地图 Lord 重建顾客记录，职责是只在组件初始化或读档后执行一次全 Lord 扫描。
        private void RebuildFromLords()
        {
            List<Lord> lords = map?.lordManager?.lords;
            if (lords == null) return;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (lord?.LordJob is LordJob_CustomerVisit visit)
                {
                    RegisterLordPawns(lord, pawn => RegisterShopCustomer(pawn, visit));
                    continue;
                }
                if (lord?.LordJob is LordJob_VendingMachineVisit vending)
                    RegisterLordPawns(lord, pawn => RegisterVendingCustomer(pawn, vending));
            }
        }

        //登记一个 Lord 的有效 Pawn，职责是过滤读档中的死亡或已离图引用。
        private void RegisterLordPawns(Lord lord, Action<Pawn> register)
        {
            if (lord?.ownedPawns == null || register == null) return;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn != null && pawn.Spawned && !pawn.Destroyed && !pawn.Dead)
                {
                    register(pawn);
                    RebuildCheckoutTicket(pawn);
                }
            }
        }

        //从 Pawn 当前或排队 Job 重建结账票据，职责是让读档后的运行结账继续保持原顺序。
        private void RebuildCheckoutTicket(Pawn pawn)
        {
            Job job = FindCheckoutJob(pawn);
            if (job?.targetA.Thing is SimThingClass.Building_CashRegister register)
                CheckoutQueue.Acquire(pawn, register);
        }

        //查找 Pawn 当前或排队的结账 Job，职责是只遍历该 Pawn 的短 Job 队列。
        internal static Job FindCheckoutJob(Pawn pawn)
        {
            if (pawn?.CurJobDef?.defName == "Customer_PayAtRegister") return pawn.CurJob;
            JobQueue queue = pawn?.jobs?.jobQueue;
            if (queue == null) return null;
            for (int i = 0; i < queue.Count; i++)
            {
                Job job = queue[i]?.job;
                if (job?.def?.defName == "Customer_PayAtRegister") return job;
            }
            return null;
        }

        //幂等登记运行记录，职责是避免同一 Pawn 重复增加容量。
        private void Register(CustomerRuntimeRecord record)
        {
            int pawnId = record?.Pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0) return;
            if (records.TryGetValue(pawnId, out CustomerRuntimeRecord old))
                RemoveCount(old);
            else
                recordIds.Add(pawnId);
            records[pawnId] = record;
            AddCount(record);
        }

        //刷新单条顾客记录，职责是用 Session 当前阶段更新容量归属。
        private bool RefreshRecord(CustomerRuntimeRecord record)
        {
            Pawn pawn = record.Pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map != map)
                return false;
            if (record.Visit != null)
            {
                CustomerVisitSession session = record.Visit.GetOrCreateSession(pawn);
                int shopId = session?.CurrentShopZoneId ?? record.Visit.targetShopZoneId;
                CustomerVisitStage stage = session?.Stage ?? CustomerVisitStage.Arriving;
                if (shopId != record.ShopId || stage != record.Stage)
                {
                    RemoveCount(record);
                    record.ShopId = shopId;
                    record.Stage = stage;
                    AddCount(record);
                }
            }
            return true;
        }

        //增加记录对应容量，职责是只统计尚未离店的有效阶段。
        private void AddCount(CustomerRuntimeRecord record)
        {
            if (record == null || record.Stage >= CustomerVisitStage.Leaving) return;
            if (record.ShopId >= 0)
            {
                Increment(shopCounts, record.ShopId, 1);
                if (record.Stage == CustomerVisitStage.WaitingCheckout || record.Stage == CustomerVisitStage.Checkout)
                    Increment(pendingCheckoutCounts, record.ShopId, 1);
            }
            else if (record.VendingThingId >= 0)
                Increment(vendingCounts, record.VendingThingId, 1);
        }

        //减少记录对应容量，职责是清理为零的键并检测负数异常。
        private void RemoveCount(CustomerRuntimeRecord record)
        {
            if (record == null || record.Stage >= CustomerVisitStage.Leaving) return;
            if (record.ShopId >= 0)
            {
                Increment(shopCounts, record.ShopId, -1);
                if (record.Stage == CustomerVisitStage.WaitingCheckout || record.Stage == CustomerVisitStage.Checkout)
                    Increment(pendingCheckoutCounts, record.ShopId, -1);
            }
            else if (record.VendingThingId >= 0)
                Increment(vendingCounts, record.VendingThingId, -1);
        }

        //调整计数字典，职责是集中保证容量计数不会变为负数。
        private static void Increment(Dictionary<int, int> counts, int key, int delta)
        {
            counts.TryGetValue(key, out int value);
            value += delta;
            if (value < 0)
            {
                Log.Error("[SimShop] 顾客容量索引出现负数，目标ID=" + key);
                value = 0;
            }
            if (value == 0) counts.Remove(key);
            else counts[key] = value;
        }
    }

    //类职责：保存一位顾客的易失索引数据，不参与存档。
    internal sealed class CustomerRuntimeRecord
    {
        public Pawn Pawn;
        public LordJob_CustomerVisit Visit;
        public LordJob_VendingMachineVisit VendingVisit;
        public int ShopId = -1;
        public int VendingThingId = -1;
        public CustomerVisitStage Stage;
    }
}
