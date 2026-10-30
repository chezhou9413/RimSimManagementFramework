using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
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
            var session = new SpaSession
            {
                id = nextId++, customer = pawn, spa = spa, shopZoneId = shop.ID,
                createdTick = Now, stageTick = Now, price = spa.TicketPrice,
                durationTicks = spa.DurationTicks, joyGain = spa.JoyGain, satisfactionGain = spa.SatisfactionGain
            };
            sessions.Add(session);
            return session;
        }

        //按照同一温泉的到达先后取得池位和可达衣柜，均成功后才收取票款。
        public bool TryAdmit(SpaSession session, Job job)
        {
            Pawn pawn = session.customer;
            if (sessions.Any(s => s.spa == session.spa && s.stage == SpaSessionStage.Queued && s.id < session.id)) return false;
            Zone_Shop shop = session.Shop(map);
            var lockers = map.listerThings.AllThings.OfType<Building_HotelLocker>()
                .Where(l => l.Available && shop.ContainsCell(l.Position)
                    && pawn.CanReserveAndReach(l, PathEndMode.Touch, Danger.None))
                .OrderBy(l => l.Position.DistanceToSquared(pawn.Position));
            Building_HotelLocker locker = lockers.FirstOrDefault();
            if (locker == null) return false;
            for (int i = 0; i < session.spa.Capacity; i++)
            {
                if (sessions.Any(s => s.spa == session.spa && s.HasReservedSlot && s.slot == i)) continue;
                IntVec3 cell = session.spa.SlotCell(i);
                if (!cell.Standable(map) || !pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                if (!pawn.Reserve(locker, job, errorOnFailed: false)) return false;
                if (!pawn.Reserve(cell, job, errorOnFailed: false))
                {
                    map.reservationManager.Release(locker, pawn, job);
                    continue;
                }
                if (!locker.TryClaim(pawn, session.id)) return false;
                session.locker = locker;
                session.slot = i;
                session.poolCell = cell;
                var payment = SimShopCustomerApi.TrySpendCustomerFunds(pawn, session.price);
                if (!payment.success)
                {
                    Abort(session, payment.failReason);
                    return false;
                }
                session.ticketPaid = true;
                ChangeStage(session, SpaSessionStage.GoingToLocker);
                return true;
            }
            return false;
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
                if (session.customer == null || !session.customer.Spawned || session.customer.Map != map
                    || session.customer.Dead || session.customer.Downed || session.spa == null || !session.spa.Operational
                    || shop == null || session.spa.Shop != shop
                    || (session.locker != null && (!session.locker.Spawned || !shop.ContainsCell(session.locker.Position))))
                    Abort(session, "RSH.Text.SpaUseInterrupted");
                else if (session.stage == SpaSessionStage.Queued && Now - session.createdTick > HotelTuningDef.Current.spaQueuePatienceTicks)
                    Abort(session, "RSH.Text.TimedOutWaitingForAPoolSlotOr");
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
