using LudeonTK;
using RimWorld;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimRestaurantExtension.Debug
{
    //提供餐厅扩展开发模式入口，职责是把完整餐厅创建函数接入地图调试工具。
    public static class DebugActions_Restaurant
    {
        //在鼠标点选位置生成可直接营业的完整餐厅。
        [DebugAction("SimRestaurant", "生成完整餐厅（含食材）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnCompleteRestaurantAtCell()
        {
            Map map = Find.CurrentMap;
            IntVec3 center = Verse.UI.MouseCell();
            if (!CompleteRestaurantCreationUtility.TryCreateCompleteRestaurant(map, center,
                    out Zone_Shop shop, out string result))
            {
                Messages.Message("生成完整餐厅失败：" + result, MessageTypeDefOf.RejectInput, false);
                return;
            }
            Messages.Message("已生成完整餐厅：" + result + "。", MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
