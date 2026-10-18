using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //统一专业货柜入库与通知顺序，保持真实库存和槽位同步。
    public partial class Building_UniqueGoodsContainer
    {
        private bool changingUniqueInventory;

        //通用库存入口同样遵守专业货柜的商品分类限制。
        public override bool AllowsInventoryItem(ThingDef item) => UniqueGoodsUtility.IsEligibleDef(item, this);

        //退回商品也必须逐件占用兼容空槽，并检查实物品质、尸衣和生物编码限制。
        public override int TryReceiveReturnedThing(Thing thing) => TryStoreDirectly(thing) ? 1 : 0;

        //转移实物时延后刷新，避免容器回调把尚未登记的商品分配到第二个槽位。
        public override void NotifyInventoryChanged()
        {
            if (!changingUniqueInventory)
                base.NotifyInventoryChanged();
        }

        //把一件实物和指定槽位一起提交，成功后再通知库存和补货队列。
        private bool TryStoreThingInSlot(UniqueGoodsSlotData slot, Thing source)
        {
            if (!IsEligibleUniqueThingForSlot(source, slot.index) || VirtualStorage.Contains(source))
                return false;

            Map sourceMap = source.Map;
            IntVec3 sourcePosition = source.Position;
            changingUniqueInventory = true;
            try
            {
                Thing stored;
                if (source.holdingOwner != null)
                {
                    if (source.holdingOwner.TryTransferToContainer(source, VirtualStorage, 1, out stored, false) != 1)
                    {
                        Log.Error($"专业货柜入库失败：{source.LabelCapNoCount} 无法转入 {LabelCap}。");
                        return false;
                    }
                }
                else
                {
                    stored = source.SplitOff(1);
                    if (!VirtualStorage.TryAdd(stored, false))
                    {
                        if (stored != source)
                        {
                            if (!source.TryAbsorbStack(stored, false))
                                Log.Error($"专业货柜入库回滚失败：{stored.LabelCapNoCount} 无法合回来源物品。");
                        }
                        else if (sourceMap != null && !GenPlace.TryPlaceThing(stored, sourcePosition, sourceMap, ThingPlaceMode.Near))
                            Log.Error($"专业货柜入库回滚失败：{stored.LabelCapNoCount} 无法放回原位置。");
                        Log.Error($"专业货柜入库失败：{source.LabelCapNoCount} 未进入 {LabelCap} 的虚拟库存。");
                        return false;
                    }
                }

                slot.storedThingId = stored.thingIDNumber;
                slot.pendingSourceThingId = -1;
                slot.pendingSourceLabel = "";
                slot.reservedCustomerId = -1;
                slot.price = Mathf.Max(1f, slot.price > 0f ? slot.price : stored.MarketValue);
            }
            finally
            {
                changingUniqueInventory = false;
            }
            NotifyUniqueSlotsChanged("专业商品入库完成");
            return true;
        }
    }
}
