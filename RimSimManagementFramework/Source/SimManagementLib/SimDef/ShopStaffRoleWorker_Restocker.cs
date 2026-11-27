using System.Linq;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;

namespace SimManagementLib.SimDef
{
    //有启售储物架的商店也可在员工管理中分配补货员。
    public sealed class ShopStaffRoleWorker_Restocker : ShopStaffRoleWorker
    {
        //使用商店已有的货柜快照，并沿用岗位定义中的其他设施条件。
        public override bool CanShow(Zone_Shop zone) => zone?.Map != null
            && (zone.RetailStorages(false).Any(storage => storage is ThingComp_StorageSales) || base.CanShow(zone));
    }
}
