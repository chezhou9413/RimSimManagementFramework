using SimManagementLib.Api;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 声明商店框架可挂载的 UI 页面，负责让 XML 和代码注册的页面进入统一排序与生命周期。
    /// </summary>
    public class ShopUiPageDef : Def
    {
        public ShopUiPageScope scope = ShopUiPageScope.Both;
        public int order;
        public Type workerClass = typeof(ShopUiPageWorker);
        public string iconPath = "";
        public string labelKey = "";
        public string descriptionKey = "";
        public bool defaultVisible = true;
        public bool showInNavigation = true;
        public List<string> requiredModPackageIds = new List<string>();

        [Unsaved] private ShopUiPageWorker workerInt;

        /// <summary>
        /// 返回页面运行时 Worker，负责在 XML 类型失效时回退到安全空页面。
        /// </summary>
        public ShopUiPageWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    try
                    {
                        Type type = workerClass ?? typeof(ShopUiPageWorker);
                        workerInt = (ShopUiPageWorker)Activator.CreateInstance(type);
                        workerInt.def = this;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SimShop.UI] 页面 {defName} 初始化 Worker 失败: {ex}");
                        workerInt = new ShopUiPageWorker { def = this };
                    }
                }

                return workerInt;
            }
        }

        /// <summary>
        /// 返回页面显示名称，负责优先使用 Keyed 翻译并在缺失时回退到 Def 标签。
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                string fallback = LabelCap.RawText;
                if (string.IsNullOrEmpty(labelKey))
                    return fallback;
                return SimTranslation.TOrFallback(labelKey, fallback);
            }
        }

        /// <summary>
        /// 返回页面说明文本，负责优先使用 Keyed 翻译并在缺失时回退到 Def 说明。
        /// </summary>
        public string DisplayDescription
        {
            get
            {
                if (string.IsNullOrEmpty(descriptionKey))
                    return description ?? "";
                return SimTranslation.TOrFallback(descriptionKey, description ?? "");
            }
        }
    }

    /// <summary>
    /// 描述 UI 页面可挂载的窗口范围，负责让同一个 Def 能按窗口类型过滤。
    /// </summary>
    public enum ShopUiPageScope
    {
        BusinessManager,
        ShopManager,
        Both
    }
}
