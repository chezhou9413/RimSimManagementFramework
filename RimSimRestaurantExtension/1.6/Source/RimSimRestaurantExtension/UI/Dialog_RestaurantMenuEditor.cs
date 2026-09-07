using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimSimRestaurantExtension.Models;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //编辑一道菜的独立副本，职责是验证产物配方与每份食材后只回写父窗口草稿。
    internal sealed class Dialog_RestaurantMenuEditor : Window
    {
        private readonly Zone_Shop shop;
        private readonly Action<RestaurantMenuItem> confirm;
        private readonly RestaurantMenuItem draft;
        private readonly Dictionary<RestaurantIngredientRequirement, string> counts = new Dictionary<RestaurantIngredientRequirement, string>();
        private Vector2 scroll;
        private string price;
        private string minimum;
        private string maximum;
        private string error = "";
        public override Vector2 InitialSize => new Vector2(660f, Mathf.Min(680f, Verse.UI.screenHeight - 60f));

        //建立编辑副本与数字输入缓冲，职责是让关闭子窗口丢弃修改。
        public Dialog_RestaurantMenuEditor(Zone_Shop shop, RestaurantMenuItem item, Action<RestaurantMenuItem> confirm)
        {
            this.shop = shop;
            this.confirm = confirm;
            draft = item.Clone();
            price = draft.unitPrice.ToString(CultureInfo.InvariantCulture);
            minimum = draft.minCount.ToString();
            maximum = draft.maxCount.ToString();
            if (draft.ingredients.Count == 0)
                draft.ingredients = RestaurantIngredientUtility.BuildDefaultMenuIngredients(shop, draft.MealDef);
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        //绘制完整编辑窗口，职责是按实际行高保留标题、错误提示与确认区域。
        public override void DoWindowContents(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                float row = RestaurantUiStyle.ControlHeight() + 8f;
                Widgets.Label(new Rect(0f, 0f, rect.width - 34f, row), "编辑餐厅菜品");
                float errorHeight = error.NullOrEmpty() ? 0f : Text.CalcHeight(error, rect.width) + 8f;
                Rect body = new Rect(0f, row, rect.width, Mathf.Max(0f, rect.height - row * 2f - errorHeight - 8f));
                float width = body.width - 16f;
                string note = "每份原料必须满足该餐品的同一个生产配方。原料选择受配方过滤限制；菜品确认后仍需在店铺窗口统一保存。";
                float noteHeight = Text.CalcHeight(note, width);
                Rect view = new Rect(0f, 0f, width, Mathf.Max(body.height, row * (8 + draft.ingredients.Count) + noteHeight + 12f));
                Widgets.BeginScrollView(body, ref scroll, view);
                try { DrawForm(view.width, row, note, noteHeight); }
                finally { Widgets.EndScrollView(); }
                if (!error.NullOrEmpty())
                {
                    GUI.color = RestaurantUiStyle.Warning;
                    Widgets.Label(new Rect(0f, rect.height - row - errorHeight, rect.width, errorHeight), error);
                    GUI.color = Color.white;
                }
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(0f, rect.height - row + 8f, 100f, row - 8f), "取消")) Close();
                if (RestaurantUiStyle.DrawPrimaryButton(new Rect(rect.width - 150f, rect.height - row + 8f, 150f, row - 8f), "确认菜品草稿"))
                {
                    if (!Validate()) return;
                    confirm(draft.Clone());
                    Close();
                }
            }
        }

        //绘制文本、数字与原料字段，职责是保持输入缓冲并允许调整每份数量。
        private void DrawForm(float width, float row, string note, float noteHeight)
        {
            float y = 0f;
            draft.label = Field(width, y, row, "菜单名称", draft.label); y += row;
            Widgets.Label(new Rect(0f, y, 110f, row - 8f), "餐品产物");
            Widgets.ThingIcon(new Rect(114f, y, row - 8f, row - 8f), draft.MealDef);
            float labelWidth = Mathf.Max(80f, width - 266f);
            Widgets.Label(new Rect(160f, y, labelWidth, row - 8f), (draft.MealDef?.LabelCap.ToString() ?? "未选择").Truncate(labelWidth));
            if (RestaurantUiStyle.DrawSecondaryButton(new Rect(width - 96f, y, 96f, row - 8f), "选择产物"))
                Find.WindowStack.Add(new Dialog_SelectRestaurantFood(SelectFood));
            y += row;
            price = Field(width, y, row, "每份基础售价", price); y += row;
            minimum = Field(width, y, row, "最少份数", minimum); y += row;
            maximum = Field(width, y, row, "最多份数", maximum); y += row;
            Widgets.CheckboxLabeled(new Rect(0f, y, width, row - 8f), "启用此菜品", ref draft.enabled); y += row;
            Widgets.Label(new Rect(0f, y, width, row - 8f), "每份食材与数量"); y += row;
            foreach (var ingredient in draft.ingredients.ToList())
            {
                if (!counts.TryGetValue(ingredient, out string count)) count = ingredient.countPerMeal.ToString();
                float selectWidth = width - 190f;
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(0f, y, selectWidth, row - 8f),
                    (ingredient.ThingDef?.LabelCap.ToString() ?? "选择原料").Truncate(selectWidth - 12f)))
                    SelectIngredient(ingredient);
                counts[ingredient] = Widgets.TextField(new Rect(selectWidth + 8f, y, 96f, row - 8f), count);
                if (RestaurantUiStyle.DrawDangerButton(new Rect(width - 76f, y, 76f, row - 8f), "移除"))
                {
                    draft.ingredients.Remove(ingredient);
                    counts.Remove(ingredient);
                }
                y += row;
            }
            if (RestaurantUiStyle.DrawSecondaryButton(new Rect(0f, y, 130f, row - 8f), "添加食材"))
                SelectIngredient(null);
            y += row;
            Widgets.Label(new Rect(0f, y, width, noteHeight), note);
        }

        //绘制单个输入字段，职责是为标签和输入框使用统一行高。
        private static string Field(float width, float y, float row, string label, string value)
        {
            Widgets.Label(new Rect(0f, y, 110f, row - 8f), label);
            return Widgets.TextField(new Rect(118f, y, width - 118f, row - 8f), value ?? "");
        }

        //切换餐品产物，职责是按兼容配方重建原料并清理旧输入缓冲。
        private void SelectFood(ThingDef food)
        {
            draft.mealDefName = food.defName;
            draft.ingredients = RestaurantIngredientUtility.BuildDefaultMenuIngredients(shop, food);
            counts.Clear();
            error = "";
        }

        //选择受配方限制的食材，职责是不允许填入不被任何该产物配方接受的物品。
        private void SelectIngredient(RestaurantIngredientRequirement ingredient)
        {
            var allowed = RestaurantCookingUtility.GetProductionRecipes(draft.MealDef)
                .SelectMany(recipe => recipe.ingredients.SelectMany(slot => slot.filter.AllowedThingDefs
                    .Where(food => slot.IsFixedIngredient || recipe.fixedIngredientFilter.Allows(food))))
                .Distinct().OrderBy(food => food.label).ToList();
            var options = allowed.Select(food => new FloatMenuOption(food.LabelCap, () =>
            {
                var target = ingredient ?? new RestaurantIngredientRequirement();
                target.thingDefName = food.defName;
                if (ingredient == null) draft.ingredients.Add(target);
            })).ToList();
            if (options.Count == 0) { error = "该餐品没有可选生产原料。"; return; }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //校验编辑结果，职责是拒绝非法数字和无法满足同一个生产配方的食材组合。
        private bool Validate()
        {
            if (!float.TryParse(price, NumberStyles.Float, CultureInfo.InvariantCulture, out float unitPrice)
                || float.IsNaN(unitPrice) || float.IsInfinity(unitPrice) || unitPrice < 1f || unitPrice > 100000f)
            { error = "售价必须在 1 到 100000 之间。"; return false; }
            if (!int.TryParse(minimum, out int min) || !int.TryParse(maximum, out int max)
                || min < 1 || max < min || max > (draft.MealDef?.stackLimit ?? 0))
            { error = "份数必须为正整数，且最少份数不大于最多份数和餐品堆叠上限。"; return false; }
            foreach (var ingredient in draft.ingredients)
            {
                string count = counts.TryGetValue(ingredient, out var input) ? input : ingredient.countPerMeal.ToString();
                if (!int.TryParse(count, out int amount) || amount < 1 || amount > 100000)
                { error = "每份食材数量必须为 1 到 100000 的整数。"; return false; }
                ingredient.countPerMeal = amount;
            }
            var preview = new RestaurantOrder { mealDef = draft.MealDef, mealCount = 1,
                ingredients = RestaurantIngredientUtility.BuildNeeds(draft, 1) };
            if (!RestaurantCookingUtility.GetProductionRecipes(draft.MealDef)
                .Any(recipe => RestaurantCookingUtility.CanRecipeUseOrderIngredients(recipe, preview)))
            { error = "这组食材无法满足同一个生产配方，请核对原料种类和每份数量。"; return false; }
            draft.unitPrice = unitPrice;
            draft.minCount = min;
            draft.maxCount = max;
            error = "";
            return true;
        }
    }
}
