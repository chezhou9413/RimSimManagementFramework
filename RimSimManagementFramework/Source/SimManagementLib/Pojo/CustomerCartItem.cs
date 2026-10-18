using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Pojo
{
    //顾客购物车条目，职责是保存普通数量商品或托管一件真实专业商品。
    public class CustomerCartItem : IExposable, IThingHolder
    {
        public ThingDef def;
        public int count;
        public int sourceContainerId = -1;
        public int sourceSlotIndex = -1;
        public float unitPrice;
        public float marketValue;
        public int deliveredThingId = -1;
        private ThingOwner<Thing> exactContents;

        public IThingHolder ParentHolder => null;
        public Thing ExactThing => exactContents != null && exactContents.Count > 0 ? exactContents[0] : null;
        public bool HasExactThing => ExactThing != null;

        //将真实商品转入购物车托管。
        public bool StoreExactThing(Thing thing)
        {
            if (thing == null || HasExactThing) return false;
            if (exactContents == null) exactContents = new ThingOwner<Thing>(this, true);
            bool stored = exactContents.TryAddOrTransfer(thing, false);
            if (stored)
            {
                def = thing.def;
                count = 1;
            }
            return stored;
        }

        //从购物车取出真实商品用于交付或退回。
        public Thing TakeExactThing()
        {
            Thing thing = ExactThing;
            return thing == null ? null : exactContents.Take(thing);
        }

        //返回购物车条目的直接持有容器。
        public ThingOwner GetDirectlyHeldThings()
        {
            if (exactContents == null) exactContents = new ThingOwner<Thing>(this, true);
            return exactContents;
        }

        //收集真实商品的子持有容器。
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        //保存或读取购物车条目。
        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref count, "count", 0);
            Scribe_Values.Look(ref sourceContainerId, "sourceContainerId", -1);
            Scribe_Values.Look(ref sourceSlotIndex, "sourceSlotIndex", -1);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref marketValue, "marketValue", 0f);
            Scribe_Values.Look(ref deliveredThingId, "deliveredThingId", -1);
            Scribe_Deep.Look(ref exactContents, "exactContents", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && exactContents == null)
                exactContents = new ThingOwner<Thing>(this, true);
        }
    }
}
