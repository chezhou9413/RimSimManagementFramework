using RimSimHotelExtension.Services;
using RimSimHotelExtension.Tool;
using RimSimHotelExtension.Settings;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.Spa
{
    //负责托管票款转收入、退款与物品归还，所有终止入口共用同一段结算。
    public sealed partial class SpaMapComponent
    {
        //顾客在有人值守的服务台完成读条后才扣取票款，入池前仍可原额退款。
        public bool BuyTicket(SpaSession session)
        {
            if (session.ticketPaid) return true;
            if (session.stage != SpaSessionStage.BuyingTicket || session.ticketDesk?.IsManned != true
                || session.customer.Position != session.ticketServiceCell
                || session.ticketServiceTicks < HotelTuningDef.Current.returnReceptionTicks) return false;
            var payment = SimShopCustomerApi.TrySpendCustomerFunds(session.customer, session.price);
            if (!payment.success)
            {
                Abort(session, payment.failReason);
                return false;
            }
            session.ticketPaid = true;
            ShopBubbleUtility.ShowSilverPayment(session.customer, session.price);
            return true;
        }

        //顾客已经站到自己的池位后才将托管票款作为营业收入。
        public bool StartBathing(SpaSession session)
        {
            if (!session.ticketPaid)
            {
                Abort(session, "RSH.Text.SpaTicketNotPurchased");
                return false;
            }
            if (session.incomeCommitted)
            {
                ChangeStage(session, SpaSessionStage.Soaking);
                return true;
            }
            var result = SimShopFinanceApi.CommitImmediateIncome(session.customer, session.Shop(map), session.ticketDesk,
                "HotelSpa/" + map.uniqueID + "/" + session.id, session.spa.LabelCap, session.price);
            if (!result.success)
            {
                Abort(session, result.failReason);
                return false;
            }
            session.incomeCommitted = true;
            RecordCooldown(session.customer);
            ChangeStage(session, SpaSessionStage.Soaking);
            return true;
        }

        //正常离池并取回衣物后完成会话，票款已经在开始泡汤时入账。
        public void Complete(SpaSession session)
        {
            if (session == null || session.IsTerminal) return;
            RestoreClothes(session);
            GrantBenefit(session, true);
            session.stage = SpaSessionStage.Completed;
            session.completedTick = Now;
        }

        //未使用的票款原额退回，已经入池的按实际时长恢复休闲。
        public void Abort(SpaSession session, string reason)
        {
            if (session == null || session.IsTerminal) return;
            if (session.stage == SpaSessionStage.BuyingTicket)
                SimShopCheckoutApi.LeaveQueue(session.customer);
            session.stage = SpaSessionStage.Canceled;
            session.completedTick = Now;
            session.endReason = reason ?? "RSH.Text.BathingCanceled";
            if (session.ticketPaid && !session.incomeCommitted)
            {
                SimShopCustomerApi.RefundCustomerFunds(session.customer, session.price);
                session.ticketPaid = false;
            }
            RestoreClothes(session);
            GrantBenefit(session, false);
            CustomerActionOrder order = SimShopCustomerApi.GetActionOrder(session.actionOrderId);
            if (order?.IsActiveState == true) SimShopCustomerApi.CancelActionOrder(order, HotelText.Display(session.endReason));
        }

        //正常流程在柜旁穿衣，突发中断也必须先归还实物再释放预约。
        private void RestoreClothes(SpaSession session)
        {
            if (session.locker == null) return;
            session.locker.ReturnClothes(session.customer, session.id);
            session.locker.ReleaseClaim(session.id);
            session.clothesStored = false;
        }

        //一次性发放按进度计算的休闲，完整泡汤才提高住宿满意度。
        private void GrantBenefit(SpaSession session, bool completed)
        {
            if (session.benefitGranted || !session.incomeCommitted) return;
            session.benefitGranted = true;
            float portion = Mathf.Clamp01((float)session.elapsedTicks / Mathf.Max(1, session.durationTicks));
            session.customer?.needs?.joy?.GainJoy(session.joyGain * portion, JoyKindDefOf.Meditative);
            if (completed) HotelMoneyUtility.AddSatisfaction(session.customer, session.satisfactionGain);
        }
    }
}
