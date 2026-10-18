using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 负责保存当前 Steam 会话的最小身份信息和可用状态。
    /// </summary>
    [DataContract]
    public sealed class SteamSessionInfo
    {
        [DataMember] public bool IsAvailable;
        [DataMember] public string SteamId = "";
        [DataMember] public string PersonaName = "";
        [DataMember] public string ErrorMessage = "";
    }

    /// <summary>
    /// 负责承载网络蓝图服务状态返回。
    /// </summary>
    [DataContract]
    public sealed class BlueprintNetworkStatusData
    {
        [DataMember] public bool available;
        [DataMember] public string serviceName = "";
        [DataMember] public string version = "";
    }

    /// <summary>
    /// 负责承载网络蓝图列表项。
    /// </summary>
    [DataContract]
    public sealed class BlueprintNetworkListItemData
    {
        [DataMember] public string blueprintCode = "";
        [DataMember] public string steamId = "";
        [DataMember] public string name = "";
        [DataMember] public string description = "";
        [DataMember] public string previewUrl = "";
        [DataMember] public string detailUrl = "";
        [DataMember] public string downloadUrl = "";
        [DataMember] public int likeCount;
        [DataMember] public int downloadCount;
        [DataMember] public int requiredModCount;
        [DataMember] public bool? isCompatibleWithQueryMods;
        [DataMember] public string createdAt = "";
    }

    /// <summary>
    /// 负责承载网络蓝图分页列表。
    /// </summary>
    [DataContract]
    public sealed class BlueprintNetworkPagedListData
    {
        [DataMember] public List<BlueprintNetworkListItemData> items = new List<BlueprintNetworkListItemData>();
        [DataMember] public int page;
        [DataMember] public int pageSize;
        [DataMember] public int totalCount;
        [DataMember] public int totalPages;
    }

    /// <summary>
    /// 负责承载网络蓝图详情。
    /// </summary>
    [DataContract]
    public sealed class BlueprintNetworkDetailData
    {
        [DataMember] public string blueprintCode = "";
        [DataMember] public string steamId = "";
        [DataMember] public string name = "";
        [DataMember] public string description = "";
        [DataMember] public string originalFileName = "";
        [DataMember] public long fileSize;
        [DataMember] public string contentType = "";
        [DataMember] public int likeCount;
        [DataMember] public int downloadCount;
        [DataMember] public string createdAt = "";
        [DataMember] public string detailUrl = "";
        [DataMember] public string previewUrl = "";
        [DataMember] public string downloadUrl = "";
        [DataMember] public List<ShopBlueprintRequiredModData> requiredMods = new List<ShopBlueprintRequiredModData>();
    }

    /// <summary>
    /// 负责描述网络蓝图页当前查询模式。
    /// </summary>
    public enum BlueprintNetworkSortMode
    {
        Latest,
        Hot,
        Downloads,
        Mine,
        Compatible
    }

    /// <summary>
    /// 负责保存网络蓝图详情中的兼容校验结果。
    /// </summary>
    [DataContract]
    public sealed class BlueprintCompatibilityCheckResult
    {
        [DataMember] public bool IsCompatible;
        [DataMember] public List<ShopBlueprintRequiredModData> MissingMods = new List<ShopBlueprintRequiredModData>();
    }
}
