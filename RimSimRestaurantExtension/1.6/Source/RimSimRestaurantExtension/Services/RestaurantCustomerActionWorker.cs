using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Services
{
    //连接框架顾客动作与堂食会话，职责是入座时建单、完成用餐后记账并交接收银。
    public class RestaurantCustomerActionWorker : CustomerActionWorker
    {
        //判断餐厅吸引力是否有效，职责是复用经营快照排除无法营业的配置。
        public override bool IsAttractionAvailable(Zone_Shop shop)
        {
            return base.IsAttractionAvailable(shop) && RestaurantBusinessAvailability.Snapshot(shop).NullOrEmpty();
        }

        //报告店内餐厅的到客限制，职责是让框架强制刷新能重算并展示菜单、岗位和路线问题。
        public override string GetAttractionBlockReason(Zone_Shop shop, bool refresh = false)
        {
            return base.IsAttractionAvailable(shop) ? RestaurantBusinessAvailability.Snapshot(shop, refresh) : "";
        }

        //检查是否可以入座，职责是保持点菜与库存承诺发生在员工交谈之后。
        public override bool CanRun(CustomerActionContext context, out string reason)
        {
            return base.CanRun(context, out reason)
                && RestaurantBusinessAvailability.CanSeat(context.customer, context.shop, out reason);
        }

        //返回动作选择权重，职责是不在顾客入座前随机固定菜单。
        public override float GetSelectionWeight(CustomerActionContext context)
        {
            return Mathf.Max(0.05f, def?.selectionWeight ?? 1f);
        }

        //创建未点菜用餐会话，职责是仅保存顾客、设施和预约座位。
        public override CustomerActionOrder CreateOrder(CustomerActionContext context)
        {
            var shop = context?.shop;
            Pawn customer = context?.customer;
            Thing provider = RestaurantOrderCreationUtility.FindOrderCounter(shop);
            if (provider == null || !RestaurantBusinessAvailability.CanSeat(customer, shop, out _)
                || !RestaurantDiningSpotUtility.TryFindDiningSpot(customer, shop, out IntVec3 seat, out Thing table)) return null;
            var order = RestaurantOrderUtility.OrderManager.AddOrder(new RestaurantOrder
            {
                mapId = customer.Map.uniqueID,
                customerThingId = customer.thingIDNumber,
                shopZoneId = shop.ID,
                providerThingId = provider.thingIDNumber,
                seatCell = seat,
                tableThingId = table.thingIDNumber,
                createdTick = Find.TickManager.TicksGame,
                lastProgressTick = Find.TickManager.TicksGame
            });
            if (order == null) return null;
            RestaurantOrderUtility.OrderManager.CreateSession(order);
            return new CustomerActionOrder
            {
                customerThingId = customer.thingIDNumber,
                shopZoneId = shop.ID,
                actionDefName = def.defName,
                targetThingId = provider.thingIDNumber,
                targetLabel = provider.LabelCap,
                state = CustomerActionOrderState.Active,
                createdTick = order.createdTick,
                externalData = order.orderId.ToString()
            };
        }

        //检查已建立会话能否恢复，职责是按阶段验证餐位且不要求已收餐顾客重新寻找服务员。
        public override bool CanStartOrder(CustomerActionContext context, out string reason)
        {
            reason = "";
            var order = RestaurantActionUtility.ResolveRestaurantOrder(context?.order);
            var session = RestaurantOrderUtility.OrderManager.SessionFor(order);
            if (context?.customer == null || session == null || session.IsTerminal
                || session.state == Dining.RestaurantSessionState.AwaitingCheckout)
            {
                reason = "用餐会话已结束";
                return false;
            }
            if (!Dining.RestaurantSessionUtility.Validate(session, context.customer.Map, out reason)) return false;
            return true;
        }

        //构造顾客用餐任务，职责是绑定框架和领域订单的持久编号。
        public override Job MakeJobForOrder(CustomerActionContext context)
        {
            var order = RestaurantActionUtility.ResolveRestaurantOrder(context?.order);
            if (order == null) return null;
            RestaurantOrderUtility.OrderManager.BindAction(order, context.order.orderId);
            return RestaurantOrderUtility.MakeCustomerDiningJob(context.customer, order);
        }

        //记录任务关联，职责是避免原版进食数量覆盖动作订单编号。
        public override void NotifyJobCreated(CustomerActionContext context, Job job)
        {
            if (context?.order == null || job == null) return;
            RestaurantJobUtility.SetOrderId(job, context.order.orderId);
            var order = RestaurantActionUtility.ResolveRestaurantOrder(context.order);
            if (order != null) order.actionOrderId = context.order.orderId;
        }

        //记录首次执行，职责是让管理器知道用餐会话已有推进。
        public override void NotifyOrderStarted(CustomerActionContext context)
        {
            RestaurantActionUtility.ResolveRestaurantOrder(context?.order)?.TouchProgress();
        }

        //登记已吃完餐品的待付款账单，职责是通过框架幂等入口同步应付款和实际成本。
        public override void NotifyOrderCompleted(CustomerActionContext context)
        {
            var order = RestaurantActionUtility.ResolveRestaurantOrder(context?.order);
            var session = RestaurantOrderUtility.OrderManager.SessionFor(order);
            if (session == null || !session.ReadyForCheckout || session.IsTerminal) return;
            if (Dining.RestaurantSessionSettlement.QueueBill(context.customer, session, context.order))
                context.RegisterConsumptionActionAndShouldCheckout();
        }

        //处理框架取消，职责是在付款前终止餐厅员工工作并释放资源。
        public override void NotifyOrderCanceled(CustomerActionContext context, string reason)
        {
            var order = RestaurantActionUtility.ResolveRestaurantOrder(context?.order);
            Dining.RestaurantSessionUtility.Abort(RestaurantOrderUtility.OrderManager.SessionFor(order), reason);
        }
    }
}
