using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Models
{
    //描述餐厅订单的业务阶段，职责是约束顾客、厨师和服务员之间的状态交接。
    public enum RestaurantOrderState
    {
        GoingToSeat,
        WaitingOrder,
        TakingOrder,
        WaitingCook,
        Cooking,
        ChefBringingToPass,
        ReadyToDeliver,
        Delivering,
        Dining,
        AwaitingCheckout,
        Completed,
        Canceled,
        Failed
    }

    //保存一张餐厅订单，职责是跨 Job 和存档追踪动作订单、餐位、菜品、员工与结算状态。
    public class RestaurantOrder : IExposable
    {
        public int orderId;
        public int sessionId, round, acceptedCount;
        public bool stockProduct;
        public Inventory.RestaurantDeliveryMode mode;
        public Buildings.Building_RestaurantStorage sourceCabinet;
        public List<Thing> goods = new List<Thing>();
        public int actionOrderId = -1;
        public int customerThingId = -1;
        public int shopZoneId = -1;
        public int providerThingId = -1;
        public string menuItemId = "";
        public string menuItemLabel = "";
        public float unitPrice;
        public ThingDef mealDef;
        public int mealCount = 1;
        public int mealThingId = -1;
        public Thing meal;
        public RecipeDef recipe;
        public int mapId = -1;
        public int seatedTick = -1;
        public int orderedTick = -1;
        public bool menuConfirmed;
        public bool dishBubbleShown;
        public bool mealProduced;
        public bool mealDelivered;
        public bool mealConsumed;
        public IntVec3 cookedMealCell = IntVec3.Invalid;
        public int stoveThingId = -1;
        public float price;
        public float paidAmount;
        public float ingredientCost;
        public List<RestaurantIngredientRequirement> ingredients = new List<RestaurantIngredientRequirement>();
        public RestaurantOrderState state = RestaurantOrderState.GoingToSeat;
        public IntVec3 seatCell = IntVec3.Invalid;
        public int tableThingId = -1;
        public int createdTick;
        public int cookingStartedTick;
        public int cookedTick;
        public int deliveredTick;
        public int diningStartedTick;
        public int completedTick;
        public int lastProgressTick;
        public int cookThingId = -1;
        public int waiterThingId = -1;
        public bool mealLockedForbidden;
        public bool chargeQueued;
        public string preferenceSummary = "";
        public string selectionReason = "";
        public float preferenceScore;
        public string failReason = "";
        public string blockReason = "";

        //返回订单是否已经结束，职责是让管理器、结账门禁和岗位扫描共享同一终态判定。
        public bool IsTerminal => state == RestaurantOrderState.Completed
            || state == RestaurantOrderState.Canceled
            || state == RestaurantOrderState.Failed;

        //返回订单是否仍需餐厅员工推进，职责是决定关店后是否保留厨师和服务员工作。
        public bool NeedsStaffWork => state == RestaurantOrderState.WaitingOrder
            || state == RestaurantOrderState.TakingOrder
            || state == RestaurantOrderState.ChefBringingToPass
            || state == RestaurantOrderState.WaitingCook
            || state == RestaurantOrderState.Cooking
            || state == RestaurantOrderState.ReadyToDeliver
            || state == RestaurantOrderState.Delivering;

        //读写餐厅订单数据，职责是保证读档后集合和数值仍处于合法范围。
        public void ExposeData()
        {
            Scribe_Values.Look(ref orderId, "orderId", 0);
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref round, "round");
            Scribe_Values.Look(ref acceptedCount, "acceptedCount");
            Scribe_Values.Look(ref stockProduct, "stockProduct");
            Scribe_Values.Look(ref mode, "deliveryMode");
            Scribe_References.Look(ref sourceCabinet, "sourceCabinet");
            Scribe_Collections.Look(ref goods, "goods", LookMode.Reference);
            Scribe_Values.Look(ref actionOrderId, "actionOrderId", -1);
            Scribe_Values.Look(ref customerThingId, "customerThingId", -1);
            Scribe_Values.Look(ref shopZoneId, "shopZoneId", -1);
            Scribe_Values.Look(ref providerThingId, "providerThingId", -1);
            Scribe_Values.Look(ref menuItemId, "menuItemId", "");
            Scribe_Values.Look(ref menuItemLabel, "menuItemLabel", "");
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Defs.Look(ref mealDef, "mealDef");
            Scribe_Values.Look(ref mealCount, "mealCount", 1);
            Scribe_Values.Look(ref mealThingId, "mealThingId", -1);
            Scribe_References.Look(ref meal, "meal");
            Scribe_Defs.Look(ref recipe, "recipe");
            Scribe_Values.Look(ref mapId, "mapId", -1);
            Scribe_Values.Look(ref seatedTick, "seatedTick", -1);
            Scribe_Values.Look(ref orderedTick, "orderedTick", -1);
            Scribe_Values.Look(ref menuConfirmed, "menuConfirmed", false);
            Scribe_Values.Look(ref dishBubbleShown, "dishBubbleShown", false);
            Scribe_Values.Look(ref mealProduced, "mealProduced", false);
            Scribe_Values.Look(ref mealDelivered, "mealDelivered", false);
            Scribe_Values.Look(ref mealConsumed, "mealConsumed", false);
            Scribe_Values.Look(ref cookedMealCell, "cookedMealCell", IntVec3.Invalid);
            Scribe_Values.Look(ref stoveThingId, "stoveThingId", -1);
            Scribe_Values.Look(ref price, "price", 0f);
            Scribe_Values.Look(ref paidAmount, "paidAmount", 0f);
            Scribe_Values.Look(ref ingredientCost, "ingredientCost", 0f);
            Scribe_Collections.Look(ref ingredients, "ingredients", LookMode.Deep);
            Scribe_Values.Look(ref state, "state", RestaurantOrderState.GoingToSeat);
            Scribe_Values.Look(ref seatCell, "seatCell", IntVec3.Invalid);
            Scribe_Values.Look(ref tableThingId, "tableThingId", -1);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref cookingStartedTick, "cookingStartedTick", 0);
            Scribe_Values.Look(ref cookedTick, "cookedTick", 0);
            Scribe_Values.Look(ref deliveredTick, "deliveredTick", 0);
            Scribe_Values.Look(ref diningStartedTick, "diningStartedTick", 0);
            Scribe_Values.Look(ref completedTick, "completedTick", 0);
            Scribe_Values.Look(ref lastProgressTick, "lastProgressTick", 0);
            Scribe_Values.Look(ref cookThingId, "cookThingId", -1);
            Scribe_Values.Look(ref waiterThingId, "waiterThingId", -1);
            Scribe_Values.Look(ref mealLockedForbidden, "mealLockedForbidden", false);
            Scribe_Values.Look(ref chargeQueued, "chargeQueued", false);
            Scribe_Values.Look(ref preferenceSummary, "preferenceSummary", "");
            Scribe_Values.Look(ref selectionReason, "selectionReason", "");
            Scribe_Values.Look(ref preferenceScore, "preferenceScore", 0f);
            Scribe_Values.Look(ref failReason, "failReason", "");
            Scribe_Values.Look(ref blockReason, "blockReason", "");

            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            mealCount = Mathf.Max(1, mealCount);
            ingredientCost = Mathf.Max(0f, ingredientCost);
            menuItemId = menuItemId ?? "";
            menuItemLabel = menuItemLabel ?? "";
            preferenceSummary = preferenceSummary ?? "";
            selectionReason = selectionReason ?? "";
            failReason = failReason ?? "";
            ingredients = ingredients ?? new List<RestaurantIngredientRequirement>();
            ingredients.RemoveAll(item => item == null || item.thingDefName.NullOrEmpty());
            if (unitPrice <= 0f && price > 0f)
                unitPrice = price / mealCount;
            if (lastProgressTick <= 0)
                lastProgressTick = createdTick;
        }

        //记录最近一次有效推进，职责是让守护逻辑区分正常长流程与真正卡单。
        public void TouchProgress()
        {
            lastProgressTick = Find.TickManager?.TicksGame ?? lastProgressTick;
        }

        //返回订单总食材需求的独立副本，职责是避免厨房逻辑修改订单持久化明细。
        public List<RestaurantIngredientRequirement> GetTotalIngredientNeeds()
        {
            if (ingredients == null) return new List<RestaurantIngredientRequirement>();
            return ingredients
                .Where(item => item != null && !item.thingDefName.NullOrEmpty())
                .GroupBy(item => item.thingDefName)
                .Select(group => new RestaurantIngredientRequirement
                {
                    thingDefName = group.Key,
                    countPerMeal = group.Sum(item => Mathf.Max(1, item.countPerMeal))
                })
                .ToList();
        }
    }
}
