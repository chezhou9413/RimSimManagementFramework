using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Models;
using Verse;

namespace RimSimRestaurantExtension.GameComp
{
    //保存单店餐厅扩展设置，负责让框架店铺 UI 读写餐厅行为参数。
    public class RestaurantShopSettings : IExposable
    {
        public int shopZoneId = -1;
        public bool enabled = true;
        public float priceMultiplier = 1f;
        public int maxWaitTicks = 12000;
        public int maxServiceWaitTicks = 12000;
        public int maxOrderRounds = 3;
        public int reorderIntervalTicks = 1200;
        public List<int> pantryZoneIds = new List<int>();
        public bool useCustomerPreferences = true;
        public float preferenceStrength = 1f;
        public List<RestaurantMenuItem> menuItems = new List<RestaurantMenuItem>();

        //读写单店设置，并在读档后夹紧范围。
        public void ExposeData()
        {
            Scribe_Values.Look(ref shopZoneId, "shopZoneId", -1);
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref priceMultiplier, "priceMultiplier", 1f);
            Scribe_Values.Look(ref maxWaitTicks, "maxWaitTicks", 12000);
            Scribe_Values.Look(ref maxServiceWaitTicks, "maxServiceWaitTicks", 12000);
            Scribe_Values.Look(ref maxOrderRounds, "maxOrderRounds", 3);
            Scribe_Values.Look(ref reorderIntervalTicks, "reorderIntervalTicks", 1200);
            Scribe_Collections.Look(ref pantryZoneIds, "pantryZoneIds", LookMode.Value);
            Scribe_Values.Look(ref useCustomerPreferences, "useCustomerPreferences", true);
            Scribe_Values.Look(ref preferenceStrength, "preferenceStrength", 1f);
            Scribe_Collections.Look(ref menuItems, "menuItems", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Normalize();
        }

        //确保菜单和隐藏设置处于可用范围，负责保留用户主动清空菜单的经营状态。
        public void Normalize()
        {
            maxOrderRounds = UnityEngine.Mathf.Clamp(maxOrderRounds, 1, 5);
            reorderIntervalTicks = UnityEngine.Mathf.Clamp(reorderIntervalTicks, 120, 12000);
            priceMultiplier = UnityEngine.Mathf.Clamp(priceMultiplier, 0.1f, 5f);
            maxWaitTicks = UnityEngine.Mathf.Clamp(maxWaitTicks, 6000, 30000);
            maxServiceWaitTicks = UnityEngine.Mathf.Clamp(maxServiceWaitTicks, 6000, 30000);
            preferenceStrength = UnityEngine.Mathf.Clamp(preferenceStrength, 0f, 2f);
            if (menuItems == null) menuItems = new List<RestaurantMenuItem>();
            menuItems.RemoveAll(item => item == null);
            for (int i = 0; i < menuItems.Count; i++)
                menuItems[i]?.Normalize();
            HashSet<string> usedIds = new HashSet<string>();
            for (int i = 0; i < menuItems.Count; i++)
            {
                RestaurantMenuItem item = menuItems[i];
                if (item == null) continue;
                if (item.id.NullOrEmpty() || !usedIds.Add(item.id))
                {
                    item.id = MakeMenuId();
                    usedIds.Add(item.id);
                }
            }
        }

        //重置为教程示例菜单，负责给新店和测试店提供可直接运行的菜品配置。
        public void ResetDefaultMenu()
        {
            menuItems = CreateDefaultMenu();
        }

        //追加一套教程示例菜单，负责让用户快速恢复被删掉的默认菜品。
        public void AddDefaultMenuItems()
        {
            if (menuItems == null) menuItems = new List<RestaurantMenuItem>();
            List<RestaurantMenuItem> defaults = CreateDefaultMenu();
            for (int i = 0; i < defaults.Count; i++)
            {
                RestaurantMenuItem item = defaults[i];
                item.id = MakeMenuId();
                menuItems.Add(item);
            }
            Normalize();
        }

        //创建餐厅设置深副本，职责是让框架统一保存流程编辑独立草稿而不即时污染存档数据。
        public RestaurantShopSettings Clone()
        {
            RestaurantShopSettings clone = new RestaurantShopSettings
            {
                shopZoneId = shopZoneId,
                enabled = enabled,
                priceMultiplier = priceMultiplier,
                maxWaitTicks = maxWaitTicks,
                maxServiceWaitTicks = maxServiceWaitTicks,
                maxOrderRounds = maxOrderRounds,
                reorderIntervalTicks = reorderIntervalTicks,
                pantryZoneIds = new List<int>(pantryZoneIds),
                useCustomerPreferences = useCustomerPreferences,
                preferenceStrength = preferenceStrength,
                menuItems = menuItems?
                    .Where(item => item != null)
                    .Select(item => item.Clone())
                    .ToList() ?? new List<RestaurantMenuItem>()
            };
            clone.Normalize();
            return clone;
        }

