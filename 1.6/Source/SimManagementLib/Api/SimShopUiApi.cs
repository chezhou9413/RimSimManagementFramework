using SimManagementLib.SimDef;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供商店 UI 插件化入口，负责页面查询、运行时注册、刷新请求和当前上下文访问。
    /// </summary>
    public static class SimShopUiApi
    {
        private static readonly List<ShopUiPageDef> runtimePages = new List<ShopUiPageDef>();
        private static readonly Dictionary<string, ShopUiContext> activeContextsByDefName = new Dictionary<string, ShopUiContext>();
        private static ShopUiContext currentContext;
        private static bool refreshRequested;

        /// <summary>
        /// 按范围、可见性和排序返回可绘制页面。
        /// </summary>
        public static List<ShopUiPageDef> GetPages(ShopUiPageScope scope, ShopUiContext context)
        {
            IEnumerable<ShopUiPageDef> xmlPages = DefDatabase<ShopUiPageDef>.AllDefsListForReading ?? new List<ShopUiPageDef>();
            return xmlPages.Concat(runtimePages)
                .Where(page => PageMatchesScope(page, scope))
                .Where(page => RequiredModsLoaded(page))
                .Where(page => SafeCanShow(page, context))
                .OrderBy(page => page.order)
                .ThenBy(page => page.defName)
                .ToList();
        }

        /// <summary>
        /// 运行时注册 UI 页面，负责给代码模组提供无需 XML 的挂载方式。
        /// </summary>
        public static ShopUiPageDef RegisterRuntimePage(string defName, string label, ShopUiPageScope scope, int order, Type workerClass, bool replace = false)
        {
            if (string.IsNullOrWhiteSpace(defName))
                throw new ArgumentException("运行时页面 defName 不能为空。", nameof(defName));

            ShopUiPageDef existing = runtimePages.FirstOrDefault(page => page.defName == defName);
            if (existing != null && !replace)
                return existing;
            if (existing != null)
                runtimePages.Remove(existing);

            ShopUiPageDef pageDef = new ShopUiPageDef
            {
                defName = defName,
                label = label ?? defName,
                scope = scope,
                order = order,
                workerClass = workerClass ?? typeof(ShopUiPageWorker),
                defaultVisible = true
            };
            runtimePages.Add(pageDef);
            RequestRefresh();
            return pageDef;
        }

        /// <summary>
        /// 请求窗口重建页面缓存。
        /// </summary>
        public static void RequestRefresh()
        {
            refreshRequested = true;
        }

        /// <summary>
        /// 返回并清除全局刷新标记，负责让窗口决定何时重建页面。
        /// </summary>
        internal static bool ConsumeRefreshRequest()
        {
            bool requested = refreshRequested;
            refreshRequested = false;
            return requested;
        }

        /// <summary>
        /// 尝试获取当前绘制中的上下文，负责让外部工具和 Worker 安全访问窗口状态。
        /// </summary>
        public static bool TryGetContext<TContext>(out TContext context) where TContext : ShopUiContext
        {
            context = currentContext as TContext;
            if (context != null)
                return true;

            foreach (ShopUiContext activeContext in activeContextsByDefName.Values)
            {
                if (activeContext is TContext typed)
                {
                    context = typed;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 在受保护环境中执行页面回调，负责隔离外部模组异常和恢复 GUI 状态。
        /// </summary>
        internal static void SafeInvoke(ShopUiPageDef page, ShopUiContext context, string stage, Action<ShopUiPageWorker> action)
        {
            GameFont oldFont = Text.Font;
            UnityEngine.TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            UnityEngine.Color oldColor = UnityEngine.GUI.color;
            currentContext = context;

            try
            {
                if (context != null)
                {
                    context.PageDef = page;
                    if (page != null)
                        activeContextsByDefName[page.defName] = context;
                }
                action?.Invoke(page?.Worker);
            }
            catch (Exception ex)
            {
                context?.RecordException(ex);
                Log.Error($"[SimShop.UI] 页面 {page?.defName ?? "<null>"} 在 {stage} 阶段出错: {ex}");
            }
            finally
            {
                currentContext = null;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                UnityEngine.GUI.color = oldColor;
            }
        }

        /// <summary>
        /// 清理窗口持有的上下文，负责避免关闭窗口后外部代码拿到过期对象。
        /// </summary>
        internal static void ClearContext(ShopUiContext context)
        {
            if (context == null) return;
            List<string> removeKeys = activeContextsByDefName
                .Where(pair => ReferenceEquals(pair.Value, context))
                .Select(pair => pair.Key)
                .ToList();
            for (int i = 0; i < removeKeys.Count; i++)
                activeContextsByDefName.Remove(removeKeys[i]);
        }

        /// <summary>
        /// 判断页面是否匹配指定挂载范围。
        /// </summary>
        private static bool PageMatchesScope(ShopUiPageDef page, ShopUiPageScope scope)
        {
            if (page == null) return false;
            return page.scope == ShopUiPageScope.Both || page.scope == scope;
        }

        /// <summary>
        /// 判断页面依赖的模组是否已启用。
        /// </summary>
        private static bool RequiredModsLoaded(ShopUiPageDef page)
        {
            if (page?.requiredModPackageIds == null || page.requiredModPackageIds.Count == 0)
                return true;

            for (int i = 0; i < page.requiredModPackageIds.Count; i++)
            {
                string packageId = page.requiredModPackageIds[i];
                if (!string.IsNullOrWhiteSpace(packageId) && ModLister.GetActiveModWithIdentifier(packageId, true) == null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 安全执行页面可见性判断。
        /// </summary>
        private static bool SafeCanShow(ShopUiPageDef page, ShopUiContext context)
        {
            try
            {
                if (context != null)
                    context.PageDef = page;
                return page?.Worker?.CanShow(context) != false;
            }
            catch (Exception ex)
            {
                Log.Error($"[SimShop.UI] 页面 {page?.defName ?? "<null>"} 可见性判断失败: {ex}");
                return false;
            }
        }
    }
}
