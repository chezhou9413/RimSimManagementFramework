using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingComp
{
    //缓存组件货柜操作按钮使用的框架图标，避免每帧重复检索资源。
    [StaticConstructorOnStartup]
    internal static class StorageSalesIcons
    {
        //框架商店主图标，用于标识组件货柜销售开关。
        internal static readonly Texture2D SalesToggle = ContentFinder<Texture2D>.Get("Things/Icon/Mod_Icon", true);

        //与框架货柜和商店管理一致的管理界面图标。
        internal static readonly Texture2D ManageStorage = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true);
    }
}
