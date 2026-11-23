using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using Verse;

namespace SimManagementLib.Api
{
    //让扩展的前台业务与普通付款共用同一收银队列。
    public static partial class SimShopCheckoutApi
    {
        //登记或续期排队票据；外部业务等待期间每 tick 调用，返回前方人数。
        public static bool TryJoinQueue(Pawn customer, Building_CashRegister register, out int customersAhead)
        {
            customersAhead = 0;
            if (customer?.Spawned != true || register?.Spawned != true || register.Map != customer.Map)
                return false;
            var queue = customer.Map.GetComponent<CustomerArrivalManager>()?.CheckoutQueue;
            if (queue?.Acquire(customer, register) == null) return false;
            customersAhead = queue.CountAhead(customer.thingIDNumber);
            return true;
        }

        //服务完成或中断时交还排队位置。
        public static void LeaveQueue(Pawn customer)
        {
            customer?.Map?.GetComponent<CustomerArrivalManager>()?.ReleaseCheckoutTicket(customer);
        }

        //返回指定收银台或服务台已排队与正在前往的顾客总数，供多柜台分流使用。
        public static int GetRegisterLoad(Building_CashRegister register, Pawn excludeCustomer = null)
        {
            if (register?.Spawned != true || register.Map == null) return 0;
            var manager = register.Map.GetComponent<CustomerArrivalManager>();
            if (manager == null) return 0;
            return manager.RuntimeIndex.CountRegisterLoad(register, excludeCustomer?.thingIDNumber ?? -1);
        }

        //判断指定顾客当前是否已加入指定收银台或服务台的排队队列。
        public static bool IsQueuedForRegister(Pawn customer, Building_CashRegister register)
        {
            if (customer?.Spawned != true || register?.Spawned != true || register.Map != customer.Map)
                return false;
            var manager = customer.Map.GetComponent<CustomerArrivalManager>();
            return manager != null && manager.CheckoutQueue.IsQueuedFor(customer.thingIDNumber, register.thingIDNumber);
        }
    }
}
