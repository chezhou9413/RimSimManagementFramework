using RimSimRestaurantExtension.Conveyor.Placement;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //绘制蓝图和框架的连接图集，职责是与成品和拖拽预览共用输入输出判定。
    public sealed class Graphic_SushiConveyor : Buildings.Rendering.Graphic_RestaurantLinkedAtlas
    {
        //只显示具有实际有向连接的相邻节点。
        public override bool ShouldLinkWith(IntVec3 cell, Thing parent) => parent.Spawned
            && (ConveyorLinks.Connects(parent.Map, parent.Position, cell) || ConveyorLinks.Connects(parent.Map, cell, parent.Position));

        //保留自定义连接规则，职责是支持原版蓝图与施工框架着色。
        public override Graphic GetColoredVersion(Shader shader, Color first, Color second) =>
            GraphicDatabase.Get<Graphic_SushiConveyor>(path, shader, drawSize, first, second, data, maskPath);
    }
}
