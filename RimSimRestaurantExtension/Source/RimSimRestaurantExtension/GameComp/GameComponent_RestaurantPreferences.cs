using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.GameComp
{
    //管理顾客餐厅偏好，职责是稳定生成、持久化并更新每名顾客的点菜倾向。
    public class GameComponent_RestaurantPreferences : GameComponent
    {
        private const int MaximumProfiles = 4096;
        private Dictionary<int, RestaurantCustomerPreference> preferencesByPawnId = new Dictionary<int, RestaurantCustomerPreference>();
        private List<int> tmpKeys;
        private List<RestaurantCustomerPreference> tmpValues;

        //构造偏好组件，职责是参与游戏级存档生命周期。
        public GameComponent_RestaurantPreferences(Game game)
        {
        }

        //周期限制偏好档案规模，职责是让长期经营存档不会因一次性访客无限增长。
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now <= 0 || now % 60000 != 0 || preferencesByPawnId == null || preferencesByPawnId.Count <= MaximumProfiles)
                return;
            int removeCount = preferencesByPawnId.Count - MaximumProfiles;
            List<int> oldest = preferencesByPawnId
                .OrderBy(pair => pair.Value?.lastSeenTick ?? 0)
                .Take(removeCount)
                .Select(pair => pair.Key)
                .ToList();
            for (int i = 0; i < oldest.Count; i++)
                preferencesByPawnId.Remove(oldest[i]);
        }

        //读写全部顾客偏好，职责是恢复跨来访的稳定选择记录。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref preferencesByPawnId, "restaurantPreferencesByPawnId", LookMode.Value, LookMode.Deep, ref tmpKeys, ref tmpValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                preferencesByPawnId = preferencesByPawnId ?? new Dictionary<int, RestaurantCustomerPreference>();
                List<int> invalid = preferencesByPawnId.Where(pair => pair.Value == null).Select(pair => pair.Key).ToList();
                for (int i = 0; i < invalid.Count; i++) preferencesByPawnId.Remove(invalid[i]);
            }
        }

        //取得顾客偏好，职责是在首次点菜时用独立随机种子生成稳定档案。
        public RestaurantCustomerPreference GetOrCreate(Pawn customer)
        {
            if (customer == null) return null;
            preferencesByPawnId = preferencesByPawnId ?? new Dictionary<int, RestaurantCustomerPreference>();
            int pawnId = customer.thingIDNumber;
            if (preferencesByPawnId.TryGetValue(pawnId, out RestaurantCustomerPreference preference) && preference != null)
            {
                preference.lastSeenTick = Find.TickManager?.TicksGame ?? preference.lastSeenTick;
                return preference;
            }

            Rand.PushState(unchecked(pawnId * 7919 + 104729));
            try
            {
                float dietRoll = Rand.Value;
                preference = new RestaurantCustomerPreference
                {
                    pawnThingId = pawnId,
                    dietPreference = dietRoll < 0.2f
                        ? RestaurantDietPreference.Vegetarian
                        : dietRoll > 0.8f ? RestaurantDietPreference.Meat : RestaurantDietPreference.Any,
                    frugality = Rand.Range(0.15f, 0.9f),
                    qualityPreference = Rand.Range(0.15f, 0.9f),
                    noveltyPreference = Rand.Range(0.15f, 0.9f),
                    preferredPortions = Rand.Chance(0.22f) ? 2 : 1,
                    lastSeenTick = Find.TickManager?.TicksGame ?? 0
                };
            }
            finally
            {
                Rand.PopState();
            }

            preferencesByPawnId[pawnId] = preference;
            return preference;
        }

        //记录一次成功用餐，职责是更新最近菜品并逐步形成顾客的常点菜单。
        public void RecordCompletedOrder(Pawn customer, RestaurantOrder order)
        {
            RestaurantCustomerPreference preference = GetOrCreate(customer);
            if (preference == null || order == null) return;
            preference.lastMenuItemId = order.menuItemId ?? "";
            preference.completedOrders = Mathf.Max(0, preference.completedOrders) + 1;
            preference.lastSeenTick = Find.TickManager?.TicksGame ?? preference.lastSeenTick;
            if (preference.favoriteMenuItemId.NullOrEmpty() || order.preferenceScore >= 1.15f)
                preference.favoriteMenuItemId = order.menuItemId ?? "";
        }
    }
}
