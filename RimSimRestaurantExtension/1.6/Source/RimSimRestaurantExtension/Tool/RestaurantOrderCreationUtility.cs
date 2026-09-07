using System.Linq;
using RimSimRestaurantExtension.GameComp;
using RimSimRestaurantExtension.Models;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅订单创建前的目标解析与最终校验，职责是隔离动作 Worker 的无副作用准备逻辑。
    internal static class RestaurantOrderCreationUtility
    {
        //查找店内点餐台，职责是给动作目标、缓存键和订单追踪提供同一设施。
        public static Thing FindOrderCounter(Zone_Shop shop)
        {
            if (shop?.Map == null || DefOfRefs.RSR_RestaurantOrderCounter == null) return null;
            foreach (IntVec3 cell in shop.Cells)
            {
                Thing counter = cell.GetThingList(shop.Map)
                    .FirstOrDefault(thing => thing != null && !thing.Destroyed
                        && thing.def == DefOfRefs.RSR_RestaurantOrderCounter);
                if (counter != null) return counter;
            }
            return null;
        }

        //构造菜单制作预览，职责是让动作可用性和最终落单使用相同餐品、份数与整单食材。
        public static RestaurantOrder BuildPreview(RestaurantMenuSelection selection)
        {
            return selection?.menuItem == null ? null : new RestaurantOrder
            {
                stockProduct = selection.menuItem.IsStockProduct, sourceCabinet = selection.menuItem.sourceCabinet,
                mode = selection.menuItem.deliveryMode, mealDef = selection.menuItem.MealDef,
                mealCount = selection.count,
                ingredients = RestaurantIngredientUtility.BuildNeeds(selection.menuItem, selection.count)
            };
        }

        //在写入订单前重新校验选择，职责是阻止缓存期间发生的改价、缺货或设施失效生成坏单。
        public static bool ValidateSelectionAtOrderTime(Pawn customer, Zone_Shop shop,
            RestaurantShopSettings settings, RestaurantMenuSelection selection)
        {
            RestaurantMenuItem menu = Inventory.RestaurantProductMenuUtility.AllMenus(shop, settings)
                .FirstOrDefault(item => item != null && item.id == selection?.menuItem?.id);
            if (customer == null || shop == null || menu == null || !menu.enabled
                || (!menu.IsStockProduct && !RestaurantFoodUtility.IsMenuFood(menu.MealDef)))
            {
                return false;
            }
            if (selection.count < menu.minCount || selection.count > menu.maxCount
                || selection.count > menu.MealDef.stackLimit)
            {
                return false;
            }
            if (!Inventory.RestaurantProductMenuUtility.Accepts(customer, menu)) return false;

            float unitPrice = Mathf.Max(1f, menu.unitPrice * settings.priceMultiplier);
            float totalPrice = unitPrice * selection.count;
            if (totalPrice > Inventory.RestaurantProductMenuUtility.RemainingBudget(customer, shop)) return false;
            if (menu.IsStockProduct ? !Inventory.RestaurantProductMenuUtility.Available(shop, menu, selection.count, customer)
                : !RestaurantIngredientUtility.HasIngredients(customer, shop, menu, selection.count)) return false;

            selection.menuItem = menu;
            selection.totalPrice = totalPrice;
            RestaurantOrder preview = BuildPreview(selection);
            return (menu.IsStockProduct || RestaurantCookingUtility.FindUsableStoves(shop, preview).Count > 0
                && RestaurantStaffAvailabilityUtility.HasCook(shop, preview))
                && RestaurantStaffAvailabilityUtility.HasStaffForWorkGiver(shop,
                    DefOfRefs.RSR_WorkGiver_DeliverRestaurantOrder);
        }
    }
}
