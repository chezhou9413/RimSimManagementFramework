using SimManagementLib.SimService;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义建筑默认提供的服务列表、默认并发上限和初始启用状态。
    /// </summary>
    public class CompProperties_ServiceProvider : CompProperties
    {
        public List<string> defaultServiceDefNames = new List<string>();
        public int defaultMaxSimultaneousUsers = 1;
        public bool enabledByDefault = true;

        public CompProperties_ServiceProvider()
        {
            compClass = typeof(ThingComp_ServiceProvider);
        }
    }

    /// <summary>
    /// 挂在建筑上的服务提供组件，负责保存该建筑启用的服务槽位、价格覆盖和并发限制。
    /// </summary>
    public class ThingComp_ServiceProvider : ThingComp
    {
        public bool enabled = true;
        public List<ServiceSlotData> serviceSlots = new List<ServiceSlotData>();

        private CompProperties_ServiceProvider ServiceProps => props as CompProperties_ServiceProvider;

        /// <summary>
        /// 在组件生成后补齐 XML 默认服务槽位。
        /// </summary>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureDefaultSlots();
        }

        /// <summary>
        /// 读写服务组件状态，并在读档后补齐默认服务槽位。
        /// </summary>
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", ServiceProps?.enabledByDefault ?? true);
            Scribe_Collections.Look(ref serviceSlots, "serviceSlots", LookMode.Deep);
            if (serviceSlots == null) serviceSlots = new List<ServiceSlotData>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureDefaultSlots();
        }

        /// <summary>
        /// 返回当前启用且能解析到服务 Def 的服务槽位。
        /// </summary>
        public IEnumerable<ServiceSlotData> EnabledSlots
        {
            get
            {
                EnsureDefaultSlots();
                return serviceSlots.Where(s => s != null && s.enabled && s.ServiceDef != null);
            }
        }

        /// <summary>
        /// 查找指定服务 Def 对应的建筑槽位。
        /// </summary>
        public ServiceSlotData FindSlot(ShopServiceDef serviceDef)
        {
            if (serviceDef == null) return null;
            EnsureDefaultSlots();
            return serviceSlots.FirstOrDefault(s => s != null && s.serviceDefName == serviceDef.defName);
        }

        /// <summary>
        /// 确保 XML 默认服务在存档列表中存在，不移除玩家或其他模组已保存的槽位。
        /// </summary>
        public void EnsureDefaultSlots()
        {
            if (serviceSlots == null) serviceSlots = new List<ServiceSlotData>();
            CompProperties_ServiceProvider p = ServiceProps;
            if (p == null)
            {
                enabled = true;
                return;
            }

            if (Scribe.mode != LoadSaveMode.Inactive && Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (!p.defaultServiceDefNames.NullOrEmpty())
            {
                for (int i = 0; i < p.defaultServiceDefNames.Count; i++)
                {
                    string defName = p.defaultServiceDefNames[i];
                    if (string.IsNullOrEmpty(defName)) continue;
                    if (serviceSlots.Any(s => s != null && s.serviceDefName == defName)) continue;

                    serviceSlots.Add(new ServiceSlotData
                    {
                        serviceDefName = defName,
                        enabled = p.enabledByDefault,
                        priceOverrideEnabled = false,
                        priceOverride = 0f,
                        maxSimultaneousUsers = Mathf.Max(1, p.defaultMaxSimultaneousUsers)
                    });
                }
            }
        }
    }

    /// <summary>
    /// 保存建筑上单项服务的开关、价格覆盖和并发配置。
    /// </summary>
    public class ServiceSlotData : IExposable
    {
        public string serviceDefName = "";
        public bool enabled = true;
        public bool priceOverrideEnabled;
        public float priceOverride;
        public int maxSimultaneousUsers = 1;

        [NonSerialized] public string priceBuffer;
        [NonSerialized] public string capacityBuffer;

        /// <summary>
        /// 返回该槽位引用的服务 Def，缺失或卸载时返回 null。
        /// </summary>
        public ShopServiceDef ServiceDef => DefDatabase<ShopServiceDef>.GetNamedSilentFail(serviceDefName);

        /// <summary>
        /// 读写服务槽位配置，并修正非法并发值。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref serviceDefName, "serviceDefName", "");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref priceOverrideEnabled, "priceOverrideEnabled", false);
            Scribe_Values.Look(ref priceOverride, "priceOverride", 0f);
            Scribe_Values.Look(ref maxSimultaneousUsers, "maxSimultaneousUsers", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (priceOverride > 0f)
                    priceOverrideEnabled = true;
                priceOverride = Math.Max(0f, priceOverride);
                maxSimultaneousUsers = Math.Max(1, maxSimultaneousUsers);
            }
        }
    }
}
