using UnityEngine;
using Verse;
namespace RimSimRestaurantExtension.Buildings
{
    //绘制拼接吧台图集，职责是仅与同一建筑定义连接。
    public sealed class Graphic_RestaurantBar : Graphic_Linked
    {
        //初始化原版四向连接图集，职责是保留蒙版和染色参数。
        public override void Init(GraphicRequest req)
        {
            subGraphic = GraphicDatabase.Get<Graphic_Single>(req.path, req.shader, req.drawSize, req.color, req.colorTwo, req.graphicData);
            data = req.graphicData;
            path = req.path;
            color = req.color;
            colorTwo = req.colorTwo;
            drawSize = req.drawSize;
        }
        //限制连接对象，职责是避免吧台连接墙壁或其他模组家具。
        public override bool ShouldLinkWith(IntVec3 cell, Thing parent)
        {
            return parent.Spawned && cell.InBounds(parent.Map) && cell.GetEdifice(parent.Map)?.def == parent.def;
        }
        //创建染色版本，职责是保持同类连接规则。
        public override Graphic GetColoredVersion(Shader shader, Color first, Color second)
        {
            return GraphicDatabase.Get<Graphic_RestaurantBar>(path, shader, drawSize, first, second, data);
        }
    }
}
