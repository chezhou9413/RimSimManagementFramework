using System;
using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义自动售货机货柜的刷客、并发和售卖行为参数。
    /// </summary>
    public class CompProperties_VendingMachine : CompProperties
    {
        public bool enabledByDefault = true;
        public int maxSimultaneousCustomers = 2;
        public float baseMtbDays = 0.18f;

        /// <summary>
        /// 绑定自动售货机货柜组件类型。
        /// </summary>
        public CompProperties_VendingMachine()
        {
            compClass = typeof(ThingComp_VendingMachine);
        }
    }

    /// <summary>
    /// 挂在货柜上的自动售卖组件，负责保存营业开关并暴露刷客参数。
    /// </summary>
    public class ThingComp_VendingMachine : ThingComp
    {
        public bool enabled = true;

        private CompProperties_VendingMachine VendingProps => props as CompProperties_VendingMachine;

        /// <summary>
        /// 返回自动售货机当前允许同时接待的顾客数量。
        /// </summary>
        public int MaxSimultaneousCustomers => Math.Max(1, VendingProps?.maxSimultaneousCustomers ?? 2);

        /// <summary>
        /// 返回自动售货机自身基础刷客间隔，单位为游戏天。
        /// </summary>
        public float BaseMtbDays => Math.Max(0.01f, VendingProps?.baseMtbDays ?? 0.18f);

        /// <summary>
        /// 读写自动售货机营业开关。
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref enabled, "enabled", VendingProps?.enabledByDefault ?? true);
        }
    }
}
