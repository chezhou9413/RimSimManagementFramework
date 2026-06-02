using RimWorld;
using SimManagementLib.Tool;
using System;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimService
{
    /// <summary>
    /// 定义一项可由商店建筑提供的付费服务，负责保存服务分类、价格、时长、计费模式和默认执行器。
    /// </summary>
    public class ShopServiceDef : Def
    {
        public string serviceCategoryId = "";
        public float basePrice = 1f;
        public IntRange durationTicks = new IntRange(300, 600);
        public ServiceBillingMode billingMode = ServiceBillingMode.PayBeforeUse;
        public Type workerClass = typeof(ShopServiceWorker);
        public JobDef useJobDef;
        public string labelOverride = "";
        public bool checkoutAfterSelection;

        [Unsaved] private ShopServiceWorker workerInt;

        /// <summary>
        /// 返回该服务的运行时执行器，并在首次访问时创建默认或自定义 Worker。
        /// </summary>
        public ShopServiceWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    try
                    {
                        Type type = workerClass ?? typeof(ShopServiceWorker);
                        workerInt = (ShopServiceWorker)Activator.CreateInstance(type);
                        workerInt.def = this;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SimShop] 服务 {defName} 初始化执行器失败: {ex}");
                        workerInt = new ShopServiceWorker { def = this };
                    }
                }

                return workerInt;
            }
        }

        /// <summary>
        /// 返回面向 UI 和账单显示的服务名称。
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(labelOverride)) return labelOverride;
                return LabelCap.RawText;
            }
        }
    }

    /// <summary>
    /// 描述服务费用是在使用前、使用后还是以单次票据资格结算。
    /// </summary>
    public enum ServiceBillingMode
    {
        PayBeforeUse,
        UseBeforePay,
        TicketBeforeUse
    }

    /// <summary>
    /// 描述顾客服务订单从选择、付款、使用到完成或失败的运行时状态。
    /// </summary>
    public enum ServiceOrderState
    {
        Draft,
        AwaitingPayment,
        ReadyToUse,
        TicketIssued,
        InUse,
        UsedAwaitingPayment,
        Completed,
        Canceled,
        CheckoutFailed
    }

    /// <summary>
    /// 为服务提供可重写的执行逻辑，复杂模组可继承它来控制可用性、价格、预约、使用 Job 和状态通知。
    /// </summary>
    public class ShopServiceWorker
    {
        public ShopServiceDef def;

        /// <summary>
        /// 判断指定顾客是否能在指定建筑上使用服务，默认只检查基础对象、地图和可达性。
        /// </summary>
        public virtual bool CanUse(Pawn customer, Thing provider, SimZone.Zone_Shop shop, out string failReason)
        {
            failReason = "";
            if (def == null)
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.MissingDef");
                return false;
            }
            if (customer == null || provider == null || provider.Destroyed || provider.Map == null)
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ProviderUnavailable");
                return false;
            }
            if (!provider.Spawned || customer.Map != provider.Map)
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.ProviderWrongMap");
                return false;
            }

            IntVec3 cell = GetUseCell(provider);
            if (!cell.IsValid || !cell.InBounds(provider.Map) || !cell.Standable(provider.Map))
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.UseCellNotStandable");
                return false;
            }
            if (!customer.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
            {
                failReason = SimTranslation.T("RSMF.ShopService.Fail.CustomerCannotReach");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 返回指定顾客在指定建筑使用服务时的单价，默认读取建筑服务槽位覆盖价和服务基础价。
        /// </summary>
        public virtual float GetPrice(Pawn customer, Thing provider, SimZone.Zone_Shop shop)
        {
            return Tool.ShopServiceUtility.GetServicePrice(provider, def);
        }

        /// <summary>
        /// 尝试为服务订单占用运行资格，默认只做并发数量检查。
        /// </summary>
        public virtual bool TryReserve(Pawn customer, Thing provider, Pojo.CustomerServiceOrder order)
        {
            return Tool.ShopServiceUtility.CanAcceptMoreUsers(provider, def);
        }

        /// <summary>
        /// 生成服务使用 Job，默认使用服务 Def 配置的 JobDef，缺省时使用通用等待服务 Job。
        /// </summary>
        public virtual Job MakeUseJob(Pawn customer, Thing provider, Pojo.CustomerServiceOrder order)
        {
            if (provider == null || order == null) return null;
            JobDef jobDef = def?.useJobDef ?? DefDatabase<JobDef>.GetNamedSilentFail("Customer_UsePaidService");
            if (jobDef == null) return null;

            Job job = JobMaker.MakeJob(jobDef, provider, GetUseCell(provider));
            job.count = order.orderId;
            return job;
        }

        /// <summary>
        /// 在服务开始时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void NotifyServiceStarted(Pawn customer, Thing provider, Pojo.CustomerServiceOrder order)
        {
        }

        /// <summary>
        /// 在服务使用读条期间每 Tick 接收通知，默认让顾客面向服务建筑。
        /// </summary>
        public virtual void TickServiceUse(Pawn customer, Thing provider, Pojo.CustomerServiceOrder order)
        {
            if (customer == null || provider == null) return;
            customer.rotationTracker.FaceTarget(provider);
        }

        /// <summary>
        /// 在服务完成时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void NotifyServiceCompleted(Pawn customer, Thing provider, Pojo.CustomerServiceOrder order)
        {
        }

        /// <summary>
        /// 在服务取消时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void NotifyServiceCanceled(Pawn customer, Thing provider, Pojo.CustomerServiceOrder order)
        {
        }

        /// <summary>
        /// 在服务费用结清时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void NotifyServicePaid(Pawn customer, Thing provider, Pojo.CustomerServiceOrder order)
        {
        }

        /// <summary>
        /// 返回服务建筑的默认使用格，优先使用建筑交互格。
        /// </summary>
        protected virtual IntVec3 GetUseCell(Thing provider)
        {
            if (provider is Building building && building.InteractionCell.IsValid)
                return building.InteractionCell;
            return provider?.Position ?? IntVec3.Invalid;
        }

        /// <summary>
        /// 返回本次服务等待时长，默认从服务定义的范围中随机取值。
        /// </summary>
        public virtual int GetDurationTicks()
        {
            if (def == null) return 300;
            return Mathf.Max(60, def.durationTicks.RandomInRange);
        }
    }
}
