using SimManagementLib.Pojo;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责根据当前已启用模组列表检查网络蓝图是否可导入。
    /// </summary>
    public static class BlueprintModCompatibilityChecker
    {
        /// <summary>
        /// 返回当前运行中的全部包 ID。
        /// </summary>
        public static HashSet<string> GetActivePackageIds()
        {
            return new HashSet<string>(
                LoadedModManager.RunningMods
                    .Where(mod => mod != null && !string.IsNullOrWhiteSpace(mod.PackageId))
                    .Select(mod => mod.PackageId),
                System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检查蓝图依赖是否全部满足。
        /// </summary>
        public static BlueprintCompatibilityCheckResult CheckCompatibility(IEnumerable<ShopBlueprintRequiredModData> requiredMods)
        {
            HashSet<string> activePackageIds = GetActivePackageIds();
            BlueprintCompatibilityCheckResult result = new BlueprintCompatibilityCheckResult
            {
                IsCompatible = true
            };

            if (requiredMods == null)
                return result;

            foreach (ShopBlueprintRequiredModData mod in requiredMods)
            {
                if (mod == null || string.IsNullOrWhiteSpace(mod.packageId))
                    continue;
                if (activePackageIds.Contains(mod.packageId))
                    continue;

                result.IsCompatible = false;
                result.MissingMods.Add(mod);
            }

            return result;
        }
    }
}
