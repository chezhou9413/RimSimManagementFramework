using RimSimRestaurantExtension.Services;
using SimManagementLib.Api;
using Verse;

namespace RimSimRestaurantExtension
{
    //初始化餐厅扩展程序集，负责把餐厅逻辑注册到模拟经营框架公开接口。
    [StaticConstructorOnStartup]
    public static class MainEntry
    {
        //初始化餐厅图标与冷藏逻辑，并将结账门禁和评价快照注册到框架生命周期。
        static MainEntry()
        {
            UI.RestaurantBarIcon.Initialize();
            Buildings.Rendering.RestaurantBarConstructionNotifications.Install();
            Conveyor.Placement.ConveyorConstructionHooks.Install();
            Inventory.RestaurantRefrigeration.Install();
            SimShopCheckoutApi.RegisterCheckoutWorker(new RestaurantCheckoutWorker());
            SimShopReviewApi.RegisterSnapshotWorker(new RestaurantReviewSnapshotWorker());
        }
    }
}
