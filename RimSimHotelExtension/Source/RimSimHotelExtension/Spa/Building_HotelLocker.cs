using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Tool;
using RimWorld;
using Verse;

namespace RimSimHotelExtension.Spa
{
    //以真实容器保管一名顾客的衣物，任何转移都保留原物品实例。
    public sealed class Building_HotelLocker : Building, IThingHolder
    {
        private ThingOwner<Apparel> contents;
        private Pawn owner;
        private int sessionId;
        public Pawn Occupant => owner;
        public int SessionId => sessionId;
        public bool Available => sessionId == 0 && contents.Count == 0;
        public int ClothingCount => contents.Count;

        //在原版创建建筑实例时准备可存档的物品容器。
        public Building_HotelLocker()
        {
            contents = new ThingOwner<Apparel>(this);
        }

        //锁定一名顾客的柜位，预约阶段尚不转移衣物。
        public bool TryClaim(Pawn customer, int id)
        {
            if (!Spawned || customer == null || (!Available && sessionId != id)) return false;
            owner = customer;
            sessionId = id;
            return true;
        }

        //将可以脱下的衣物移入容器，锁定装备继续穿着。
        public bool StoreClothes(Pawn customer, int id)
        {
            if (customer != owner || id != sessionId || customer.apparel == null) return false;
            foreach (Apparel apparel in customer.apparel.UnlockedApparel.ToList())
            {
                if (!contents.TryAddOrTransfer(apparel, false))
                {
                    Log.Error("[RimSimHotel] 衣柜无法接收衣物: " + apparel);
                    return false;
                }
            }
            return true;
        }

        //归还原衣物，身体变化导致无法穿着的物品放入原顾客背包。
        public void ReturnClothes(Pawn customer, int id)
        {
            if (id != sessionId || customer != owner) return;
            foreach (Apparel apparel in contents.InnerListForReading.ToList())
            {
                bool canWear = customer?.apparel != null && !customer.Dead && !customer.Destroyed
                    && ApparelUtility.HasPartsToWear(customer, apparel.def)
                    && apparel.PawnCanWear(customer, ignoreGender: true)
                    && customer.apparel.CanWearWithoutDroppingAnything(apparel.def);
                if (canWear)
                {
                    contents.Remove(apparel);
                    customer.apparel.Wear(apparel, false);
                    if (customer.apparel.Wearing(apparel)) continue;
                    contents.TryAdd(apparel, false);
                }
                if (customer?.inventory != null && !customer.Destroyed
                    && contents.TryTransferToContainer(apparel, customer.inventory.innerContainer, false)) continue;
                if (Spawned) contents.TryDrop(apparel, Position, Map, ThingPlaceMode.Near, out _, playDropSound: false);
            }
            ReleaseClaim(id);
        }

        //只有实际物品已经归还时才重新开放柜位。
        public void ReleaseClaim(int id)
        {
            if (id != sessionId || contents.Count != 0) return;
            owner = null;
            sessionId = 0;
        }

        //保存原衣物及归属，避免把衣物快照重新生成成重复物品。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contents, "spaClothes", this);
            Scribe_References.Look(ref owner, "spaCustomer");
            Scribe_Values.Look(ref sessionId, "spaSessionId");
        }

        //建筑被移走前把实际内容释放到地图，再终止关联预约。
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (contents.Count > 0 && !contents.TryDropAll(Position, Map, ThingPlaceMode.Near, playDropSound: false))
                Log.Error("[RimSimHotel] 衣柜拆除时部分衣物未能放置: " + this);
            SpaMapComponent.For(Map)?.NotifyBuildingRemoved(this);
            owner = null;
            sessionId = 0;
            base.DeSpawn(mode);
        }

        //让原版保存系统跟踪容器中的物品。
        public ThingOwner GetDirectlyHeldThings() => contents;

        //枚举衣物内部可能存在的其他容器。
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents);
        }

        //显示柜位归属和真实衣物数量。
        public override string GetInspectString()
        {
            string info = base.GetInspectString();
            if (!info.NullOrEmpty()) info += "\n";
            return info + (owner == null ? HotelText.T("RSH.Text.FreeLocker")
                : owner.LabelShortCap + ": " + contents.Count + HotelText.T("RSH.Text.ApparelItems"));
        }
    }
}
