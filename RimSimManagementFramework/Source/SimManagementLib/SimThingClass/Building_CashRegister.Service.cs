using SimManagementLib.SimMapComp;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //记录柜台实际服务工时，让排队耐心和临时补位共用同一份状态。
    public partial class Building_CashRegister
    {
        private int customerServiceTicks;
        private int lastCustomerServiceTick = -1;
        public int CustomerServiceTicks => customerServiceTicks;

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
