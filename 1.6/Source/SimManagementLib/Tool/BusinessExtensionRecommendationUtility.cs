using HarmonyLib;
using SimManagementLib.SimDef;
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供推荐扩展状态检测，负责合并启用、安装和 Steam 订阅状态。
    /// </summary>
    public static class BusinessExtensionRecommendationUtility
    {
        private static MethodInfo getPublishedFileIdMethod;
        private static MethodInfo allSubscribedItemsMethod;

        /// <summary>
        /// 获取推荐扩展当前状态，负责在 Steam 不可用时安全降级为本地检测。
        /// </summary>
        public static BusinessExtensionRecommendationStatus GetStatus(IBusinessExtensionRecommendation recommendation)
        {
            BusinessExtensionRecommendationStatus status = new BusinessExtensionRecommendationStatus();
            if (recommendation == null)
                return status;

            status.IsActive = IsActive(recommendation);
            status.IsInstalled = status.IsActive || IsInstalled(recommendation);
            status.IsSubscribed = IsSubscribed(recommendation);
            return status;
        }

        /// <summary>
        /// 打开创意工坊页面，负责优先拉起 Steam 客户端并在失败时回退到系统浏览器。
        /// </summary>
        public static void OpenWorkshopUrl(string workshopUrl)
        {
            string url = StringEncodingUtility.SanitizeUtf16(workshopUrl);
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (TryOpenWithSystemShell(BuildSteamOpenUrl(url)))
                return;

            if (TryOpenWithSystemShell(url))
                return;

            Application.OpenURL(url);
        }

        /// <summary>
        /// 判断扩展是否已在当前运行中的模组列表启用。
        /// </summary>
        private static bool IsActive(IBusinessExtensionRecommendation recommendation)
        {
            if (!recommendation.PackageIds.NullOrEmpty())
            {
                for (int i = 0; i < recommendation.PackageIds.Count; i++)
                {
                    string packageId = recommendation.PackageIds[i];
                    if (!string.IsNullOrWhiteSpace(packageId) && ModLister.GetActiveModWithIdentifier(packageId, true) != null)
                        return true;
                }
            }

            string publishedId = NormalizePublishedFileId(recommendation.PublishedFileId);
            if (string.IsNullOrWhiteSpace(publishedId))
                return false;

            foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder)
            {
                if (string.Equals(GetPublishedFileIdText(mod), publishedId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断扩展是否存在于本地已安装模组列表。
        /// </summary>
        private static bool IsInstalled(IBusinessExtensionRecommendation recommendation)
        {
            if (!recommendation.PackageIds.NullOrEmpty())
            {
                for (int i = 0; i < recommendation.PackageIds.Count; i++)
                {
                    string packageId = recommendation.PackageIds[i];
                    if (!string.IsNullOrWhiteSpace(packageId) && ModLister.GetModWithIdentifier(packageId, true) != null)
                        return true;
                }
            }

            string publishedId = NormalizePublishedFileId(recommendation.PublishedFileId);
            if (string.IsNullOrWhiteSpace(publishedId))
                return false;

            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (string.Equals(GetPublishedFileIdText(mod), publishedId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断扩展是否在 Steam 当前订阅列表中。
        /// </summary>
        private static bool IsSubscribed(IBusinessExtensionRecommendation recommendation)
        {
            string publishedId = NormalizePublishedFileId(recommendation?.PublishedFileId);
            if (string.IsNullOrWhiteSpace(publishedId))
                return false;

            try
            {
                if (!Verse.Steam.SteamManager.Initialized)
                    return false;

                MethodInfo method = GetAllSubscribedItemsMethod();
                IEnumerable items = method?.Invoke(null, null) as IEnumerable;
                if (items == null)
                    return false;

                foreach (object item in items)
                {
                    if (string.Equals(item?.ToString(), publishedId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// 读取原版内部订阅项枚举方法，负责避免新增 Steamworks 编译引用。
        /// </summary>
        private static MethodInfo GetAllSubscribedItemsMethod()
        {
            if (allSubscribedItemsMethod != null)
                return allSubscribedItemsMethod;

            Type workshopType = AccessTools.TypeByName("Verse.Steam.Workshop");
            allSubscribedItemsMethod = workshopType?.GetMethod("AllSubscribedItems", BindingFlags.NonPublic | BindingFlags.Static);
            return allSubscribedItemsMethod;
        }

        /// <summary>
        /// 构建 Steam 客户端可识别的打开链接，负责让外部壳层直接跳转到创意工坊页面。
        /// </summary>
        private static string BuildSteamOpenUrl(string url)
        {
            return "steam://openurl/" + url;
        }

        /// <summary>
        /// 通过系统壳层打开链接，负责在不同平台上安全捕获协议未注册等失败情况。
        /// </summary>
        private static bool TryOpenWithSystemShell(string url)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[RSMF] 无法通过系统壳层打开链接: " + url + " - " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 读取模组元数据中的创意工坊文件 ID。
        /// </summary>
        private static string GetPublishedFileIdText(ModMetaData mod)
        {
            if (mod == null)
                return "";

            try
            {
                if (getPublishedFileIdMethod == null)
                    getPublishedFileIdMethod = typeof(ModMetaData).GetMethod("GetPublishedFileId", BindingFlags.Public | BindingFlags.Instance);
                return getPublishedFileIdMethod?.Invoke(mod, null)?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 规范化创意工坊文件 ID，负责容忍 XML 中误填完整链接。
        /// </summary>
        private static string NormalizePublishedFileId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string text = value.Trim();
            int idIndex = text.IndexOf("id=", StringComparison.OrdinalIgnoreCase);
            if (idIndex >= 0)
                text = text.Substring(idIndex + 3);

            int endIndex = text.IndexOfAny(new[] { '&', '?', '#', '/' });
            if (endIndex >= 0)
                text = text.Substring(0, endIndex);

            return text.Trim();
        }
    }

    /// <summary>
    /// 保存推荐扩展检测结果，负责给 UI 汇总展示状态。
    /// </summary>
    public class BusinessExtensionRecommendationStatus
    {
        public bool IsActive;
        public bool IsInstalled;
        public bool IsSubscribed;

        /// <summary>
        /// 判断推荐项是否应显示已完成勾选。
        /// </summary>
        public bool IsChecked => IsInstalled || IsSubscribed;
    }
}
