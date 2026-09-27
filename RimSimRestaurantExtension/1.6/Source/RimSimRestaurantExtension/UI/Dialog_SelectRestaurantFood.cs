using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //显示餐厅食品选择器，职责是按名称、品质及模组筛选餐品或配方允许的食材。
    public class Dialog_SelectRestaurantFood : Window
    {
        private static float RowHeight => RestaurantFoodRow.Height;
        private readonly Action<ThingDef> onSelected;
        private readonly List<ThingDef> candidates;
        private readonly RestaurantFoodModFilter modFilter;
        private readonly bool selectingIngredient;
        private Vector2 scrollPosition;
        private string search = "";
        private MealTier tier = MealTier.All;
        private List<ThingDef> filteredCache;
        private string filteredSearch = "";
        private MealTier filteredTier;

        public override Vector2 InitialSize => new Vector2(
            Mathf.Min(980f, Verse.UI.screenWidth - 48f),
            Mathf.Min(720f, Verse.UI.screenHeight - 48f));

        //创建餐品选择窗口，职责是保存选择回调并启用标准关闭行为。
        public Dialog_SelectRestaurantFood(Action<ThingDef> onSelected)
            : this(onSelected, null)
        {
        }

        //建立候选快照，职责是让食材选择遵守调用者的配方限制并共用食品浏览界面。
        public Dialog_SelectRestaurantFood(Action<ThingDef> onSelected, IEnumerable<ThingDef> allowedIngredients)
        {
            this.onSelected = onSelected;
            selectingIngredient = allowedIngredients != null;
            candidates = (allowedIngredients ?? RestaurantFoodUtility.AllMenuFoods()).Distinct().ToList();
            modFilter = new RestaurantFoodModFilter(candidates);
            doCloseX = true;
            closeOnAccept = false;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        //绘制窗口内容，职责是为来源侧栏、搜索、品质筛选和取消按钮保留独立空间。
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
                float controlHeight = RestaurantUiStyle.ControlHeight();
                Rect searchRect = new Rect(inner.x, inner.y + titleHeight + 8f, inner.width, controlHeight);
                DrawSearch(searchRect);
                float bodyTop = searchRect.yMax + 8f;
                float footerTop = inner.yMax - controlHeight;
                float sourceWidth = Mathf.Min(250f, inner.width * 0.32f);
                Rect sourceRect = new Rect(inner.x, bodyTop, sourceWidth, Mathf.Max(0f, footerTop - bodyTop - 8f));
                if (modFilter.Draw(sourceRect)) InvalidateFilter();
                Rect results = new Rect(sourceRect.xMax + 10f, bodyTop, inner.width - sourceWidth - 10f, sourceRect.height);
                float contentTop = results.y;
                if (!selectingIngredient)
                {
                    DrawFilters(new Rect(results.x, contentTop, results.width, controlHeight));
                    contentTop += controlHeight + 6f;
                }
                List<ThingDef> foods = GetFilteredFoods();
                float countHeight = RestaurantUiStyle.LineHeight(GameFont.Small);
                Rect countRect = new Rect(results.x, contentTop, results.width, countHeight);
                ShopUiVisualUtility.DrawCellLabel(countRect, SimTranslation.T("RSR.UI.AvailableFoods", foods.Count.Named("count")), RestaurantUiStyle.MutedText);
                Rect listRect = new Rect(results.x, countRect.yMax + 4f, results.width,
                    Mathf.Max(0f, results.yMax - countRect.yMax - 4f));
                ShopUiVisualUtility.DrawSection(listRect);
                DrawFoodList(listRect, foods);
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(inner.x, footerTop, 100f, controlHeight), SimTranslation.T("RSR.UI.Cancel"))) Close();
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                GUI.color = oldColor;
            }
        }

        //绘制标题说明，职责是区分菜单产物和受配方限制的食材选择。
        private float DrawTitle(Rect rect)
        {
            return ShopUiVisualUtility.DrawPageHeading(rect,
                SimTranslation.T(selectingIngredient ? "RSR.UI.SelectIngredient" : "RSR.UI.SelectMenuFood"),
                SimTranslation.T(selectingIngredient ? "RSR.UI.SelectIngredientHint" : "RSR.UI.SelectMenuFoodHint"), true);
        }

        //绘制搜索栏，职责是支持标签、DefName 和来源模组并提供清空入口。
        private void DrawSearch(Rect rect)
        {
            string next = ShopUiVisualUtility.DrawSearchField(rect, search, SimTranslation.T("RSR.UI.SearchFoodSelection"));
            if (next != search) { search = next; InvalidateFilter(); }
        }

        //绘制餐品品质筛选，职责是把长列表按原版偏好等级快速缩小。
        private void DrawFilters(Rect rect)
        {
            const float gap = 6f;
            float width = (rect.width - gap * 3f) / 4f;
            DrawFilter(new Rect(rect.x, rect.y, width, rect.height), MealTier.All, SimTranslation.T("RSR.UI.AllFood"));
            DrawFilter(new Rect(rect.x + width + gap, rect.y, width, rect.height), MealTier.Simple, SimTranslation.T("RSR.UI.SimpleFood"));
            DrawFilter(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height), MealTier.Fine, SimTranslation.T("RSR.UI.FineFood"));
            DrawFilter(new Rect(rect.x + (width + gap) * 3f, rect.y, width, rect.height), MealTier.Lavish, SimTranslation.T("RSR.UI.LavishOtherFood"));
        }

        //绘制单个筛选按钮，职责是维护选中态并重置滚动位置。
        private void DrawFilter(Rect rect, MealTier target, string label)
        {
            bool selected = tier == target;
            bool clicked = ShopUiVisualUtility.DrawTabButton(rect, label, selected, RestaurantUiStyle.MutedText);
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
                Widgets.Label(rect.ContractedBy(12f), SimTranslation.T("RSR.UI.NoFoodSelectionMatch"));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }
            float viewWidth = Mathf.Max(1f, rect.width - 16f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, foods.Count * (RowHeight + 5f)));
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            try
            {
                //只绘制可见行，职责是避免大型食品模组列表逐帧绘制全部图标。
                float stride = RowHeight + 5f;
                int first = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / stride));
                int last = Mathf.Min(foods.Count, Mathf.CeilToInt((scrollPosition.y + rect.height) / stride));
                for (int i = first; i < last; i++)
                {
                    DrawFoodRow(new Rect(0f, i * stride, viewWidth, RowHeight), foods[i], i);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        //绘制单个食品行，职责是显示图标和来源并在选择后回写草稿、关闭选择窗口。
        private void DrawFoodRow(Rect rect, ThingDef def, int index)
        {
            if (!RestaurantFoodRow.Draw(rect, def, index, selectingIngredient)) return;
            onSelected?.Invoke(def);
            Close();
        }

        //返回缓存后的筛选结果，职责是避免每帧重新扫描全部 Def。
        private List<ThingDef> GetFilteredFoods()
        {
            string key = (search ?? "").Trim();
            if (filteredCache != null && filteredSearch == key && filteredTier == tier) return filteredCache;
            filteredSearch = key;
            filteredTier = tier;
            filteredCache = candidates
                .Where(def => modFilter.Allows(def) && (selectingIngredient || MatchesTier(def))
                    && RestaurantFoodUtility.MatchesSearch(def, key))
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

        //清理筛选缓存，职责是让搜索、来源或品质变化立即刷新列表并回到顶部。
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
