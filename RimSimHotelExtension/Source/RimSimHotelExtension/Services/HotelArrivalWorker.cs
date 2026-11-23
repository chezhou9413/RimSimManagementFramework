using RimSimHotelExtension.GameComp;
using SimManagementLib.Api;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;
using System.Linq;
using SimManagementLib.Pojo;
using RimSimHotelExtension.Tool;

namespace RimSimHotelExtension.Services
{
    //把旅馆设施纳入商店吸引力，同时允许普通来访者在前台入住。
    public sealed class HotelArrivalWorker : CustomerActionWorker
    {
        //设施合法性只要求服务台和登记床位，满房及换班不会关闭已有旅馆。
        public override bool IsAttractionAvailable(Zone_Shop shop)
            => base.IsAttractionAvailable(shop) && HotelMapComponent.For(shop.Map).DeskFor(shop) != null
                && HotelMapComponent.For(shop.Map).SettingsFor(shop).registeredBeds.Any(b => b != null && b.Spawned
                    && shop.ContainsCell(b.Position));

        //将旅馆已登记床位容量折算为等效设施规模，使无货柜旅馆也能正常获得评分、容量与来客倍率。
        public override int GetAttractionFacilityUnits(Zone_Shop shop)
        {
            if (shop?.Map == null) return 0;
            var manager = HotelMapComponent.For(shop.Map);
            if (manager == null || manager.DeskFor(shop) == null) return 0;
            return manager.SettingsFor(shop).registeredBeds
                .Where(b => b != null && b.Spawned && shop.ContainsCell(b.Position))
                .Sum(b => b.SleepingSlotsCount);
        }

        //设施存在与当下能接新客分开，满房不会令服务台停止值班。
        public override bool CanAttractCustomer(Zone_Shop shop, RuntimeCustomerKind kind)
            => base.CanAttractCustomer(shop, kind) && HotelMapComponent.For(shop.Map).GetIssue(shop).NullOrEmpty();

        //返回统一接待条件说明供经营面板使用。
        public override string GetAttractionBlockReason(Zone_Shop shop, bool refresh = false)
            => HotelMapComponent.For(shop?.Map)?.GetIssue(shop) ?? "";

        //暂停接待、非接待时段或正常满房属于正常经营状态，不弹出异常警报。
        public override bool TryGetOperatingAlert(Zone_Shop shop, out string reason, out Thing target)
        {
            reason = "";
            target = null;
            if (shop?.Map == null) return false;
            var manager = HotelMapComponent.For(shop.Map);
            if (manager == null || !manager.SettingsFor(shop).enabled) return false;
            var desk = manager.DeskFor(shop);
            if (desk == null)
            {
                reason = HotelText.T("RSH.Text.NoHotelReceptionDesk");
                return true;
            }
            if (!manager.SettingsFor(shop).registeredBeds.Any(b => b != null && b.Spawned && shop.ContainsCell(b.Position)))
            {
                reason = HotelText.T("RSH.Text.NoVacantRegisteredBeds");
                target = desk;
                return true;
            }
            return false;
        }

        //已经入住、尚有未付商品或预算不足的顾客不能重复开房。
        public override bool CanRun(CustomerActionContext context, out string reason)
        {
            if (!base.CanRun(context, out reason)) return false;
            if (HotelMoneyUtility.IsGuest(context.customer) || SimShopCustomerApi.GetAmountOwed(context.customer) > 0)
            {
                reason = HotelText.T("RSH.Text.AlreadyStayingOrHasAnUnpaidBill");
                return false;
            }
            HotelMapComponent manager = HotelMapComponent.For(context.shop.Map);
            reason = manager.GetIssue(context.shop);
            if (!reason.NullOrEmpty()) return false;
            if (manager.CanAffordCheckIn(context.customer, context.shop, context.remainingBudget)) return true;
            reason = HotelText.T("RSH.Text.CannotAffordTheMinimumStayAndMealReserve");
            return false;
        }

        //合同保存在旅馆组件内，不占用跨活动的动作订单槽。
        public override bool ShouldCreateOrder(CustomerActionContext context) => false;

        //优先选择负载更轻且可达的前台加入接待队列，暂时被占用不会阻止申请入住。
        public override Job MakeJob(CustomerActionContext context)
        {
            var desk = HotelMapComponent.For(context.shop.Map)
                .BestDeskFor(context.shop, context.customer, out IntVec3 cell, maxDanger: Danger.Some);
            if (desk == null) return null;
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RSH_Reception"), desk);
            job.SetTarget(TargetIndex.C, cell);
            return job;
        }
    }
}
