using RimSimRestaurantExtension.Conveyor.Placement;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //绘制蓝图和框架的连接图集，职责是与成品和拖拽预览共用输入输出判定。
    public sealed class Graphic_SushiConveyor : Graphic_Linked
    {
        //建立支持染色的静态图集，职责是保留原版蓝图颜色。
        public override void Init(GraphicRequest req)
        {
            subGraphic = GraphicDatabase.Get<Graphic_Single>(req.path, req.shader, req.drawSize, req.color, req.colorTwo, req.graphicData);
            data = req.graphicData; path = req.path; color = req.color; colorTwo = req.colorTwo; drawSize = req.drawSize;
        }

        //只显示具有实际有向连接的相邻节点。
        public override bool ShouldLinkWith(IntVec3 cell, Thing parent) => parent.Spawned
            && (ConveyorLinks.Connects(parent.Map, parent.Position, cell) || ConveyorLinks.Connects(parent.Map, cell, parent.Position));

        //按实际节点连接绘制动态图块，职责是让实时蓝图避免显示孤立图块。
        public override void DrawWorker(Vector3 location, Rot4 rotation, ThingDef definition, Thing thing, float extraRotation)
        {
            Graphics.DrawMesh(MeshPool.plane10, location, Quaternion.identity,
                thing?.Spawned == true ? MatSingleFor(thing) : MatSingle, 0);
        }

        //保留自定义连接规则，职责是支持原版蓝图与施工框架着色。
        public override Graphic GetColoredVersion(Shader shader, Color first, Color second) =>
            GraphicDatabase.Get<Graphic_SushiConveyor>(path, shader, drawSize, first, second, data);
    }
}
