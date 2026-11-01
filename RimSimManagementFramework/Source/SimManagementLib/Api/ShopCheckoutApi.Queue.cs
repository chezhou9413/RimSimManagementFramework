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
    }
}
