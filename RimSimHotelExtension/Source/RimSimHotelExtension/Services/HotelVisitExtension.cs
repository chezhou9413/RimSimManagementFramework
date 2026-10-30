using System.Collections.Generic;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Services
{
    //把住宿生命周期和个人钱包接入框架的长期顾客协议。
    public sealed class HotelVisitExtension : CustomerVisitExtension
    {
        //等待前台的预订也需要长期职责，避免普通购物超时提前离开。
        public override bool HasLongStay(Pawn customer) => HotelMapComponent.For(customer?.Map)?.FindStay(customer) != null;

        //由旅馆日程生成睡觉、取款、接待或下一家消费任务。
        public override Job MakeLongStayJob(Pawn customer) => HotelScheduleUtility.MakeJob(customer);

        //扣除已经存在的未来食费保护，当前账单由框架再扣一次。
        public override int GetAvailableFunds(Pawn customer)
        {
            var manager = HotelMapComponent.For(customer.Map);
            var stay = manager.FindStay(customer);
            var guest = manager.GuestFor(customer);
            return guest == null ? 0 : HotelMoneyUtility.SpendingFunds(stay, guest);
        }

        //实际付款只扣个人钱包，并记录可见的个人消费累计。
        public override bool TrySpendFunds(Pawn customer, int amount, out string reason)
        {
            var guest = HotelMapComponent.For(customer.Map)?.GuestFor(customer);
            reason = "";
            if (guest == null || amount < 0 || guest.wallet < amount)
            {
                reason = HotelText.T("RSH.Text.GuestWalletHasInsufficientFunds");
                return false;
            }
            guest.wallet -= amount;
            guest.totalSpent += amount;
            if (!guest.foodShopping) guest.optionalSpent += amount;
            var shop = SimShopCustomerApi.GetCurrentShop(customer);
            HotelMapComponent.For(customer.Map).AddMoneyRecord(new HotelMoneyRecord {
                tick = Find.TickManager.TicksGame, pawn = customer, shopZoneId = shop?.ID ?? -1,
                kind = "Spend", label = shop?.label ?? "RSH.Text.Purchase", amount = amount });
            return true;
        }

        //服务取消后退回原住客钱包。
        public override void RefundFunds(Pawn customer, int amount)
        {
            var guest = HotelMapComponent.For(customer.Map)?.GuestFor(customer);
            if (guest == null || amount <= 0) return;
            guest.wallet += amount;
            guest.totalSpent = System.Math.Max(0, guest.totalSpent - amount);
            if (!guest.foodShopping) guest.optionalSpent = System.Math.Max(0, guest.optionalSpent - amount);
            var shop = SimShopCustomerApi.GetCurrentShop(customer);
            HotelMapComponent.For(customer.Map).AddMoneyRecord(new HotelMoneyRecord {
                tick = Find.TickManager.TicksGame, pawn = customer, shopZoneId = shop?.ID ?? -1,
                kind = "Refund", label = "RSH.Text.CanceledPurchaseRefund", amount = amount });
        }

        //开始实际生成前预订单床或双床。
        public override CustomerArrivalReservation PrepareArrival(Zone_Shop shop, RuntimeCustomerKind kind, int availablePlaces)
            => kind?.kindId == "RSH_Traveller" ? HotelMapComponent.For(shop.Map).PrepareArrival(shop, availablePlaces) : null;

        //收到已生成的同行顾客后办理同一入住合同。
        public override void CompleteArrival(Zone_Shop shop, RuntimeCustomerKind kind,
            CustomerArrivalReservation reservation, List<Pawn> customers)
            => HotelMapComponent.For(shop.Map).CompleteArrival(shop, reservation, customers);

        //生成取消时立即释放临时床位。
        public override void CancelArrival(Zone_Shop shop, CustomerArrivalReservation reservation, string reason)
            => HotelMapComponent.For(shop.Map).CancelArrival(reservation);

        //紧急中断会同时结束同行合同，避免永久保留孤立床位。
        public override void OnLongStayInterrupted(Pawn customer, string reason)
        {
            var manager = HotelMapComponent.For(customer?.Map);
            var stay = manager?.FindStay(customer);
            if (stay == null || stay.finishing) return;
            manager.RequestCheckout(stay, reason);
            manager.FinishCheckout(stay);
        }

        //消费访问到时后先结清账单，再返回住宿职责。
        public override void TickLongStay(CustomerVisitExtensionContext context)
            => HotelScheduleUtility.CheckShoppingReturn(context.customer);
    }
}
