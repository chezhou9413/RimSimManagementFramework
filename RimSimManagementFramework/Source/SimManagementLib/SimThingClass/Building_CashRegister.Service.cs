using SimManagementLib.SimMapComp;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingClass
{
    //记录柜台实际服务工时与换班交接状态，让排队耐心和多人倒班共用同一份运行状态。
    public partial class Building_CashRegister
    {
        public const int MaxStaffReservations = 2;

        private int customerServiceTicks;
        private int lastCustomerServiceTick = -1;
        private Pawn activeShiftCashier;
        private Pawn incomingReliever;
        private bool handoverRequested;
        private bool relieverArrived;
        private bool forcedHandover;

        public int CustomerServiceTicks => customerServiceTicks;

        //判断无人收银台是否已有收银员接单正在赶来，用于暂缓排队顾客的短暂无人超时。
        public bool HasIncomingCashier
            => !IsManned && (GetActiveShiftCashier() != null || GetIncomingReliever() != null);

        //返回当前有效的主值班或首位赶往本台的收银员；主班离场时立即自动晋升接班员，避免出现双人抢占空窗。
        public Pawn GetActiveShiftCashier()
        {
            if (IsManningThisRegister(activeShiftCashier))
                return activeShiftCashier;
            activeShiftCashier = null;
            handoverRequested = false;
            forcedHandover = false;
            Pawn manned = CurrentCashier;
            if (IsManningThisRegister(manned))
            {
                activeShiftCashier = manned;
                if (incomingReliever == manned)
                {
                    incomingReliever = null;
                    relieverArrived = false;
                }
                return activeShiftCashier;
            }
            if (IsManningThisRegister(incomingReliever))
            {
                activeShiftCashier = incomingReliever;
                incomingReliever = null;
                relieverArrived = false;
                return activeShiftCashier;
            }
            return null;
        }

        //返回当前正在赶来交接班的第二任收银员。
        public Pawn GetIncomingReliever()
        {
            Pawn active = GetActiveShiftCashier();
            if (IsManningThisRegister(incomingReliever) && incomingReliever != active)
                return incomingReliever;
            incomingReliever = null;
            relieverArrived = false;
            forcedHandover = false;
            return null;
        }

        //判断指定员工是否可以接手本台，玩家强制指令允许顶替当前在班或赶来的收银员。
        public bool CanAcceptStaffReservation(Pawn pawn, out bool isHandover, bool forced = false)
        {
            isHandover = false;
            if (pawn == null) return false;
            Pawn active = GetActiveShiftCashier();
            Pawn reliever = GetIncomingReliever();
            if (active == null)
                return forced || reliever == null || reliever == pawn;
            if (active == pawn)
                return true;
            if ((forced || handoverRequested || forcedHandover) && (forced || reliever == null || reliever == pawn))
            {
                isHandover = true;
                return true;
            }
            return false;
        }

        //判断当前柜台是否正在等待接班员接替。
        public bool NeedsHandoverFor(Pawn candidate)
        {
            return CanAcceptStaffReservation(candidate, out bool isHandover) && isHandover;
        }

        //登记刚获得预约的收银员为主班或交接接班员；玩家手动强派时触发前任立即让位。
        public void RegisterCashierClaim(Pawn pawn, bool asHandover, bool forced = false)
        {
            if (pawn == null) return;
            Pawn active = GetActiveShiftCashier();
            if (active == null || active == pawn)
            {
                activeShiftCashier = pawn;
                handoverRequested = false;
                forcedHandover = false;
                if (incomingReliever == pawn)
                {
                    incomingReliever = null;
                    relieverArrived = false;
                }
                return;
            }
            incomingReliever = pawn;
            relieverArrived = false;
            if (forced)
                forcedHandover = true;
        }

        //接班员走上交互格时切换为新的主收银员；若仍有其他主班在岗则拒绝覆盖，严防双人同时值班。
        public bool PromoteRelieverToActive(Pawn pawn)
        {
            if (pawn == null) return false;
            Pawn active = GetActiveShiftCashier();
            if (active != null && active != pawn)
                return false;
            activeShiftCashier = pawn;
            handoverRequested = false;
            forcedHandover = false;
            if (incomingReliever == pawn)
                incomingReliever = null;
            relieverArrived = false;
            return true;
        }

        //在收银任务结束或中断时释放本台的交接登记；若已有接班员在途或待命则立即无缝转正。
        public void ClearCashierClaim(Pawn pawn)
        {
            if (pawn == null) return;
            if (cachedCashier == pawn)
            {
                cachedCashier = null;
                nextCashierCheckTick = -1;
            }
            if (activeShiftCashier == pawn)
            {
                activeShiftCashier = null;
                handoverRequested = false;
                forcedHandover = false;
                if (IsManningThisRegister(incomingReliever) && incomingReliever != pawn)
                {
                    activeShiftCashier = incomingReliever;
                    incomingReliever = null;
                    relieverArrived = false;
                }
            }
            else if (incomingReliever == pawn)
            {
                incomingReliever = null;
                relieverArrived = false;
                forcedHandover = false;
            }
        }

        //由主收银员更新是否需要呼叫接班员。
        public void SetHandoverRequested(Pawn pawn, bool requested)
        {
            if (pawn == null) return;
            if (GetActiveShiftCashier() == pawn)
                handoverRequested = requested;
        }

        //判断当前交接请求是否仍然有效，避免主班取消交接后接班员原地傻等。
        public bool IsHandoverStillWanted(Pawn reliever)
        {
            if (reliever == null) return false;
            Pawn active = GetActiveShiftCashier();
            return active != null
                && (handoverRequested || forcedHandover)
                && GetIncomingReliever() == reliever;
        }

        //接班员抵达柜台旁时通知主收银员让出交互格。
        public void NotifyRelieverArrived(Pawn pawn)
        {
            if (pawn != null && GetIncomingReliever() == pawn)
                relieverArrived = true;
        }

        //判断接班员是否已到达柜台旁等待主收银员让位。
        public bool HasRelieverArrivedFor(Pawn activePawn)
        {
            return activePawn != null
                && GetActiveShiftCashier() == activePawn
                && relieverArrived
                && GetIncomingReliever() != null;
        }

        //判断主收银员此刻是否应让出交互格给已到位的正常接班员，或已启程的玩家强派接班员。
        public bool ShouldYieldToReliever(Pawn activePawn)
        {
            if (activePawn == null || GetActiveShiftCashier() != activePawn)
                return false;
            Pawn reliever = GetIncomingReliever();
            if (reliever == null)
                return false;
            if (handoverRequested && relieverArrived)
                return true;
            return forcedHandover && IsActivelyManningThisRegister(reliever);
        }

        //验证员工当前是否正在执行本收银台值班任务。
        private bool IsActivelyManningThisRegister(Pawn pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.Spawned
                && pawn.Map == Map
                && pawn.CurJobDef?.defName == "Sim_ManCashRegister"
                && pawn.CurJob?.targetA.Thing == this;
        }

        //验证员工是否正在执行或已排队等待执行本收银台的 Sim_ManCashRegister 任务。
        private bool IsManningThisRegister(Pawn pawn)
        {
            if (IsActivelyManningThisRegister(pawn))
                return true;
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map != Map)
                return false;
            JobQueue queue = pawn.jobs?.jobQueue;
            if (queue == null)
                return false;
            for (int i = 0; i < queue.Count; i++)
            {
                Job queued = queue[i]?.job;
                if (queued?.def?.defName == "Sim_ManCashRegister" && queued.targetA.Thing == this)
                    return true;
            }
            return false;
        }

        //已登记的队列需要接待；尚未选台的顾客在全店无人时也能请求补位。
        public virtual bool HasWaitingCustomers
        {
            get
            {
                if (!Spawned) return false;
                if (Map.GetComponent<CustomerArrivalManager>()?.CheckoutQueue.CountForRegister(this) > 0)
                    return true;
                var shop = ShopStaffUtility.FindShopFor(this);
                return ShopStaffUtility.HasPendingCheckoutCustomers(shop) && !ShopStaffUtility.HasMannedCashRegister(shop);
            }
        }

        //只由正在办理业务的顾客调用，同一柜台同一 tick 最多记录一次。
        public void NotifyCustomerService()
        {
            int now = Find.TickManager.TicksGame;
            if (!IsManned || lastCustomerServiceTick == now) return;
            lastCustomerServiceTick = now;
            customerServiceTicks++;
        }

        //柜台正常办理他人业务时按 25% 速率消耗耐心，既不因前人结账立刻发怒，也不会在长队中无限等待。
        public int GetUnservedWaitTicks(int waitStartTick, int serviceTicksAtStart)
        {
            int elapsed = System.Math.Max(0, Find.TickManager.TicksGame - waitStartTick);
            int served = System.Math.Min(elapsed, System.Math.Max(0, customerServiceTicks - serviceTicksAtStart));
            int unserved = elapsed - served;
            return unserved + (served / 4);
        }

        //累计工时与值班交接引用随柜台保存，读档后不会丢失排队等待进度或出现双人同时占台。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref customerServiceTicks, "customerServiceTicks");
            Scribe_References.Look(ref activeShiftCashier, "activeShiftCashier");
            Scribe_References.Look(ref incomingReliever, "incomingReliever");
            Scribe_Values.Look(ref handoverRequested, "handoverRequested");
            Scribe_Values.Look(ref relieverArrived, "relieverArrived");
            Scribe_Values.Look(ref forcedHandover, "forcedHandover");
        }
    }
}
