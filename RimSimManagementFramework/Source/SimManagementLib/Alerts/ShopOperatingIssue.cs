using RimWorld.Planet;
using SimManagementLib.SimZone;

namespace SimManagementLib.Alerts
{
    //保存一项经营问题及其定位目标，短暂缺员等问题允许延迟显示。
    internal sealed class ShopOperatingIssue
    {
        public readonly Zone_Shop Shop;
        public readonly string Key;
        public readonly string Category;
        public readonly string Reason;
        public readonly GlobalTargetInfo Target;
        public readonly bool Delay;

        //绑定店铺、原因和具体位置，供警报说明与点击跳转共用。
        public ShopOperatingIssue(Zone_Shop shop, string key, string reason, GlobalTargetInfo target, bool delay = true, string category = null)
        {
            Shop = shop;
            Key = key;
            Category = category ?? key;
            Reason = reason;
            Target = target;
            Delay = delay;
        }
    }
}
