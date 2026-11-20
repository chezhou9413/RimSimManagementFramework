using SimManagementLib.SimMapComp;
using SimManagementLib.Tool;
using Verse;

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

        public int CustomerServiceTicks => customerServiceTicks;

        //判断无人收银台是否已有收银员接单正在赶来，用于暂缓排队顾客的短暂无人超时。
        public bool HasIncomingCashier
            => !IsManned && (GetActiveShiftCashier() != null || GetIncomingReliever() != null);

        //返回当前有效的主值班或首位赶往本台的收银员。
        public Pawn GetActiveShiftCashier()
        {
            if (IsManningThisRegister(activeShiftCashier))
                return activeShiftCashier;
            activeShiftCashier = null;
            handoverRequested = false;
            Pawn manned = CurrentCashier;
            if (IsManningThisRegister(manned))
            {
                activeShiftCashier = manned;
                return activeShiftCashier;
            }
            return null;
        }

        //返回当前正在赶来交接班的第二任收银员。
        public Pawn GetIncomingReliever()
        {
            if (IsManningThisRegister(incomingReliever) && incomingReliever != activeShiftCashier)
                return incomingReliever;
            incomingReliever = null;
            relieverArrived = false;
            return null;
        }

        //判断指定员工是否可以接手本台（无人时正常接班，或主班请求交接时作为接班员提前赶来）。
        public bool CanAcceptStaffReservation(Pawn pawn, out bool isHandover)
        {
            isHandover = false;
            if (pawn == null) return false;
            Pawn active = GetActiveShiftCashier();
            if (active == null || active == pawn)
                return true;
            Pawn reliever = GetIncomingReliever();
            if (handoverRequested && (reliever == null || reliever == pawn))
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

        //登记刚获得预约的收银员为主班或交接接班员。
        public void RegisterCashierClaim(Pawn pawn, bool asHandover)
        {
            if (pawn == null) return;
            Pawn active = GetActiveShiftCashier();
            if (!asHandover || active == null || active == pawn)
            {
                activeShiftCashier = pawn;
                handoverRequested = false;
                if (incomingReliever == pawn)
                {
                    incomingReliever = null;
                    relieverArrived = false;
                }
                return;
            }
            incomingReliever = pawn;
            relieverArrived = false;
        }

        //接班员走上交互格时切换为新的主收银员。
        public void PromoteRelieverToActive(Pawn pawn)
        {
            if (pawn == null) return;
            activeShiftCashier = pawn;
            handoverRequested = false;
            if (incomingReliever == pawn)
                incomingReliever = null;
            relieverArrived = false;
        }

        //在收银任务结束或中断时释放本台的交接登记。
        public void ClearCashierClaim(Pawn pawn)
        {
            if (pawn == null) return;
            if (activeShiftCashier == pawn)
            {
                activeShiftCashier = null;
                handoverRequested = false;
            }
            if (incomingReliever == pawn)
            {
                incomingReliever = null;
                relieverArrived = false;
            }
        }

        //由主收银员更新是否需要呼叫接班员。
        public void SetHandoverRequested(Pawn pawn, bool requested)
        {
            if (pawn == null) return;
            if (GetActiveShiftCashier() == pawn)
                handoverRequested = requested;
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

        //验证员工是否仍在当前地图执行本收银台的 Sim_ManCashRegister 任务。
        private bool IsManningThisRegister(Pawn pawn)
        {
            return pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.Spawned
                && pawn.Map == Map
                && pawn.CurJobDef?.defName == "Sim_ManCashRegister"
                && pawn.CurJob?.targetA.Thing == this;
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

        //柜台实际办理业务的时间不消耗任何排队顾客的耐心。
        public int GetUnservedWaitTicks(int waitStartTick, int serviceTicksAtStart)
        {
            int elapsed = Find.TickManager.TicksGame - waitStartTick;
            int served = System.Math.Max(0, customerServiceTicks - serviceTicksAtStart);
            return System.Math.Max(0, elapsed - served);
        }

        //累计工时随柜台保存，顾客读档后可以继续扣除已经完成的服务时间。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref customerServiceTicks, "customerServiceTicks");
        }
    }
}
