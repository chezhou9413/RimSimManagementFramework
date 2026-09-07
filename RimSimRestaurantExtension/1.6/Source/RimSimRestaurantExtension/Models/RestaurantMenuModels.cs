using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Models
{
    //保存一道菜单菜品的食材需求，负责让点餐、制作和调试生成共用同一份配方数据。
    public class RestaurantIngredientRequirement : IExposable
    {
        public string thingDefName = "";
        public int countPerMeal = 1;

        //返回食材 Def，负责把存档中的字符串解析成原版物品定义。
        public ThingDef ThingDef => DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);

        //读写食材需求，并在读档后补齐安全默认值。
        public void ExposeData()
        {
            Scribe_Values.Look(ref thingDefName, "thingDefName", "");
            Scribe_Values.Look(ref countPerMeal, "countPerMeal", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (thingDefName == null) thingDefName = "";
                countPerMeal = Mathf.Max(1, countPerMeal);
            }
        }

        //创建食材需求副本，负责避免菜单模板和订单明细共享可变对象。
        public RestaurantIngredientRequirement Clone()
        {
            return new RestaurantIngredientRequirement
            {
                thingDefName = thingDefName ?? "",
                countPerMeal = Mathf.Max(1, countPerMeal)
            };
        }
    }

    //保存店铺菜单中的一道菜，负责描述顾客可点的餐品、价格、份数和食材。
    public class RestaurantMenuItem : IExposable
    {
        public string id = "";
        public Buildings.Building_RestaurantStorage sourceCabinet;
        public Inventory.RestaurantDeliveryMode deliveryMode;
        public float selectionWeight = 1f;
        public bool IsStockProduct => sourceCabinet != null;
        public string label = "";
        public string mealDefName = "MealSimple";
        public bool enabled = true;
        public float unitPrice = 45f;
        public int minCount = 1;
        public int maxCount = 2;
        public List<RestaurantIngredientRequirement> ingredients = new List<RestaurantIngredientRequirement>();

        //返回菜单产出的餐品 Def，职责是让无效配置显式保持无效而不静默替换成简单餐。
        public ThingDef MealDef => DefDatabase<ThingDef>.GetNamedSilentFail(mealDefName);

        //返回菜单显示名，负责在 UI、订单和提示中使用稳定文本。
        public string DisplayLabel => string.IsNullOrEmpty(label) ? MealDef?.LabelCap.RawText ?? "无效餐品" : label;

        //读写菜单项，并在读档后夹紧价格和份数范围。
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref mealDefName, "mealDefName", "MealSimple");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 45f);
            Scribe_Values.Look(ref minCount, "minCount", 1);
            Scribe_Values.Look(ref maxCount, "maxCount", 2);
            Scribe_Collections.Look(ref ingredients, "ingredients", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Normalize();
        }

        //规范化菜单字段，负责防止 UI 和点餐逻辑遇到空列表或非法范围。
        public void Normalize()
        {
            if (id.NullOrEmpty()) id = "menu_" + Guid.NewGuid().ToString("N");
            if (label == null) label = "";
            if (mealDefName == null) mealDefName = "";
            unitPrice = Mathf.Clamp(unitPrice, 1f, 100000f);
            int stackLimit = Mathf.Max(1, MealDef?.stackLimit ?? 1);
            minCount = Mathf.Clamp(minCount, 1, stackLimit);
            maxCount = Mathf.Clamp(maxCount, minCount, stackLimit);
            if (ingredients == null) ingredients = new List<RestaurantIngredientRequirement>();
            ingredients.RemoveAll(i => i == null || i.thingDefName.NullOrEmpty());
            for (int i = 0; i < ingredients.Count; i++)
                ingredients[i].countPerMeal = Mathf.Max(1, ingredients[i].countPerMeal);
        }

        //创建菜单项副本，负责把默认菜单安全写入单店设置。
        public RestaurantMenuItem Clone()
        {
            RestaurantMenuItem clone = new RestaurantMenuItem
            {
                id = id ?? "",
                label = label ?? "",
                mealDefName = mealDefName ?? "",
                enabled = enabled,
                unitPrice = unitPrice,
                minCount = minCount,
                maxCount = maxCount,
                ingredients = new List<RestaurantIngredientRequirement>()
            };
            if (ingredients != null)
            {
                for (int i = 0; i < ingredients.Count; i++)
                    if (ingredients[i] != null)
                        clone.ingredients.Add(ingredients[i].Clone());
            }
            clone.Normalize();
            return clone;
        }
    }

    //保存顾客一次点餐的临时选择，负责让价格计算和订单创建保持一致。
    public class RestaurantMenuSelection
    {
        public RestaurantMenuItem menuItem;
        public int count;
        public float totalPrice;
        public RestaurantCustomerPreference preference;
        public float preferenceScore;
        public string selectionReason = "";
        public int createdTick;

        //判断选择是否仍可使用，负责避免过期缓存生成无效订单。
        public bool IsValid => menuItem?.MealDef != null && count > 0 && totalPrice > 0f;
    }
}
