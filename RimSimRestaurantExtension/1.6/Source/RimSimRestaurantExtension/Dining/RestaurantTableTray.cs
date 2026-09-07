using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Tool;
using UnityEngine;
using Verse;
namespace RimSimRestaurantExtension.Dining
{
    //承载单个餐位的桌面实物，职责是保存、绘制和更新独立商品而不加入地面取物。
    public sealed class RestaurantTableTray : Thing, IThingHolder
    {
        private ThingOwner<Thing> contents;
        public int sessionId;
        public Vector3 displayOffset;

        //初始化独立物品容器，职责是禁止不同订单自动合堆。
        public RestaurantTableTray() { contents = new ThingOwner<Thing>(this, false); }

        //返回桌面餐品的原版持有容器。
        public ThingOwner GetDirectlyHeldThings() => contents;

        //枚举子持有者，职责是让原版推进商品组件生命周期。
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, contents);

        //持久化内部托盘，职责是保留桌面物品和子订单的直接引用。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sessionId, "sessionId");
            Scribe_Values.Look(ref displayOffset, "displayOffset");
            Scribe_Deep.Look(ref contents, "contents", this);
        }

        //绘制桌面上的真实商品，职责是把同桌不同餐位的物品放在各自桌沿。
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            int index = 0;
            foreach (Thing thing in contents.Take(4))
            {
                Vector3 pos = drawLoc + displayOffset + new Vector3((index % 2) * 0.18f - 0.09f, 0.05f + index * 0.002f, index / 2 * 0.16f);
                Graphics.DrawMesh(MeshPool.plane03, pos, Quaternion.identity, thing.Graphic.MatSingleFor(thing), 0);
                index++;
            }
        }

        //移除托盘前释放实物，职责是避免桌面和会话销毁时吞掉商品。
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned)
                foreach (Thing thing in contents.ToList())
                {
                    contents.Remove(thing);
                    GenSpawn.Spawn(thing, Position, Map);
                }
            base.DeSpawn(mode);
        }
    }
}
