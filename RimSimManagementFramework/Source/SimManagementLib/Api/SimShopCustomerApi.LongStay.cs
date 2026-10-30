using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    //连接长期住客的生活阶段、标准消费和实际钱包。
    public static partial class SimShopCustomerApi
    {
        //取得管理该住客的扩展。
        internal static CustomerVisitExtension LongStayOwner(Pawn customer)
            => customer == null ? null : VisitExtensions.FirstOrDefault(extension => extension.HasLongStay(customer));

        //判断顾客是否仍有有效入住。
        public static bool HasLongStay(Pawn customer) => LongStayOwner(customer) != null;

        //取得生活阶段任务，不创建购物订单。
        internal static Job MakeLongStayJob(Pawn customer) => LongStayOwner(customer)?.MakeLongStayJob(customer);

        //返回扣除当前未付款账单后的实际可用金额。
        public static int GetAvailableFunds(Pawn customer)
        {
            var visit = GetCustomerVisit(customer);
            if (visit == null) return 0;
            var owner = LongStayOwner(customer);
            float funds = owner != null ? owner.GetAvailableFunds(customer)
                : visit.GetBudgetForPawn(customer.thingIDNumber) - (visit.GetOrCreateSession(customer)?.TotalSpentAcrossShops ?? 0f);
            return Mathf.Max(0, Mathf.FloorToInt(funds - visit.GetAmountOwedForCheckout(customer.thingIDNumber)));
        }

        //扣除票款或直接业务付款，保留已有待付款额度。
        public static SimApiResult TrySpendCustomerFunds(Pawn customer, int amount)
            => DebitFunds(customer, amount, true);

        //收银提交时当前账单就是本次付款，不能再次预留同一金额。
        internal static SimApiResult DebitFunds(Pawn customer, int amount, bool protectPending)
        {
            if (customer == null || amount < 0) return SimApiResult.Fail("顾客或付款金额无效");
            if (amount == 0) return SimApiResult.Success();
            var visit = GetCustomerVisit(customer);
            if (visit == null) return SimApiResult.Fail("顾客没有有效经营访问");
            var owner = LongStayOwner(customer);
            float funds = owner != null ? owner.GetAvailableFunds(customer)
                : visit.GetBudgetForPawn(customer.thingIDNumber) - visit.GetOrCreateSession(customer).TotalSpentAcrossShops;
            if (protectPending) funds -= visit.GetAmountOwedForCheckout(customer.thingIDNumber);
            if (funds < amount) return SimApiResult.Fail("顾客可用余额不足");
            if (owner != null && !owner.TrySpendFunds(customer, amount, out string reason)) return SimApiResult.Fail(reason);
            visit.RecordShopPayment(customer, amount);
            return SimApiResult.Success();
        }

        //退回尚未转为商店收入的托管款，调用者保存退款完成状态。
        public static void RefundCustomerFunds(Pawn customer, int amount)
        {
            if (customer == null || amount <= 0) return;
            LongStayOwner(customer)?.RefundFunds(customer, amount);
            var session = GetCustomerSession(customer);
            if (session != null) session.totalSpentAcrossShops = Mathf.Max(0f, session.totalSpentAcrossShops - amount);
        }

        //开始新的标准消费访问，生活记录继续由扩展保存。
        public static SimApiResult BeginShopVisit(Pawn customer, Zone_Shop shop, bool foodOnly = false)
        {
            var visit = GetCustomerVisit(customer);
            if (visit == null || !HasLongStay(customer) || shop?.Map != customer.Map || !shop.IsOpenNow())
                return SimApiResult.Fail("住客或目标商店不可用");
            if (visit.GetOrCreateSession(customer).Stage != CustomerVisitStage.LongStay)
                return SimApiResult.Fail("顾客尚未返回生活阶段");
            if (visit.GetAmountOwedForCheckout(customer.thingIDNumber) > 0f || HasRunningCustomerActionOrder(customer))
                return SimApiResult.Fail("当前消费尚未完成");
            visit.BeginLongStayShopVisit(customer, shop, foodOnly);
            return SimApiResult.Success();
        }

        //完成单店消费后回到住客生活职责。
        public static SimApiResult ReturnToLongStay(Pawn customer)
        {
            var visit = GetCustomerVisit(customer);
            if (visit == null || !HasLongStay(customer)) return SimApiResult.Fail("顾客没有有效入住");
            if (visit.GetOrCreateSession(customer).Stage == CustomerVisitStage.LongStay) return SimApiResult.Success();
            if (visit.GetAmountOwedForCheckout(customer.thingIDNumber) > 0f || HasRunningCustomerActionOrder(customer)
                || visit.NeedsPostCheckoutCompletion(customer.thingIDNumber)) return SimApiResult.Fail("当前消费尚未完成");
            visit.ReturnPawnToLongStay(customer);
            return SimApiResult.Success();
        }

        //结束住宿并交给框架清理订单和自然离图。
        public static void EndLongStayVisit(Pawn customer, string reason)
        {
            GetCustomerVisit(customer)?.CleanupUnpaidCustomerStateForSession(customer, reason);
            CustomerExitUtility.BeginEmergencyExit(customer, reason);
        }

        //活动取消完毕后通知扩展释放住宿，确保托管款仍能退回原钱包。
        internal static void NotifyLongStayInterrupted(Pawn customer, string reason)
            => LongStayOwner(customer)?.OnLongStayInterrupted(customer, reason);

        //统一发现商品、服务和既有扩展动作，无需识别扩展名称。
        public static List<Zone_Shop> FindConsumableShops(Pawn customer, bool foodOnly = false)
        {
            var visit = GetCustomerVisit(customer);
            if (visit == null || customer.Map == null) return new List<Zone_Shop>();
            int funds = GetAvailableFunds(customer);
            return customer.Map.zoneManager.AllZones.OfType<Zone_Shop>()
                .Where(shop => shop.IsOpenNow() && shop.Cells.Count > 0
                    && (foodOnly ? CustomerShoppingMatchUtility.ShopHasAffordableFood(customer, shop, funds)
                        : CustomerShoppingMatchUtility.ShopHasMatchingAffordableGoodsOrServices(customer, shop, visit, funds))
                    && CustomerSafetyUtility.CanCustomerReach(customer, shop.Cells[0], PathEndMode.OnCell, Danger.Deadly))
                .ToList();
        }

        //预览外部动作，仅调用已有可执行查询，不创建订单或工作。
        internal static bool HasRunnableCustomerAction(Pawn customer, Zone_Shop shop, float budget)
        {
            var visit = GetCustomerVisit(customer);
            if (visit == null || budget < 0f) return false;
            var context = BuildActionContext(customer, visit, shop);
            if (context == null) return false;
            context.remainingBudget = budget;
            foreach (var def in DefDatabase<SimDef.CustomerActionDef>.AllDefsListForReading)
                if (def != null && TryGetCustomerActionWeight(CloneContextForAction(context, def), def.Worker, out _)) return true;
            return false;
        }

        //向扩展取得到客前的设施预留，随后仍由框架统一生成并计数。
        internal static CustomerArrivalReservation PrepareArrival(Zone_Shop shop, RuntimeCustomerKind kind, int places)
        {
            foreach (var extension in VisitExtensions)
            {
                var reservation = extension.PrepareArrival(shop, kind, places);
                if (reservation == null) continue;
                reservation.owner = extension;
                return reservation;
            }
            return null;
        }
    }
}
