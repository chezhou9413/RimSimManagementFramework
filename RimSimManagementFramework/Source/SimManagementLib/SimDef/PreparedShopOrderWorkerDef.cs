using SimManagementLib.Api;
using SimManagementLib.SimService;
using System;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 声明服务对应的现做订单 Worker，负责让外部模组通过独立 Def 绑定现做订单逻辑。
    /// </summary>
    public class PreparedShopOrderWorkerDef : Def
    {
        public ShopServiceDef serviceDef;
        public Type workerClass = typeof(PreparedShopOrderWorker);
        public bool replaceRuntimeRegistration;

        [Unsaved] private PreparedShopOrderWorker workerInt;

        /// <summary>
        /// 返回现做订单运行时 Worker，负责在类型缺失时回退到安全空实现。
        /// </summary>
        public PreparedShopOrderWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    try
                    {
                        Type type = workerClass ?? typeof(PreparedShopOrderWorker);
                        workerInt = (PreparedShopOrderWorker)Activator.CreateInstance(type);
                        workerInt.serviceDef = serviceDef;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SimShop.Order] 现做订单 Worker {defName} 初始化失败: {ex}");
                        workerInt = new PreparedShopOrderWorker { serviceDef = serviceDef };
                    }
                }

                return workerInt;
            }
        }
    }
}
