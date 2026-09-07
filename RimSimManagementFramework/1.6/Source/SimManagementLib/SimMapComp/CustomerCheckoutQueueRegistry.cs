using SimManagementLib.SimThingClass;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //类职责：按收银台维护地图级幂等结账票据，提供 O(1) 人数和稳定队首查询。
    internal sealed class CustomerCheckoutQueueRegistry
    {
        private const int StaleTicketTicks = 30;
        private readonly Map map;
        private readonly Dictionary<int, CustomerCheckoutTicket> byPawn = new Dictionary<int, CustomerCheckoutTicket>();
        private readonly Dictionary<int, LinkedList<CustomerCheckoutTicket>> byRegister = new Dictionary<int, LinkedList<CustomerCheckoutTicket>>();
        private long nextSequence;
        public int TicketCount => byPawn.Count;

        //创建结账票据登记器，职责是保存所属地图用于失效检查。
        public CustomerCheckoutQueueRegistry(Map map)
        {
            this.map = map;
        }

        //为 Pawn 取得幂等票据，职责是目标收银台变化时先释放旧票据。
        public CustomerCheckoutTicket Acquire(Pawn pawn, Building_CashRegister register)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            int registerId = register?.thingIDNumber ?? -1;
            if (pawnId <= 0 || registerId <= 0) return null;
            if (byPawn.TryGetValue(pawnId, out CustomerCheckoutTicket existing))
            {
                if (existing.RegisterId == registerId)
                {
                    existing.LastSeenTick = Find.TickManager?.TicksGame ?? 0;
                    return existing;
                }
                ReleasePawn(pawnId);
            }

            CustomerCheckoutTicket ticket = new CustomerCheckoutTicket
            {
                Pawn = pawn,
                PawnId = pawnId,
                Register = register,
                RegisterId = registerId,
                Sequence = ++nextSequence,
                LastSeenTick = Find.TickManager?.TicksGame ?? 0
            };
            if (!byRegister.TryGetValue(registerId, out LinkedList<CustomerCheckoutTicket> queue))
            {
                queue = new LinkedList<CustomerCheckoutTicket>();
                byRegister[registerId] = queue;
            }
            ticket.Node = queue.AddLast(ticket);
            byPawn[pawnId] = ticket;
            return ticket;
        }

        //刷新 Pawn 票据活跃时间，职责是供运行 Job 防止票据被看门狗误清。
        public void Touch(int pawnId)
        {
            if (byPawn.TryGetValue(pawnId, out CustomerCheckoutTicket ticket))
                ticket.LastSeenTick = Find.TickManager?.TicksGame ?? 0;
        }

        //返回指定收银台票据数。
        public int CountForRegister(Building_CashRegister register)
        {
            int registerId = register?.thingIDNumber ?? -1;
            return registerId >= 0 && byRegister.TryGetValue(registerId, out LinkedList<CustomerCheckoutTicket> queue) ? queue.Count : 0;
        }

        //返回当前 Pawn 前方人数，职责是沿同一收银台短队列读取稳定顺序。
        public int CountAhead(int pawnId)
        {
            if (!byPawn.TryGetValue(pawnId, out CustomerCheckoutTicket ticket) || ticket.Node == null) return 0;
            int count = 0;
            LinkedListNode<CustomerCheckoutTicket> node = ticket.Node.Previous;
            while (node != null)
            {
                count++;
                node = node.Previous;
            }
            return count;
        }

        //判断 Pawn 是否为对应收银台队首。
        public bool IsHead(int pawnId)
        {
            if (!byPawn.TryGetValue(pawnId, out CustomerCheckoutTicket ticket) || ticket.Node?.List == null) return false;
            return ticket.Node.List.First == ticket.Node;
        }

        //释放 Pawn 票据，职责是幂等清理两个索引。
        public void ReleasePawn(int pawnId)
        {
            if (!byPawn.TryGetValue(pawnId, out CustomerCheckoutTicket ticket)) return;
            byPawn.Remove(pawnId);
            LinkedList<CustomerCheckoutTicket> queue = ticket.Node?.List;
            queue?.Remove(ticket.Node);
            ticket.Node = null;
            if (queue != null && queue.Count == 0)
                byRegister.Remove(ticket.RegisterId);
        }

        //巡检票据，职责是在 Job 清除后 30 tick 内释放失效排队状态。
        public void Tick()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now % 30 != 0 || byPawn.Count == 0) return;
            List<int> stale = null;
            foreach (KeyValuePair<int, CustomerCheckoutTicket> pair in byPawn)
            {
                CustomerCheckoutTicket ticket = pair.Value;
                Pawn pawn = ticket.Pawn;
                bool invalid = pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map != map;
                bool hasCheckoutJob = CustomerRuntimeIndex.FindCheckoutJob(pawn) != null;
                if (invalid || (!hasCheckoutJob && now - ticket.LastSeenTick >= StaleTicketTicks))
                {
                    if (stale == null) stale = new List<int>();
                    stale.Add(pair.Key);
                }
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++)
                ReleasePawn(stale[i]);
        }
    }

    //类职责：保存一个 Pawn 在一个收银台的易失结账顺序。
    internal sealed class CustomerCheckoutTicket
    {
        public Pawn Pawn;
        public int PawnId;
        public Building_CashRegister Register;
        public int RegisterId;
        public long Sequence;
        public int LastSeenTick;
        public LinkedListNode<CustomerCheckoutTicket> Node;
    }
}