        //应用餐厅设置草稿，职责是以深复制方式提交运行参数和菜单而不共享可变集合。
        public void CopyFrom(RestaurantShopSettings source)
        {
            if (source == null) return;
            shopZoneId = source.shopZoneId;
            enabled = source.enabled;
            priceMultiplier = source.priceMultiplier;
            maxWaitTicks = source.maxWaitTicks;
            maxServiceWaitTicks = source.maxServiceWaitTicks;
            maxOrderRounds = source.maxOrderRounds;
            reorderIntervalTicks = source.reorderIntervalTicks;
            pantryZoneIds = new List<int>(source.pantryZoneIds);
            useCustomerPreferences = source.useCustomerPreferences;
            preferenceStrength = source.preferenceStrength;
            menuItems = source.menuItems?
                .Where(item => item != null)
                .Select(item => item.Clone())
                .ToList() ?? new List<RestaurantMenuItem>();
            Normalize();
        }

        //创建默认菜单模板，负责集中维护新餐厅和手动重置时使用的示例菜品。
        public static List<RestaurantMenuItem> CreateDefaultMenu()
        {
            return new List<RestaurantMenuItem>
            {
                MakeMenuItem("rsr_simple_rice", "家常米饭套餐", "MealSimple", 35f, 1, 2,
                    ("RawRice", 6), ("RawPotatoes", 4)),
                MakeMenuItem("rsr_fine_veg", "精致蔬菜套餐", "MealFine_Veg", 65f, 1, 3,
                    ("RawCorn", 9), ("RawBerries", 6)),
                MakeMenuItem("rsr_lavish_veg", "豪华餐厅套餐", "MealLavish_Veg", 110f, 1, 2,
                    ("RawRice", 9), ("RawCorn", 9), ("RawBerries", 7))
            };
        }

        //构造单个菜单模板，负责减少默认菜单的重复字段。
        private static RestaurantMenuItem MakeMenuItem(string id, string label, string mealDefName, float price, int minCount, int maxCount, params (string defName, int count)[] ingredients)
        {
            RestaurantMenuItem item = new RestaurantMenuItem
            {
                id = id,
                label = label,
                mealDefName = mealDefName,
                enabled = true,
                unitPrice = price,
                minCount = minCount,
                maxCount = maxCount,
                ingredients = new List<RestaurantIngredientRequirement>()
            };
            for (int i = 0; i < ingredients.Length; i++)
            {
                item.ingredients.Add(new RestaurantIngredientRequirement
                {
                    thingDefName = ingredients[i].defName,
                    countPerMeal = ingredients[i].count
                });
            }
            item.Normalize();
            return item;
        }

        //生成不会依赖游戏随机状态的菜单编号，职责是避免复制和追加菜单时发生缓存键冲突。
        public static string MakeMenuId()
        {
            return "menu_" + Guid.NewGuid().ToString("N");
        }
    }

    //保存餐厅扩展的地图级设置，负责避免修改模拟经营框架本体存档字段。
    public class GameComponent_RestaurantSettings : GameComponent
    {
        private Dictionary<int, RestaurantShopSettings> settingsByShopId = new Dictionary<int, RestaurantShopSettings>();
        private List<int> tmpKeys;
        private List<RestaurantShopSettings> tmpValues;

        //构造餐厅设置组件，职责是参与游戏级存档生命周期。
        public GameComponent_RestaurantSettings(Game game)
        {
        }

        //读写所有店铺设置。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref settingsByShopId, "restaurantSettingsByShopId", LookMode.Value, LookMode.Deep, ref tmpKeys, ref tmpValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && settingsByShopId == null)
                settingsByShopId = new Dictionary<int, RestaurantShopSettings>();
        }

        //获取指定店铺设置，缺失时创建默认设置。
        public RestaurantShopSettings GetOrCreate(int shopZoneId)
        {
            if (shopZoneId < 0) return null;
            if (settingsByShopId == null)
                settingsByShopId = new Dictionary<int, RestaurantShopSettings>();
            if (!settingsByShopId.TryGetValue(shopZoneId, out RestaurantShopSettings settings) || settings == null)
            {
                settings = new RestaurantShopSettings { shopZoneId = shopZoneId };
                settings.ResetDefaultMenu();
                settingsByShopId[shopZoneId] = settings;
            }
            settings.Normalize();
            return settings;
        }
    }
}
