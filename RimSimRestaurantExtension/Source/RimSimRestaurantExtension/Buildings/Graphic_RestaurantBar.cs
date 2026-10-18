using UnityEngine;
using Verse;
namespace RimSimRestaurantExtension.Buildings
{
    //绘制拼接吧台图集，职责是仅与同一建筑定义连接。
    public sealed class Graphic_RestaurantBar : Rendering.Graphic_RestaurantLinkedAtlas
    {
        //限制连接对象，职责是避免吧台连接墙壁或其他模组家具。
        public override bool ShouldLinkWith(IntVec3 cell, Thing parent)
        {
            if (!parent.Spawned || !cell.InBounds(parent.Map)) return false;
            BuildableDef target = parent.def.entityDefToBuild ?? parent.def;
            var things = cell.GetThingList(parent.Map);
            for (int i = 0; i < things.Count; i++)
                if ((things[i].def.entityDefToBuild ?? things[i].def) == target) return true;
            return false;
        }
        //创建染色版本，职责是保持同类连接规则。
        public override Graphic GetColoredVersion(Shader shader, Color first, Color second)
        {
            return GraphicDatabase.Get<Graphic_RestaurantBar>(path, shader, drawSize, first, second, data, maskPath);
        }
    }
}
