using RimSimHotelExtension.Services;
using SimManagementLib.Api;
using Verse;

namespace RimSimHotelExtension
{
    //将旅馆访问协议注册到已有经营框架。
    [StaticConstructorOnStartup]
    public static class HotelBootstrap
    {
        //程序集加载后注册一次，不依赖餐厅扩展的类型或安装状态。
        static HotelBootstrap()
        {
            SimShopCustomerApi.RegisterCustomerVisitExtension(new HotelVisitExtension());
        }
    }
}
