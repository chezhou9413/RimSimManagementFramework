using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Models
{
    //描述顾客的饮食倾向，职责是给菜单评分提供肉食、素食和中性方向。
    public enum RestaurantDietPreference
    {
        Any,
        Vegetarian,
        Meat
    }

    //保存顾客的稳定餐厅偏好，职责是让同一顾客在多次来访时保持价格、品质、份量与尝鲜倾向。
    public class RestaurantCustomerPreference : IExposable
    {
        public int pawnThingId = -1;
        public RestaurantDietPreference dietPreference;
        public float frugality = 0.5f;
        public float qualityPreference = 0.5f;
        public float noveltyPreference = 0.5f;
        public int preferredPortions = 1;
        public string favoriteMenuItemId = "";
        public string lastMenuItemId = "";
        public int completedOrders;
        public int lastSeenTick;

        //读写偏好数据，职责是保证评分参数始终处于可解释范围。
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnThingId, "pawnThingId", -1);
            Scribe_Values.Look(ref dietPreference, "dietPreference", RestaurantDietPreference.Any);
            Scribe_Values.Look(ref frugality, "frugality", 0.5f);
            Scribe_Values.Look(ref qualityPreference, "qualityPreference", 0.5f);
            Scribe_Values.Look(ref noveltyPreference, "noveltyPreference", 0.5f);
            Scribe_Values.Look(ref preferredPortions, "preferredPortions", 1);
            Scribe_Values.Look(ref favoriteMenuItemId, "favoriteMenuItemId", "");
            Scribe_Values.Look(ref lastMenuItemId, "lastMenuItemId", "");
            Scribe_Values.Look(ref completedOrders, "completedOrders", 0);
            Scribe_Values.Look(ref lastSeenTick, "lastSeenTick", 0);

            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            frugality = Mathf.Clamp01(frugality);
            qualityPreference = Mathf.Clamp01(qualityPreference);
            noveltyPreference = Mathf.Clamp01(noveltyPreference);
            preferredPortions = Mathf.Clamp(preferredPortions, 1, 3);
            favoriteMenuItemId = favoriteMenuItemId ?? "";
            lastMenuItemId = lastMenuItemId ?? "";
            completedOrders = Mathf.Max(0, completedOrders);
            lastSeenTick = Mathf.Max(0, lastSeenTick);
        }

        //返回面向界面和评价的偏好摘要，职责是把内部评分参数转换为当前语言的简短描述。
        public string BuildSummary()
        {
            string diet = dietPreference == RestaurantDietPreference.Vegetarian
                ? SimTranslation.T("RSR.Preference.Vegetarian")
                : dietPreference == RestaurantDietPreference.Meat ? SimTranslation.T("RSR.Preference.Meat") : SimTranslation.T("RSR.Preference.AnyDiet");
            string budget = frugality >= 0.7f ? SimTranslation.T("RSR.Preference.Frugal") : frugality <= 0.3f ? SimTranslation.T("RSR.Preference.Generous") : SimTranslation.T("RSR.Preference.ModerateBudget");
            string quality = qualityPreference >= 0.7f ? SimTranslation.T("RSR.Preference.HighQuality") : qualityPreference <= 0.3f ? SimTranslation.T("RSR.Preference.SimpleQuality") : SimTranslation.T("RSR.Preference.BalancedQuality");
            string novelty = noveltyPreference >= 0.65f ? SimTranslation.T("RSR.Preference.Novelty") : noveltyPreference <= 0.35f ? SimTranslation.T("RSR.Preference.Familiar") : SimTranslation.T("RSR.Preference.OccasionalNovelty");
            return SimTranslation.T("RSR.Preference.Summary", (diet).Named("diet"), (budget).Named("budget"), (quality).Named("quality"), (novelty).Named("novelty"), (preferredPortions).Named("portions"));
        }
    }
}
