using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //显示餐厅菜单餐品选择器，职责是搜索所有存在普通制作配方的原版及模组餐品。
    public class Dialog_SelectRestaurantFood : Window
    {
        private const float RowHeight = 64f;
        private readonly Action<ThingDef> onSelected;
        private Vector2 scrollPosition;
        private string search = "";
        private MealTier tier = MealTier.All;
        private List<ThingDef> filteredCache;
        private string filteredSearch = "";
        private MealTier filteredTier;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(780f, Mathf.Max(420f, Verse.UI.screenWidth - 80f)),
            Mathf.Min(650f, Mathf.Max(420f, Verse.UI.screenHeight - 100f)));

        //创建餐品选择窗口，职责是保存选择回调并启用标准关闭行为。
        public Dialog_SelectRestaurantFood(Action<ThingDef> onSelected)
        {
            this.onSelected = onSelected;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        //绘制窗口内容，职责是按安全控件高度分配搜索、品质筛选和滚动列表。
        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
                RestaurantUiStyle.DrawPanel(inRect);
                Rect inner = inRect.ContractedBy(12f);
                float titleHeight = DrawTitle(new Rect(inner.x, inner.y, inner.width, inner.height));
                float controlHeight = RestaurantUiStyle.ControlHeight(GameFont.Tiny);
                Rect searchRect = new Rect(inner.x, inner.y + titleHeight + 8f, inner.width, controlHeight);
                DrawSearch(searchRect);
                Rect filterRect = new Rect(inner.x, searchRect.yMax + 6f, inner.width, controlHeight);
                DrawFilters(filterRect);
                List<ThingDef> foods = GetFilteredFoods();
                float countHeight = RestaurantUiStyle.LineHeight(GameFont.Tiny);
                Rect countRect = new Rect(inner.x, filterRect.yMax + 6f, inner.width, countHeight);
                Text.Font = GameFont.Tiny;
                GUI.color = RestaurantUiStyle.MutedText;
                Widgets.Label(countRect, $"找到 {foods.Count} 个可由普通工作台配方生产的餐品");
                Rect listRect = new Rect(inner.x, countRect.yMax + 4f, inner.width,
                    Mathf.Max(0f, inner.yMax - countRect.yMax - 4f));
                DrawFoodList(listRect, foods);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        //绘制标题说明，职责是动态测量中文副标题并说明配方限制。
        private static float DrawTitle(Rect rect)
        {
            float mediumLine = RestaurantUiStyle.LineHeight(GameFont.Medium);
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            string description = "只列出人类可食用、具有单一餐品产物且每批产出一份的普通制作配方；选择后会按当前餐厅灶台自动匹配一组食材。";
            float descriptionHeight = Text.CalcHeight(description, Mathf.Max(120f, rect.width));
            Text.Font = GameFont.Medium;
            Text.WordWrap = false;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 32f, mediumLine), "选择菜单餐品");
            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            GUI.color = RestaurantUiStyle.MutedText;
            Widgets.Label(new Rect(rect.x, rect.y + mediumLine + 4f, rect.width, descriptionHeight), description);
            GUI.color = Color.white;
            return mediumLine + 4f + descriptionHeight;
        }

        //绘制搜索栏，职责是支持标签、DefName 和来源模组并提供清空入口。
        private void DrawSearch(Rect rect)
        {
            const float labelWidth = 44f;
            const float clearWidth = 72f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), "搜索");
            Rect field = new Rect(rect.x + labelWidth + 6f, rect.y,
                Mathf.Max(80f, rect.width - labelWidth - clearWidth - 20f), rect.height);
            string next = Widgets.TextField(field, search ?? "");
            if (next != search)
            {
                search = next ?? "";
                InvalidateFilter();
            }
            Rect clear = new Rect(field.xMax + 8f, rect.y, clearWidth, rect.height);
            if (RestaurantUiStyle.DrawSecondaryButton(clear, "清空", !search.NullOrEmpty(), GameFont.Tiny))
            {
                search = "";
                InvalidateFilter();
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        //绘制餐品品质筛选，职责是把长列表按原版偏好等级快速缩小。
        private void DrawFilters(Rect rect)
        {
            const float gap = 6f;
            float width = (rect.width - gap * 3f) / 4f;
            DrawFilter(new Rect(rect.x, rect.y, width, rect.height), MealTier.All, "全部");
            DrawFilter(new Rect(rect.x + width + gap, rect.y, width, rect.height), MealTier.Simple, "家常");
            DrawFilter(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height), MealTier.Fine, "精致");
            DrawFilter(new Rect(rect.x + (width + gap) * 3f, rect.y, width, rect.height), MealTier.Lavish, "豪华及其他");
        }

        //绘制单个筛选按钮，职责是维护选中态并重置滚动位置。
        private void DrawFilter(Rect rect, MealTier target, string label)
        {
            bool selected = tier == target;
            bool clicked = selected
                ? RestaurantUiStyle.DrawPrimaryButton(rect, label, true, GameFont.Tiny)
                : RestaurantUiStyle.DrawSecondaryButton(rect, label, true, GameFont.Tiny);
            if (!clicked || selected) return;
            tier = target;
            InvalidateFilter();
        }

        //绘制餐品滚动列表，职责是使用固定安全行高并保证异常退出也能结束裁剪区。
        private void DrawFoodList(Rect rect, List<ThingDef> foods)
        {
            if (foods.Count == 0)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = RestaurantUiStyle.MutedText;
                Widgets.Label(rect.ContractedBy(12f), "没有匹配餐品。请检查搜索词、品质筛选或当前加载的餐品模组。" );
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }
            float viewWidth = Mathf.Max(1f, rect.width - 16f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, foods.Count * (RowHeight + 5f)));
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < foods.Count; i++)
                {
                    DrawFoodRow(new Rect(0f, y, viewWidth, RowHeight), foods[i]);
                    y += RowHeight + 5f;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        //绘制单个餐品行，职责是显示图标、营养、来源、配方数量和明确选择按钮。
        private void DrawFoodRow(Rect rect, ThingDef def)
        {
            RestaurantUiStyle.DrawCard(rect);
            Rect icon = new Rect(rect.x + 8f, rect.y + 8f, 48f, 48f);
            Widgets.ThingIcon(icon.ContractedBy(3f), def);
            float buttonWidth = 82f;
            float buttonHeight = RestaurantUiStyle.ControlHeight(GameFont.Tiny);
            Rect button = new Rect(rect.xMax - buttonWidth - 8f, rect.center.y - buttonHeight / 2f, buttonWidth, buttonHeight);
            float textWidth = Mathf.Max(80f, button.x - icon.xMax - 18f);
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            GUI.color = Color.white;
            Widgets.Label(new Rect(icon.xMax + 8f, rect.y + 7f, textWidth, RestaurantUiStyle.LineHeight(GameFont.Small)),
                def.LabelCap.ToString().Truncate(textWidth));
            Text.Font = GameFont.Tiny;
            GUI.color = RestaurantUiStyle.MutedText;
            string details = $"{RestaurantFoodUtility.BuildFoodSummary(def)} · 配方 {RestaurantCookingUtility.GetProductionRecipes(def).Count}";
            Widgets.Label(new Rect(icon.xMax + 8f, rect.y + 35f, textWidth, RestaurantUiStyle.LineHeight(GameFont.Tiny)),
                details.Truncate(textWidth));
            bool clicked = RestaurantUiStyle.DrawPrimaryButton(button, "选择", true, GameFont.Tiny);
            if (clicked || Widgets.ButtonInvisible(new Rect(rect.x, rect.y, button.x - rect.x - 4f, rect.height), false))
            {
                onSelected?.Invoke(def);
                Close();
            }
            GUI.color = Color.white;
        }

        //返回缓存后的筛选结果，职责是避免每帧重新扫描全部 Def。
        private List<ThingDef> GetFilteredFoods()
        {
            string key = (search ?? "").Trim();
            if (filteredCache != null && filteredSearch == key && filteredTier == tier) return filteredCache;
            filteredSearch = key;
            filteredTier = tier;
            filteredCache = RestaurantFoodUtility.AllMenuFoods()
                .Where(def => MatchesTier(def) && RestaurantFoodUtility.MatchesSearch(def, key))
                .ToList();
            return filteredCache;
        }

        //判断餐品是否符合品质筛选，职责是按原版 FoodPreferability 分类而不依赖 DefName。
        private bool MatchesTier(ThingDef def)
        {
            FoodPreferability preferability = def?.ingestible?.preferability ?? FoodPreferability.Undefined;
            if (tier == MealTier.Simple) return preferability <= FoodPreferability.MealSimple;
            if (tier == MealTier.Fine) return preferability == FoodPreferability.MealFine;
            if (tier == MealTier.Lavish) return preferability >= FoodPreferability.MealLavish;
            return true;
        }

        //清理筛选缓存，职责是让搜索或品质变化立即刷新列表并回到顶部。
        private void InvalidateFilter()
        {
            filteredCache = null;
            scrollPosition = Vector2.zero;
        }

        //描述餐品品质筛选，职责是限制窗口状态为明确的四种模式。
        private enum MealTier
        {
            All,
            Simple,
            Fine,
            Lavish
        }
    }
}
