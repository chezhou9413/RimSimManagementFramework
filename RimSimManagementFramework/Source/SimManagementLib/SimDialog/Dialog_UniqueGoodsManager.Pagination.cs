using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //单件商品窗口分页缓存部分，职责是限制每帧筛选和绘制规模。
    public sealed partial class Dialog_UniqueGoodsManager
    {
        private const int AvailablePageSize = 30;
        private const int ListedPageSize = 30;
        private readonly List<Thing> filteredAvailableCache = new List<Thing>();
        private readonly List<UniqueGoodsSlotData> listedSlotsCache = new List<UniqueGoodsSlotData>();
        private readonly Dictionary<int, string> thingDetailsCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> thingTooltipCache = new Dictionary<int, string>();
        private int availablePage;
        private int listedPage;
        private int availableSourceVersion;
        private int filteredSourceVersion = -1;
        private int filteredSortMode = -1;
        private string filteredSearch = "";
        private string committedSearch = "";
        private float searchChangedRealtime = float.NegativeInfinity;
        private int listedCacheVersion = -1;

        //记录搜索输入变化并延迟提交筛选，职责是避免连续输入时每个按键都扫描全部物品。
        private void NotifySearchChanged()
        {
            searchChangedRealtime = Time.realtimeSinceStartup;
        }

        //使筛选缓存失效，职责是只在用户主动改变筛选或排序时重置浏览位置。
        private void InvalidateAvailableFilter(bool resetPosition = true)
        {
            filteredSourceVersion = -1;
            if (!resetPosition) return;
            availablePage = 0;
            availableScroll = Vector2.zero;
        }

        //返回当前搜索和排序对应的缓存结果。
        private List<Thing> GetFilteredAvailableCached()
        {
            if (search != committedSearch && Time.realtimeSinceStartup - searchChangedRealtime >= 0.2f)
            {
                committedSearch = search;
                filteredSourceVersion = -1;
                availablePage = 0;
                availableScroll = Vector2.zero;
            }
            if (filteredSourceVersion == availableSourceVersion && filteredSortMode == AvailableSortKey && filteredSearch == committedSearch)
                return filteredAvailableCache;

            IEnumerable<Thing> query = available;
            if (!committedSearch.NullOrEmpty())
            {
                query = query.Where(thing => (thing.LabelCapNoCount + " " + GetThingDetailsCached(thing))
                    .IndexOf(committedSearch, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
            query = SortItems(query, thing => thing, thing => thing.thingIDNumber, sortMode, sortDescending);
            filteredAvailableCache.Clear();
            filteredAvailableCache.AddRange(query);
            filteredSourceVersion = availableSourceVersion;
            filteredSortMode = AvailableSortKey;
            filteredSearch = committedSearch;
            return filteredAvailableCache;
        }

        //返回按库存版本缓存的已占用槽位列表。
        private List<UniqueGoodsSlotData> GetListedSlotsCached()
        {
            if (listedCacheVersion == container.StoredCountVersion) return listedSlotsCache;
            listedSlotsCache.Clear();
            listedThingCache.Clear();
            IReadOnlyList<UniqueGoodsSlotData> slots = container.UniqueSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                UniqueGoodsSlotData slot = slots[i];
                if (slot == null || !slot.IsOccupied) continue;
                listedSlotsCache.Add(slot);
                listedThingCache[slot.index] = container.GetStoredThing(slot)
                    ?? UniqueGoodsUtility.FindSource(container.Map, slot.pendingSourceThingId, container);
            }
            List<UniqueGoodsSlotData> sorted = SortItems(listedSlotsCache, slot => listedThingCache[slot.index],
                slot => slot.index, listedSortMode, listedSortDescending).ToList();
            listedSlotsCache.Clear();
            listedSlotsCache.AddRange(sorted);
            listedCacheVersion = container.StoredCountVersion;
            ClampPage(ref listedPage, listedSlotsCache.Count, ListedPageSize);
            return listedSlotsCache;
        }

        //返回真实物品摘要缓存。
        private string GetThingDetailsCached(Thing thing)
        {
            if (thing == null) return "";
            if (!thingDetailsCache.TryGetValue(thing.thingIDNumber, out string details))
            {
                details = UniqueGoodsUtility.BuildDetails(thing);
                thingDetailsCache[thing.thingIDNumber] = details;
            }
            return details;
        }

        //返回按物品编号缓存的详细提示，职责是避免可见行每帧重复构造完整描述。
        private string GetThingTooltipCached(Thing thing)
        {
            if (thing == null) return "";
            if (!thingTooltipCache.TryGetValue(thing.thingIDNumber, out string tooltip))
            {
                tooltip = thing.DescriptionDetailed;
                thingTooltipCache[thing.thingIDNumber] = tooltip;
            }
            return tooltip;
        }

        //限制当前页码落在有效范围内。
        private static void ClampPage(ref int page, int totalCount, int pageSize)
        {
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)Mathf.Max(1, pageSize)));
            page = Mathf.Clamp(page, 0, pageCount - 1);
        }

        //计算滚动区当前真正可见的行范围。
        private static void GetVisibleRowRange(float scrollY, float height, float rowHeight, int rowCount, out int first, out int last)
        {
            first = Mathf.Clamp(Mathf.FloorToInt(scrollY / rowHeight) - 1, 0, rowCount);
            last = Mathf.Clamp(Mathf.CeilToInt((scrollY + height) / rowHeight) + 1, first, rowCount);
        }

        //绘制分页控制并在换页时重置对应滚动位置。
        private static void DrawPager(Rect rect, ref int page, int totalCount, int pageSize, ref Vector2 scroll)
        {
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)Mathf.Max(1, pageSize)));
            ClampPage(ref page, totalCount, pageSize);
            float buttonWidth = Mathf.Max(36f, Text.LineHeightOf(GameFont.Small) + 16f);
            Rect previousRect = new Rect(rect.x, rect.y + 2f, buttonWidth, rect.height - 4f);
            Rect nextRect = new Rect(rect.xMax - buttonWidth, rect.y + 2f, buttonWidth, rect.height - 4f);
            if (SimUiStyle.DrawSecondaryButton(previousRect, "‹", page > 0) && page > 0)
            {
                page--;
                scroll = Vector2.zero;
            }
            if (SimUiStyle.DrawSecondaryButton(nextRect, "›", page < pageCount - 1) && page < pageCount - 1)
            {
                page++;
                scroll = Vector2.zero;
            }
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(new Rect(previousRect.xMax + 4f, rect.y, nextRect.x - previousRect.xMax - 8f, rect.height), $"{page + 1}/{pageCount}  ·  {totalCount}");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
