using HarmonyLib;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.Patch
{
    //接入主菜单更新循环，职责是在主菜单就绪后触发本地公告检查。
    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.UIRootUpdate))]
    public static class Patch_UIRootEntry_Announcements
    {
        //在原版界面更新后检查公告，职责是让未进入存档的玩家也能接收模组公告。
        public static void Postfix()
        {
            AnnouncementClientState.TryCheckOnMainMenu();
        }
    }
}
