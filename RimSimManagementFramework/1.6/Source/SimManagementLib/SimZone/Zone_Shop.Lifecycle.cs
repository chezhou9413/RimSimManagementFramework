using SimManagementLib.SimMapComp;
using Verse;

namespace SimManagementLib.SimZone
{
    //类职责：把商店区划注册和注销生命周期精确同步到地图级顾客协调器。
    public partial class Zone_Shop
    {
        //登记商店区划，职责是创建一份待预算重算的吸引力快照。
        public override void PostRegister()
        {
            base.PostRegister();
            Map?.GetComponent<CustomerArrivalManager>()?.NotifyShopDirty(this);
        }

        //注销商店区划，职责是立即按 ID 删除快照而不重建全图商店列表。
        public override void PostDeregister()
        {
            Map?.GetComponent<CustomerArrivalManager>()?.NotifyShopUnregistered(ID);
            base.PostDeregister();
        }
    }
}
