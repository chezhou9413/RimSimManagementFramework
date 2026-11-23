using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Spa
{
    //持久保存泡汤队列、独占池位和每日冷却，Job 只负责推进当前步骤。
    public sealed partial class SpaMapComponent : MapComponent
    {
        private List<SpaSession> sessions = new List<SpaSession>();
        private List<SpaCooldown> cooldowns = new List<SpaCooldown>();
        private int nextId = 1;
        public IEnumerable<SpaSession> ActiveSessions => sessions.Where(s => !s.IsTerminal);
        public IReadOnlyList<SpaSession> Sessions => sessions;
        private int Now => Verse.Find.TickManager.TicksGame;
        private int LocalDay => GenLocalDate.Year(map) * 60 + GenLocalDate.DayOfYear(map);

        //由原版地图组件机制创建，避免额外启动注册。
        public SpaMapComponent(Map map) : base(map) { }

        //取得地图内唯一的温泉管理器。
        public static SpaMapComponent For(Map map) => map?.GetComponent<SpaMapComponent>();

        //按持久编号查询会话。
        public SpaSession Find(int id) => sessions.FirstOrDefault(s => s.id == id);

        //统计已经占用的位置，不把尚未取得柜位的排队者算作在池内。
        public int OccupiedCount(Building_HotelSpa spa) => sessions.Count(s => s.spa == spa && s.HasReservedSlot);

        //统计正在等待池位和柜位的顾客。
        public int WaitingCount(Building_HotelSpa spa) => sessions.Count(s => s.spa == spa && s.stage == SpaSessionStage.Queued);

        //同一自然日和最短间隔同时满足后才允许再次泡汤。
        public bool CanBathe(Pawn pawn)
        {
            if (sessions.Any(s => s.customer == pawn && !s.IsTerminal)) return false;
            SpaCooldown cooldown = cooldowns.FirstOrDefault(c => c.pawnId == pawn.thingIDNumber);
            return cooldown == null || (cooldown.localDay != LocalDay
                && Now - cooldown.usedTick >= HotelTuningDef.Current.spaCooldownTicks);
        }

        //创建候选已确定的排队会话，此时不占柜、不收款。
        public SpaSession Enqueue(Pawn pawn, Building_HotelSpa spa, Zone_Shop shop)
        {
            var hotel = HotelMapComponent.For(map);
            var session = new SpaSession
            {
                id = nextId++, customer = pawn, spa = spa, shopZoneId = shop.ID,
                ticketDesk = hotel.BestDeskFor(shop, pawn, out _) ?? hotel.DeskFor(shop),
                createdTick = Now, stageTick = Now, price = spa.TicketPrice,
                durationTicks = spa.DurationTicks, joyGain = spa.JoyGain, satisfactionGain = spa.SatisfactionGain
            };
            sessions.Add(session);
            return session;
        }

        //按先后顺序预留池位和衣柜，再前往最优服务台购票；原池满员时自动切入同店其他无人排队的空池。
        public bool TryAdmit(SpaSession session, Job job)
        {
            Pawn pawn = session.customer;
            Zone_Shop shop = session.Shop(map);
            var hotel = HotelMapComponent.For(map);
            if (shop == null || hotel.DeskFor(shop) == null)
            {
                Abort(session, "RSH.Text.SpaNeedsReceptionDesk");
                return false;
            }
            Building_HotelDesk desk = hotel.BestDeskFor(shop, pawn, out IntVec3 serviceCell);
            if (desk == null)
            {
                Abort(session, "RSH.Text.CannotReachSpaTicketDesk");
                return false;
            }
            Building_HotelLocker locker = map.listerThings.AllThings.OfType<Building_HotelLocker>()
                .Where(l => l.Available && shop.ContainsCell(l.Position)
                    && pawn.CanReserveAndReach(l, PathEndMode.Touch, Danger.None))
                .OrderBy(l => l.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
            if (locker == null) return false;

            if (!HasActiveAhead(session.spa, session.id)
                && TryReservePoolSlot(session, job, session.spa, locker, desk, serviceCell))
                return true;

            int funds = SimShopCustomerApi.GetAvailableFunds(pawn);
            float remainingBudget = SimShopCustomerApi.GetRemainingBudget(pawn, shop);
            var alternatives = map.listerThings.AllThings.OfType<Building_HotelSpa>()
                .Where(s => s != session.spa && s.Shop == shop && s.Operational
                    && s.TicketPrice >= 0 && s.TicketPrice <= funds
                    && (s.TicketPrice <= session.price || s.TicketPrice <= remainingBudget)
                    && !HasActiveQueued(s, session.id)
                    && pawn.CanReach(s, PathEndMode.Touch, Danger.None))
                .OrderBy(s => s.Position.DistanceToSquared(pawn.Position));
            foreach (Building_HotelSpa alt in alternatives)
            {
                if (TryReservePoolSlot(session, job, alt, locker, desk, serviceCell))
                    return true;
            }
            return false;
        }

        //判断指定温泉是否还有更早且仍在活跃等位的前序会话。
        private bool HasActiveAhead(Building_HotelSpa spa, int sessionId)
        {
            return sessions.Any(s => s.spa == spa && s.stage == SpaSessionStage.Queued && s.id < sessionId
                && s.customer?.CurJobDef?.defName == "RSH_UseSpa");
        }

        //判断指定温泉当前是否有其他活跃排队会话，避免改池时插队。
        private bool HasActiveQueued(Building_HotelSpa spa, int excludeSessionId)
        {
            return sessions.Any(s => s.spa == spa && s.stage == SpaSessionStage.Queued && s.id != excludeSessionId
                && s.customer?.CurJobDef?.defName == "RSH_UseSpa");
        }

        //尝试为会话预留指定温泉的空闲池位与衣柜，成功后同步更新购票前台并进入购票阶段。
        private bool TryReservePoolSlot(SpaSession session, Job job, Building_HotelSpa spa,
            Building_HotelLocker locker, Building_HotelDesk desk, IntVec3 serviceCell)
        {
            if (spa == null || !spa.Operational) return false;
            Pawn pawn = session.customer;
            for (int i = 0; i < spa.Capacity; i++)
            {
                if (sessions.Any(s => s.spa == spa && s.HasReservedSlot && s.slot == i)) continue;
                IntVec3 cell = spa.SlotCell(i);
                if (!cell.Standable(map) || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                if (!pawn.Reserve(locker, job, errorOnFailed: false)) return false;
                if (!pawn.Reserve(cell, job, errorOnFailed: false))
                {
                    map.reservationManager.Release(locker, pawn, job);
                    continue;
                }
                if (!locker.TryClaim(pawn, session.id))
                {
                    map.reservationManager.Release(locker, pawn, job);
                    map.reservationManager.Release(cell, pawn, job);
                    return false;
                }
                if (spa != session.spa)
                    SwitchSessionSpa(session, spa, job);
                session.locker = locker;
                session.slot = i;
                session.poolCell = cell;
                session.ticketDesk = desk;
                session.ticketServiceCell = serviceCell;
                session.ticketServiceTicksAtWaitStart = desk.CustomerServiceTicks;
                ChangeStage(session, SpaSessionStage.BuyingTicket);
                return true;
            }
            return false;
        }

        //在未扣款、未占池前改换温泉时，同步更新会话参数、动作订单和任务目标。
        private static void SwitchSessionSpa(SpaSession session, Building_HotelSpa spa, Job job)
        {
            session.spa = spa;
            session.price = spa.TicketPrice;
            session.durationTicks = spa.DurationTicks;
            session.joyGain = spa.JoyGain;
            session.satisfactionGain = spa.SatisfactionGain;
            job?.SetTarget(TargetIndex.A, spa);
            CustomerActionOrder order = SimShopCustomerApi.GetActionOrder(session.actionOrderId);
            if (order != null)
            {
                order.targetThingId = spa.thingIDNumber;
                order.targetLabel = spa.LabelCap;
            }
        }

        //更新流程和该步骤起点，用于限制无法完成的移动。
        public void ChangeStage(SpaSession session, SpaSessionStage stage)
        {
            session.stage = stage;
            session.stageTick = Now;
        }

        //开始使用时记录冷却，即使提前离池也不能反复领取休闲。
        private void RecordCooldown(Pawn pawn)
        {
            SpaCooldown cooldown = cooldowns.FirstOrDefault(c => c.pawnId == pawn.thingIDNumber);
            if (cooldown == null)
            {
                cooldown = new SpaCooldown { pawnId = pawn.thingIDNumber };
                cooldowns.Add(cooldown);
            }
            cooldown.usedTick = Now;
            cooldown.localDay = LocalDay;
        }

        //保存队列、进度和冷却，所有预约均能由会话重新查询。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref sessions, "spaSessions", LookMode.Deep);
            Scribe_Collections.Look(ref cooldowns, "spaCooldowns", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextSpaSessionId", 1);
        }

        //低频处理失效目标和离图顾客，避免留下永久柜位或未退票款。
        public override void MapComponentTick()
        {
            if (Now % 120 != 0) return;
            foreach (SpaSession session in ActiveSessions.ToList())
            {
                Zone_Shop shop = session.Shop(map);
                bool deskValid = session.stage == SpaSessionStage.Queued
                    ? shop != null && HotelMapComponent.For(map).DeskFor(shop) != null
                    : session.ticketDesk != null && session.ticketDesk.Spawned
                        && shop != null && shop.ContainsCell(session.ticketDesk.Position);
                if (session.customer == null || !session.customer.Spawned || session.customer.Map != map
                    || session.customer.Dead || session.customer.Downed || session.spa == null || !session.spa.Operational
                    || shop == null || session.spa.Shop != shop
                    || (!session.incomeCommitted && !deskValid)
                    || (session.locker != null && (!session.locker.Spawned || !shop.ContainsCell(session.locker.Position)))
                    || (session.actionOrderId > 0 && SimShopCustomerApi.GetActionOrder(session.actionOrderId)?.IsActiveState == false))
                    Abort(session, "RSH.Text.SpaUseInterrupted");
                else if (session.stage == SpaSessionStage.Queued && Now - session.createdTick > HotelTuningDef.Current.spaQueuePatienceTicks)
                    Abort(session, "RSH.Text.TimedOutWaitingForAPoolSlotOr");
                else if (session.stage != SpaSessionStage.Soaking && session.stage != SpaSessionStage.Queued
                    && Now - session.stageTick > HotelTuningDef.Current.receptionTimeout * 3)
                    Abort(session, "RSH.Text.SpaUseInterrupted");
            }
            sessions.RemoveAll(s => s.IsTerminal && Now - s.completedTick > 60000);
            cooldowns.RemoveAll(c => Now - c.usedTick > 60000);
        }

        //设施失效时立即归还尚未使用的票款并释放关联物品。
        public void NotifyBuildingRemoved(Thing building)
        {
            foreach (SpaSession session in ActiveSessions.Where(s => s.spa == building || s.locker == building).ToList())
                Abort(session, "RSH.Text.SpaFacilityRemoved");
        }
    }
}
