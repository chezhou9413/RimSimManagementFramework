using System.Collections.Generic;
using SimManagementLib.SimThingComp;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingClass
{
    //经营和补货共用的货柜入口，工作目标始终是地图上的宿主建筑。
    public interface IShopStorage
    {
        Building Host { get; }
        IEnumerable<ThingDef> ActiveDefs { get; }
        bool AllowsCustomerSelfPurchase { get; }
        int StoredCountVersion { get; }
        LocalTargetInfo InventoryInteractionTarget { get; }
        PathEndMode InventoryInteractionEndMode { get; }
        string RestockSourceIssue { get; }
        int CountStored(ThingDef item);
        int GetTargetCount(ThingDef item);
        int GetRestockThreshold(ThingDef item);
        bool AllowsRestockSource(Thing source);
        int Deposit(Pawn pawn, ThingDef item, int count);
        void OpenInventoryManagement();
    }

    //把建筑解析为框架货柜或宿主提供的销售组件。
    public static class ShopStorage
    {
        //原有货柜优先使用自身实现，保证一个建筑只登记一次。
        public static IShopStorage For(Thing thing) =>
            thing is Building_SimContainer native ? native : (thing as Building)?.GetComp<ThingComp_StorageSales>();

        //判断建筑能否作为营业设施，覆盖区修复时也可调用。
        public static bool IsFacility(Thing thing) => thing is Building_SimContainer
            || For(thing) is ThingComp_StorageSales sales && sales.Host.Faction == RimWorld.Faction.OfPlayer && sales.SalesEnabled && sales.HasInventory;

        //两类货柜都扣除已预留的商品，原版货架还避让搬运任务。
        internal static int SaleCount(this IShopStorage storage, ThingDef def)
        {
            if (storage is ThingComp_StorageSales sales) return sales.SaleCount(def);
            var native = (Building_SimContainer)storage;
            return native.HasPendingInventoryWithdrawal(def) ? 0 : System.Math.Max(0, native.CountStored(def) - native.CountReserved(def));
        }
    }
}
