using System;
using System.Reflection;
using RimSimHotelExtension.Tool;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimHotelExtension.Debug
{
    //仅在餐厅扩展启用时绑定其调试入口，不产生餐厅程序集依赖。
    internal static class HotelDistrictRestaurantUtility
    {
        //完整餐厅现有布局为十五格见方，外围保留商圈通道。
        public const int Size = 15;
        internal delegate bool ValidateRestaurant(Map map, IntVec3 center, out string reason);
        internal delegate bool CreateRestaurant(Map map, IntVec3 center, out Zone_Shop shop, out string result);

        //未启用餐厅时正常返回空入口；版本缺少接口时明确报告，不能假装生成成功。
        public static bool TryResolve(out ValidateRestaurant validate, out CreateRestaurant create, out string reason)
        {
            validate = null;
            create = null;
            reason = "";
            if (ModLister.GetActiveModWithIdentifier("chezhou.rimsim.restaurantextension", true) == null) return true;
            Type type = Type.GetType("RimSimRestaurantExtension.Debug.CompleteRestaurantCreationUtility, RimSimRestaurantExtension", false);
            MethodInfo check = type?.GetMethod("CanCreateCompleteRestaurant", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Map), typeof(IntVec3), typeof(string).MakeByRefType() }, null);
            MethodInfo build = type?.GetMethod("TryCreateCompleteRestaurant", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Map), typeof(IntVec3), typeof(Zone_Shop).MakeByRefType(), typeof(string).MakeByRefType() }, null);
            if (check?.ReturnType != typeof(bool) || build?.ReturnType != typeof(bool))
            {
                reason = HotelText.T("RSH.Debug.RestaurantApiMissing");
                return false;
            }
            validate = check.CreateDelegate<ValidateRestaurant>();
            create = build.CreateDelegate<CreateRestaurant>();
            return true;
        }
    }
}
