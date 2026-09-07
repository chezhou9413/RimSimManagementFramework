using System.Collections.Generic;
using System.Linq;
using SimManagementLib.SimMapComp;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingClass
{
    //货柜业务策略接口，职责是允许扩展限定销售用途、补货来源和实际交互位置。
    public partial class Building_SimContainer
    {
        public virtual bool AllowsCustomerSelfPurchase => true;
        public virtual string RestockSourceIssue => "";
        public virtual LocalTargetInfo InventoryInteractionTarget => this;
        public virtual PathEndMode InventoryInteractionEndMode => PathEndMode.Touch;

        //检查货源是否属于本柜允许的补货范围。
        public virtual bool AllowsRestockSource(Thing source) => true;

        //检查物品是否属于本柜允许存储的范围。
        public virtual bool AllowsInventoryItem(ThingDef item) => GoodsComp?.AllowsThingDef(item) == true;

        //打开当前货柜的专用管理窗口，职责是让框架页面尊重扩展窗口策略。
        public void OpenInventoryManagement()
        {
            Window window = CreateManagementWindow();
            if (window != null) Find.WindowStack.Add(window);
        }

        //检查既有下架任务，职责是让业务预留避让正在搬出的品种。
        public bool HasPendingInventoryWithdrawal(ThingDef item) => pendingOut.TryGetValue(item, out int count) && count > 0;

        //返回指定品种的真实预留数量。
        public int CountReserved(ThingDef item)
        {
            var ledger = MapHeld?.GetComponent<MapComponent_InventoryReservations>();
            return virtualStorage?.Where(t => t.def == item).Sum(t => ledger?.Reserved(t) ?? 0) ?? 0;
        }

        //按业务标识查询本柜预留的真实库存。
        public List<ThingCount> GetReservedInventory(string key)
        {
            return MapHeld.GetComponent<MapComponent_InventoryReservations>().Query(key)
                .Where(t => t.Thing?.ParentHolder == this).ToList();
        }

        //原子预留当前货柜中的指定实物。
        public bool ReserveInventory(string key, IList<ThingCount> items)
        {
            return items.All(t => t.Thing?.ParentHolder == this)
                && MapHeld.GetComponent<MapComponent_InventoryReservations>().Reserve(key, items);
        }

        //提取本柜已预留实物，职责是同步真实库存和补货需求。
        public Thing ExtractReservedInventory(string key, Thing thing, int count, ThingOwner destination)
        {
            if (thing?.ParentHolder != this) return null;
            return MapHeld.GetComponent<MapComponent_InventoryReservations>().Extract(key, thing, count, destination);
        }

        //释放业务占用，职责是让终止业务的库存重新参与分配。
        public void ReleaseInventory(string key)
        {
            MapHeld?.GetComponent<MapComponent_InventoryReservations>()?.Release(key);
        }

        //通知库存实物发生变化，职责是更新容量缓存、外观和补货队列。
        public void NotifyInventoryChanged()
        {
            MarkStoredCountCacheDirty();
            RefreshProgressStageGraphic();
            MarkRestockQueueDirty(null, "真实库存变化");
        }

        //响应容器加入实物，职责是同步容量缓存。
        public void Notify_ItemAdded(Thing item) => NotifyInventoryChanged();

        //响应容器移除或腐坏销毁实物，职责是同步容量缓存。
        public void Notify_ItemRemoved(Thing item) => NotifyInventoryChanged();
    }
}
