using LudeonTK;
using RimSimHotelExtension.Tool;
using RimWorld;
using Verse;

namespace RimSimHotelExtension.Debug
{
    //将可营业的旅馆商圈生成器接入原版开发者地图工具。
    public static class DebugActions_Hotel
    {
        //点击商圈中心，餐厅启用时一并生成其完整样板店。
        [DebugAction("SimHotel", "生成旅馆商圈 / Create hotel district (37×31 / 54×31)", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnHotelDistrict()
        {
            bool success = HotelDistrictCreationUtility.TryCreateHotelDistrict(Find.CurrentMap,
                Verse.UI.MouseCell(), out _, out string result);
            Messages.Message((success ? HotelText.T("RSH.Text.HotelDistrictCreated")
                : HotelText.T("RSH.Text.HotelDistrictCreationFailed")) + result,
                success ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, false);
        }
    }
}
