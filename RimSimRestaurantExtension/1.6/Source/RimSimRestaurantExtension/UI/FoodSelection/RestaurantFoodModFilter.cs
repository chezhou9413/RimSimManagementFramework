using System;
using System.Collections.Generic;
using System.Linq;
using SimManagementLib.Api;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //管理食品来源侧栏，职责是按实际候选物品分组并提供可搜索的模组筛选。
    internal sealed class RestaurantFoodModFilter
    {
        private readonly List<IGrouping<ModContentPack, ThingDef>> groups;
        private readonly int total;
        private List<int> shown;
        private int selected = -1;
        private string search = "";
        private Vector2 scroll;

        //建立来源索引，职责是仅列出拥有可选食品的原版、扩展包及模组。
        public RestaurantFoodModFilter(List<ThingDef> foods)
        {
            total = foods.Count;
            groups = foods.GroupBy(food => food.modContentPack).OrderBy(group => SourceName(group.Key)).ToList();
            shown = Enumerable.Range(0, groups.Count).ToList();
        }

        //判断食品来源，职责是将模组筛选与食品搜索及品质筛选取交集。
        public bool Allows(ThingDef food)
        {
            return selected < 0 || food.modContentPack == groups[selected].Key;
        }

        //返回来源名称，职责是为缺少来源的动态定义提供明确分类。
        public static string SourceName(ModContentPack source)
        {
            return source?.Name ?? SimTranslation.T("RSR.UI.UnknownFoodSource");
        }

        //绘制来源筛选面板，职责是固定搜索与全部入口并单独滚动模组列表。
        public bool Draw(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                ShopUiVisualUtility.DrawSection(rect);
                Rect inner = rect.ContractedBy(8f);
                float line = RestaurantUiStyle.LineHeight(GameFont.Small);
                float control = RestaurantUiStyle.ControlHeight();
                ShopUiVisualUtility.DrawCellLabel(new Rect(inner.x, inner.y, inner.width, line),
                    SimTranslation.T("RSR.UI.FoodSourceMods"));
                Rect field = new Rect(inner.x, inner.y + line + 6f, inner.width, control);
                string next = ShopUiVisualUtility.DrawSearchField(field, search, SimTranslation.T("RSR.UI.SearchFoodMods"));
                if (next != search)
                {
                    search = next;
                    string key = search.Trim();
                    shown = Enumerable.Range(0, groups.Count).Where(index =>
                        SourceName(groups[index].Key).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                        || (groups[index].Key?.PackageId ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    scroll = Vector2.zero;
                }
                Rect all = new Rect(inner.x, field.yMax + 8f, inner.width, control);
                bool changed = DrawOption(all, -1, SimTranslation.T("RSR.UI.AllFoodMods"), total);
                Rect list = new Rect(inner.x, all.yMax + 6f, inner.width, Mathf.Max(0f, inner.yMax - all.yMax - 6f));
                float stride = control + 4f;
                Rect view = new Rect(0f, 0f, Mathf.Max(1f, list.width - 16f), Mathf.Max(list.height, shown.Count * stride));
                Widgets.BeginScrollView(list, ref scroll, view);
                try
                {
                    int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / stride));
                    int last = Mathf.Min(shown.Count, Mathf.CeilToInt((scroll.y + list.height) / stride));
                    for (int i = first; i < last; i++)
                    {
                        int index = shown[i];
                        changed |= DrawOption(new Rect(0f, i * stride, view.width, control), index,
                            SourceName(groups[index].Key), groups[index].Count());
                    }
                }
                finally { Widgets.EndScrollView(); }
                return changed;
            }
        }

        //绘制来源条目，职责是显示候选数量、完整名称提示及当前选中状态。
        private bool DrawOption(Rect rect, int index, string label, int count)
        {
            string text = label + " (" + count + ")";
            TooltipHandler.TipRegion(rect, text);
            if (!ShopUiVisualUtility.DrawTabButton(rect, text, selected == index, RestaurantUiStyle.MutedText)) return false;
            selected = index;
            return true;
        }
    }
}
