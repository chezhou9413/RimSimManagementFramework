using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //提供餐厅经营总览的状态计算与通用绘制，职责是让页面类只处理响应式布局和滚动列表。
    internal static class RestaurantBusinessUiUtility
    {
        //构造餐厅阻塞提示，职责是优先报告停用、无菜单、无灶台和员工缺失。
        public static string BuildShopIssue(Zone_Shop shop, RestaurantShopSettings settings,
            List<RestaurantOrder> active, int menuCount, int stoves)
        {
            string issue = RestaurantBusinessAvailability.Snapshot(shop);
            return issue.NullOrEmpty() ? "正常：当前设施、员工、库存与餐位检查通过。" : issue;
        }

        //返回状态中文标签，职责是避免界面泄露内部枚举名。
        public static string StateLabel(RestaurantOrderState state)
        {
            switch (state)
            {
                case RestaurantOrderState.WaitingCook: return "待制作";
                case RestaurantOrderState.Cooking: return "制作中";
                case RestaurantOrderState.ReadyToDeliver: return "待送餐";
                case RestaurantOrderState.Delivering: return "送餐中";
                case RestaurantOrderState.GoingToSeat: return "前往座位";
                case RestaurantOrderState.WaitingOrder: return "等待接单";
                case RestaurantOrderState.TakingOrder: return "接单中";
                case RestaurantOrderState.ChefBringingToPass: return "厨师出餐中";
                case RestaurantOrderState.AwaitingCheckout: return "待付款";
                case RestaurantOrderState.Dining: return "用餐中";
                case RestaurantOrderState.Completed: return "已完成";
                case RestaurantOrderState.Canceled: return "已取消";
                default: return "失败";
            }
        }

        //返回状态标签底色，职责是区分成功、进行中与异常订单。
        public static Color StateColor(RestaurantOrderState state)
        {
            if (state == RestaurantOrderState.Completed) return new Color(0.25f, 0.75f, 0.35f, 0.24f);
            if (state == RestaurantOrderState.Canceled || state == RestaurantOrderState.Failed)
                return new Color(0.9f, 0.35f, 0.35f, 0.24f);
            return new Color(0.25f, 0.65f, 0.85f, 0.24f);
        }

        //判断商店是否包含餐厅点餐台，职责是避免普通零售店被统计为餐厅。
        public static bool IsRestaurantShop(Zone_Shop shop)
        {
            if (shop?.Map == null || DefOfRefs.RSR_RestaurantOrderCounter == null) return false;
            foreach (IntVec3 cell in shop.Cells)
                if (cell.GetThingList(shop.Map).Any(thing => thing?.def == DefOfRefs.RSR_RestaurantOrderCounter)) return true;
            return false;
        }

        //绘制空状态，职责是保持中文多行文本居中且不污染后续锚点。
        public static void DrawEmpty(Rect rect, string message)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = true;
            GUI.color = RestaurantUiStyle.MutedText;
            Widgets.Label(rect.ContractedBy(12f), message ?? "");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

    }
}
