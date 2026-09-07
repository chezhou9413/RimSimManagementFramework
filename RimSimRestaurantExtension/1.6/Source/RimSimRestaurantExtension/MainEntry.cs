using RimSimRestaurantExtension.Services;
using SimManagementLib.Api;
using Verse;

namespace RimSimRestaurantExtension
{
    //初始化餐厅扩展程序集，负责把餐厅逻辑注册到模拟经营框架公开接口。
    [StaticConstructorOnStartup]
    public static class MainEntry
    {
        //注册餐厅结账门禁和评价快照，职责是把长期用餐流程接入框架结账与评价生命周期。
        static MainEntry()
        {
            Inventory.RestaurantRefrigeration.Install();
            SimShopCheckoutApi.RegisterCheckoutWorker(new RestaurantCheckoutWorker());
            SimShopReviewApi.RegisterSnapshotWorker(new RestaurantReviewSnapshotWorker());
        }
    }
}
