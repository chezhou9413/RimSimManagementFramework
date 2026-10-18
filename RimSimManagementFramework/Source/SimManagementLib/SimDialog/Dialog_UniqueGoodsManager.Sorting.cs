using RimWorld;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //单件商品排序部分，职责是为来源和上架列表提供独立的排序字段与方向。
    public sealed partial class Dialog_UniqueGoodsManager
    {
        private bool sortDescending;
        private int listedSortMode;
        private bool listedSortDescending;
        private readonly Dictionary<int, Thing> listedThingCache = new Dictionary<int, Thing>();
        private int AvailableSortKey => sortMode + (sortDescending ? 4 : 0);
        private static readonly string[] SortKeys =
        {
            "RSMF.UniqueGoods.Sort.Name", "RSMF.UniqueGoods.Sort.Quality",
            "RSMF.UniqueGoods.Sort.Value", "RSMF.UniqueGoods.Sort.HitPoints"
        };

        //绘制排序字段菜单和方向按钮，职责是按实际文字宽度分配控件空间。
        private void DrawSortControls(Rect rect, bool listed)
        {
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            int mode = listed ? listedSortMode : sortMode;
            bool descending = listed ? listedSortDescending : sortDescending;
            string direction = SimTranslation.T(descending ? "RSMF.UniqueGoods.Sort.Descending" : "RSMF.UniqueGoods.Sort.Ascending");
            float directionWidth = Text.CalcSize(direction).x + 24f;
            Rect fieldRect = new Rect(rect.x, rect.y, rect.width - directionWidth - 6f, rect.height);
            Rect directionRect = new Rect(fieldRect.xMax + 6f, rect.y, directionWidth, rect.height);
            if (SimUiStyle.DrawSecondaryButton(fieldRect, SimTranslation.T(SortKeys[mode])))
            {
                var options = new List<FloatMenuOption>();
                for (int i = 0; i < SortKeys.Length; i++)
                {
                    int selected = i;
                    options.Add(new FloatMenuOption(SimTranslation.T(SortKeys[i]), () => ChangeSort(listed, selected, descending)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (SimUiStyle.DrawSecondaryButton(directionRect, direction))
                ChangeSort(listed, mode, !descending);
        }

        //应用用户主动选择的排序，职责是仅重置对应列表的位置。
        private void ChangeSort(bool listed, int mode, bool descending)
        {
            if (listed)
            {
                listedSortMode = mode;
                listedSortDescending = descending;
                listedCacheVersion = -1;
                listedPage = 0;
                listedScroll = Vector2.zero;
            }
            else
            {
                sortMode = mode;
                sortDescending = descending;
                InvalidateAvailableFilter();
            }
        }

        //按实物属性排序并以固定标识打破平局，职责是保证刷新前后相同键的物品顺序稳定。
        private static IOrderedEnumerable<T> SortItems<T>(IEnumerable<T> items, Func<T, Thing> thingOf,
            Func<T, int> identityOf, int mode, bool descending)
        {
            IOrderedEnumerable<T> sorted;
            if (mode == 0)
            {
                Func<T, string> label = item => thingOf(item)?.LabelCapNoCount ?? "";
                sorted = descending ? items.OrderByDescending(label, StringComparer.CurrentCulture)
                    : items.OrderBy(label, StringComparer.CurrentCulture);
            }
            else
            {
                Func<T, float> key = item => GetSortValue(thingOf(item), mode);
                sorted = descending ? items.OrderByDescending(key) : items.OrderBy(key);
            }
            return sorted.ThenBy(identityOf);
        }

        //读取排序用实物属性，缺少实物或品质的槽位以负值区分。
        private static float GetSortValue(Thing thing, int mode)
        {
            if (thing == null || thing.Destroyed) return -1f;
            if (mode == 1) return thing.TryGetQuality(out QualityCategory quality) ? (int)quality : -1f;
            if (mode == 2) return thing.MarketValue;
            return thing.MaxHitPoints > 0 ? thing.HitPoints / (float)thing.MaxHitPoints : 1f;
        }
    }
}
