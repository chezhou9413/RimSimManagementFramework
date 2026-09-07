using SimManagementLib.Pojo;
using System;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责统一判定本地蓝图的网络来源、归属和可上传权限。
    /// </summary>
    public static class BlueprintOwnershipUtility
    {
        public const string SourceKindUploaded = "uploaded";
        public const string SourceKindImported = "imported";

        /// <summary>
        /// 负责判断蓝图是否带有远端蓝图码。
        /// </summary>
        public static bool HasRemoteCode(ShopBlueprintData data)
        {
            return data != null && !string.IsNullOrWhiteSpace(data.remoteBlueprintCode);
        }

        /// <summary>
        /// 负责判断蓝图是否是从网络下载导入的副本。
        /// </summary>
        public static bool IsImportedFromNetwork(ShopBlueprintData data)
        {
            if (data == null)
                return false;

            return string.Equals(data.remoteBlueprintSourceKind, SourceKindImported, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 负责判断蓝图是否是当前账号自己上传绑定的本地蓝图。
        /// </summary>
        public static bool IsUploadedByCurrentSteam(ShopBlueprintData data, string currentSteamId)
        {
            if (data == null || string.IsNullOrWhiteSpace(currentSteamId))
                return false;
            if (!HasRemoteCode(data))
                return false;
            if (!string.Equals(data.remoteBlueprintSourceKind, SourceKindUploaded, StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(data.remoteAuthorSteamId ?? string.Empty, currentSteamId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 负责判断蓝图当前是否允许作为新蓝图上传。
        /// </summary>
        public static bool CanUploadAsNew(ShopBlueprintData data)
        {
            if (data == null)
                return false;

            return !HasRemoteCode(data) && !IsImportedFromNetwork(data);
        }

        /// <summary>
        /// 负责判断蓝图当前是否允许更新到既有网络蓝图。
        /// </summary>
        public static bool CanUpdateExisting(ShopBlueprintData data, string currentSteamId)
        {
            return IsUploadedByCurrentSteam(data, currentSteamId);
        }

        /// <summary>
        /// 负责把蓝图标记为当前账号自己上传绑定的记录。
        /// </summary>
        public static void MarkAsUploaded(ShopBlueprintData data, string steamId)
        {
            if (data == null)
                return;

            data.remoteAuthorSteamId = steamId?.Trim() ?? string.Empty;
            data.remoteBlueprintSourceKind = SourceKindUploaded;
            data.remoteImportedAtTicks = 0L;
        }

        /// <summary>
        /// 负责把蓝图标记为从网络下载导入的本地副本。
        /// </summary>
        public static void MarkAsImported(ShopBlueprintData data, string steamId, long importedAtTicks)
        {
            if (data == null)
                return;

            data.remoteAuthorSteamId = steamId?.Trim() ?? string.Empty;
            data.remoteBlueprintSourceKind = SourceKindImported;
            data.remoteImportedAtTicks = importedAtTicks;
        }
    }
}
