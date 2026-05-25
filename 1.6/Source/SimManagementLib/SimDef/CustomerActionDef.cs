using RimWorld;
using SimManagementLib.Api;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 声明顾客浏览阶段可执行的外部动作，负责让按摩、赌博、呼叫服务员等行为通过 Def 挂入顾客系统。
    /// </summary>
    public class CustomerActionDef : Def
    {
        public int order;
        public float selectionWeight = 1f;
        public bool defaultEnabled = true;
        public List<string> targetCustomerKindIds = new List<string>();
        public List<string> targetGoodsCategoryIds = new List<string>();
        public List<string> targetServiceCategoryIds = new List<string>();
        public List<ThingDef> requiredThingDefs = new List<ThingDef>();
        public List<Type> requiredThingClasses = new List<Type>();
        public Type workerClass = typeof(CustomerActionWorker);
        public bool createsPersistentOrder;
        public float defaultBillAmount;

        [Unsaved] private CustomerActionWorker workerInt;

        /// <summary>
        /// 返回动作运行时 Worker，负责在类型缺失时提供安全空实现。
        /// </summary>
        public CustomerActionWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    try
                    {
                        Type type = workerClass ?? typeof(CustomerActionWorker);
                        workerInt = (CustomerActionWorker)Activator.CreateInstance(type);
                        workerInt.def = this;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SimShop.CustomerAction] 动作 {defName} 初始化 Worker 失败: {ex}");
                        workerInt = new CustomerActionWorker { def = this };
                    }
                }

                return workerInt;
            }
        }
    }
}
