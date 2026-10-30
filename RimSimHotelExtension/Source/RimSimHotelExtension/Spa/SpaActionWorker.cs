using System.Linq;
using RimSimHotelExtension.Services;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Spa
{
    //通过通用顾客动作接口提供温泉，散客和住客使用相同的候选与订单流程。
    public sealed class SpaActionWorker : CustomerActionWorker
    {
        //只查询资金、开放设施和冷却，不提前预约池位或创建业务记录。
        public override bool CanRun(CustomerActionContext context, out string reason)
        {
            if (!base.CanRun(context, out reason)) return false;
            if (!HotelMoneyUtility.CanUseServices(context.customer))
            {
                reason = HotelText.T("RSH.Text.GuestIsCheckingInOrOut");
                return false;
            }
            if (!SpaMapComponent.For(context.customer.Map).CanBathe(context.customer))
            {
                reason = HotelText.T("RSH.Text.AlreadyBathedTodayOrStillOnCooldown");
                return false;
            }
            if (FindSpa(context) != null) return true;
            reason = HotelText.T("RSH.Text.NoAffordableReachableSpaWithLockersIsOpen");
            return false;
        }

        //允许只有温泉动作的商店进入框架到客候选。
        public override bool IsAttractionAvailable(Zone_Shop shop)
        {
            return base.IsAttractionAvailable(shop) && shop.Map.listerThings.AllThings.OfType<Building_HotelSpa>()
                .Any(s => s.Shop == shop && s.Operational) && HasLocker(shop);
        }

        //未满足基础营业条件时向经营界面解释原因，满员只是正常排队。
        public override string GetAttractionBlockReason(Zone_Shop shop, bool refresh = false)
        {
            if (!base.IsAttractionAvailable(shop)) return "";
            if (!HasLocker(shop)) return HotelText.T("RSH.Text.TheSpaNeedsAnApparelLocker");
            return IsAttractionAvailable(shop) ? "" : HotelText.T("RSH.Text.SpaClosedUnpoweredOrBroken");
        }

        //休闲不足时更容易选中泡汤，仍保留其他店铺动作的选择机会。
        public override float GetSelectionWeight(CustomerActionContext context)
        {
            return (def?.selectionWeight ?? 1f) * (0.35f + 2f * (1f - (context.customer.needs?.joy?.CurLevel ?? 0.5f)));
        }

        //最终选中后才建立排队会话与通用动作订单。
        public override CustomerActionOrder CreateOrder(CustomerActionContext context)
        {
            Building_HotelSpa spa = FindSpa(context);
            if (spa == null || !CanRun(context, out _)) return null;
            SpaSession session = SpaMapComponent.For(context.customer.Map).Enqueue(context.customer, spa, context.shop);
            return new CustomerActionOrder
            {
                actionDefName = def.defName, customerThingId = context.customer.thingIDNumber,
                shopZoneId = context.shop.ID, targetThingId = spa.thingIDNumber, targetLabel = spa.LabelCap,
                createdTick = session.createdTick, state = CustomerActionOrderState.Active,
                externalData = session.id.ToString(), billAmount = 0f
            };
        }

        //从已保存的会话恢复，不再重新检查本次已经占用的冷却或余额。
        public override bool CanStartOrder(CustomerActionContext context, out string reason)
        {
            if (!base.CanStartOrder(context, out reason)) return false;
            SpaSession session = SessionFor(context);
            if (session != null && !session.IsTerminal && session.customer == context.customer
                && session.spa != null && session.spa.Operational && session.spa.Shop == context.shop) return true;
            reason = session?.endReason.NullOrEmpty() == false ? HotelText.Display(session.endReason)
                : HotelText.T("RSH.Text.SpaSessionIsNoLongerAvailable");
            return false;
        }

        //动作订单编号保存在 Job.count，读档和重新分配任务均能恢复同一会话。
        public override Job MakeJobForOrder(CustomerActionContext context)
        {
            SpaSession session = SessionFor(context);
            if (session == null) return null;
            session.actionOrderId = context.order.orderId;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_UseSpa"), session.spa);
            job.count = context.order.orderId;
            return job;
        }

        //票款已经单独入账，完成时只登记消费次数，绝不追加第二份账单。
        public override void NotifyOrderCompleted(CustomerActionContext context)
        {
            context.CompleteOrder();
            NotifyActionCompleted(context);
        }

        //任何通用框架取消入口都回到同一退款和归还衣物路径。
        public override void NotifyOrderCanceled(CustomerActionContext context, string reason)
        {
            SpaMapComponent.For(context?.customer?.Map)?.Abort(SessionFor(context), reason);
        }

        //解析跨框架订单保存的温泉会话编号。
        public static SpaSession SessionFor(CustomerActionContext context)
        {
            return int.TryParse(context?.order?.externalData, out int id)
                ? SpaMapComponent.For(context.customer.Map)?.Find(id) : null;
        }

        //筛选最近的可用温泉；队列允许等待，但不能无上限堆积。
        private static Building_HotelSpa FindSpa(CustomerActionContext context)
        {
            Pawn pawn = context.customer;
            Zone_Shop shop = context.shop;
            if (!HasLocker(shop)) return null;
            int funds = SimShopCustomerApi.GetAvailableFunds(pawn);
            return shop.Map.listerThings.AllThings.OfType<Building_HotelSpa>()
                .Where(s => s.Shop == shop && s.Operational && s.TicketPrice >= 0 && s.TicketPrice <= funds
                    && s.TicketPrice <= context.remainingBudget
                    && SpaMapComponent.For(shop.Map).WaitingCount(s) < s.Capacity
                    && pawn.CanReach(s, PathEndMode.Touch, Danger.None)
                    && shop.Map.listerThings.AllThings.OfType<Building_HotelLocker>()
                        .Any(l => shop.ContainsCell(l.Position) && pawn.CanReach(l, PathEndMode.Touch, Danger.None)))
                .OrderBy(s => s.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
        }

        //有柜位设施即可排队，不要求候选查询时实际占柜。
        private static bool HasLocker(Zone_Shop shop)
        {
            return shop?.Map != null && shop.Map.listerThings.AllThings.OfType<Building_HotelLocker>()
                .Any(l => l.Spawned && shop.ContainsCell(l.Position));
        }
    }
}
