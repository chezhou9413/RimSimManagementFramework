using SimManagementLib.Api;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using Verse;

namespace SimManagementLib.SimAI
{
    //在同一顾客 Lord 中切换生活与短期消费，保持人物和钱包不变。
    public partial class LordJob_CustomerVisit
    {
        //清除上一轮已经结清的单店状态。
        private void ResetLongStayVisit(Pawn pawn)
        {
            int id = pawn.thingIDNumber;
            ClearCustomerCart(id);
            ClearCustomerServiceOrders(id);
            checkoutState.MarkPostCheckoutCompleted(id);
            checkoutState.ClearPawnReadyForCheckout(id);
            checkoutState.ClearCheckoutOrder(id);
            forceLeaveAfterCheckout.Remove(id);
            pawn.Map.GetComponent<CustomerArrivalManager>()?.ReleaseCheckoutTicket(pawn);
        }

        //重新创建短访问时钟，允许住客再次光顾同一家店。
        internal void BeginLongStayShopVisit(Pawn pawn, Zone_Shop shop, bool foodOnly)
        {
            ResetLongStayVisit(pawn);
            var session = new CustomerVisitSession { pawnId = pawn.thingIDNumber, foodOnly = foodOnly };
            visitSessions[pawn.thingIDNumber] = session;
            session.MoveToShop(this, pawn, shop);
            session.Initialize(this, pawn);
            lord.ReceiveMemo("Customer_GoToNextShop");
        }

        //回到生活职责，不启动普通商店关门或离图计时。
        internal void ReturnPawnToLongStay(Pawn pawn)
        {
            ResetLongStayVisit(pawn);
            var session = GetOrCreateSession(pawn);
            session.stage = CustomerVisitStage.LongStay;
            session.foodOnly = false;
            session.lastProgressTick = Find.TickManager.TicksGame;
            session.lastStageChangeTick = Find.TickManager.TicksGame;
            session.exitRequestedTick = -1;
            session.recoveryCount = 0;
            lord.ReceiveMemo("Customer_ReturnToLongStay");
        }

        //让所有正常消费结束路径统一返回有效入住。
        internal bool TryReturnPawnToLongStay(Pawn pawn)
            => pawn != null && SimShopCustomerApi.HasLongStay(pawn) && SimShopCustomerApi.ReturnToLongStay(pawn).success;
    }
}
