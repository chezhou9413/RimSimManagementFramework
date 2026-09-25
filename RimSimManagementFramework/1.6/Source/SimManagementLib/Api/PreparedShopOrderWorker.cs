using SimManagementLib.Tool;
using RimWorld;
using SimManagementLib.SimService;
using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    //为现做订单提供可继承的制作逻辑，负责控制创建条件、员工制作 Job、顾客领取 Job 和完成交付。
    public class PreparedShopOrderWorker
    {
        public ShopServiceDef serviceDef;
        //判断顾客是否能基于指定服务创建现做订单。
        public virtual bool CanCreateOrder(Pawn customer, Thing provider, Zone_Shop shop, out string reason)
        {
            reason = "";
            if (customer == null)
            {
                reason = SimTranslation.T("RSMF.Api.Error.CustomerInvalid");
                return false;
            }
            if (serviceDef == null)
            {
                reason = SimTranslation.T("RSMF.Api.Error.ServiceInvalid");
                return false;
            }
            if (provider == null || provider.Destroyed)
            {
                reason = SimTranslation.T("RSMF.Api.Error.ProviderInvalid");
                return false;
            }
            return true;
        }
        //创建现做订单数据，外部模组可重写以写入产物、数量或自定义数据。
        public virtual PreparedShopOrder CreateOrder(Pawn customer, Thing provider, Zone_Shop shop, float totalPrice)
        {
            return new PreparedShopOrder
            {
                customerThingId = customer?.thingIDNumber ?? -1,
                shopZoneId = shop?.ID ?? -1,
                serviceDefName = serviceDef?.defName ?? "",
                providerThingId = provider?.thingIDNumber ?? -1,
                providerLabel = provider?.LabelCap ?? "",
                resultCount = 1,
                totalPrice = totalPrice,
                state = PreparedShopOrderState.Draft,
                createdTick = Find.TickManager?.TicksGame ?? 0
            };
        }
        //判断指定员工是否可以处理该现做订单。
        public virtual bool CanStaffWork(Pawn staff, PreparedShopOrder order, out string reason)
        {
            reason = "";
            if (staff == null || staff.Destroyed || staff.Dead)
            {
                reason = SimTranslation.T("RSMF.Api.Error.StaffInvalid");
                return false;
            }
            if (order == null || !order.IsActivePreparationState)
            {
                reason = SimTranslation.T("RSMF.Api.Error.OrderNotPreparable");
                return false;
            }
            return true;
        }
        //创建员工制作订单 Job，默认使用框架提供的通用制作 Job。
        public virtual Job MakeStaffPrepareJob(Pawn staff, PreparedShopOrder order)
        {
            if (staff == null || order == null) return null;
            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Sim_PrepareShopOrder");
            if (jobDef == null) return null;

            Thing provider = SimShopOrderApi.FindOrderProvider(staff.Map, order);
            Job job = provider != null ? JobMaker.MakeJob(jobDef, provider) : JobMaker.MakeJob(jobDef);
            job.count = order.orderId;
            return job;
        }
        //创建顾客领取或使用订单结果的 Job，默认不创建后续 Job。
        public virtual Job MakeCustomerReceiveJob(Pawn customer, PreparedShopOrder order)
        {
            return null;
        }
        //返回员工制作订单所需 Tick，默认使用服务定义时长。
        public virtual int GetPrepareDurationTicks(Pawn staff, PreparedShopOrder order)
        {
            return serviceDef?.Worker?.GetDurationTicks() ?? 600;
        }
        //在员工开始制作时接收通知。
        public virtual void NotifyPreparationStarted(Pawn staff, PreparedShopOrder order)
        {
        }
        //在制作读条期间接收通知。
        public virtual void TickPreparation(Pawn staff, PreparedShopOrder order)
        {
            Thing provider = staff?.Map != null ? SimShopOrderApi.FindOrderProvider(staff.Map, order) : null;
            if (staff != null && provider != null)
                staff.rotationTracker.FaceTarget(provider);
        }
        //在员工完成制作时接收通知，默认不生成实体产物。
        public virtual void NotifyPreparationCompleted(Pawn staff, PreparedShopOrder order)
        {
        }
        //在订单交付给顾客时接收通知。
        public virtual void NotifyDelivered(Pawn customer, PreparedShopOrder order)
        {
        }
        //在订单取消或失败时接收通知。
        public virtual void NotifyCanceled(PreparedShopOrder order, string reason)
        {
        }
    }
}
