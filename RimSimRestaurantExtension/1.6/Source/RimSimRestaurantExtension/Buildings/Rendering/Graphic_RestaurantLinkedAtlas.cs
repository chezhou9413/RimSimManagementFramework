using RimWorld;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Buildings.Rendering
{
    //绘制餐厅连接图集，职责是直接传递图块坐标，避免蓝图着色器依赖材质缩放和偏移。
    public abstract class Graphic_RestaurantLinkedAtlas : Graphic_Linked
    {
        //初始化未裁剪的源材质，职责是保留纹理、蒙版、绘制队列和着色参数。
        public override void Init(GraphicRequest req)
        {
            subGraphic = new Graphic_Single();
            subGraphic.Init(req);
            data = req.graphicData;
            path = req.path;
            maskPath = req.maskPath;
            color = req.color;
            colorTwo = req.colorTwo;
            drawSize = req.drawSize;
        }

        //绘制动态蓝图、施工轮廓和鼠标预览，职责是让连接方向保持世界坐标并只采样一块图集。
        public override void DrawWorker(Vector3 location, Rot4 rotation, ThingDef definition, Thing thing, float extraRotation)
        {
            int links = ConnectionMask(thing);
            Matrix4x4 transform = Matrix4x4.TRS(location + DrawOffset(rotation),
                Quaternion.Euler(0f, extraRotation, 0f), new Vector3(drawSize.x, 1f, drawSize.y));
            Graphics.DrawMesh(RestaurantAtlasGeometry.MeshFor(links), transform, subGraphic.MatSingle, 0);
            ShadowGraphic?.DrawWorker(location, rotation, definition, thing, extraRotation);
        }

        //写入静态地图网格，职责是让成品和静态蓝图使用与动态绘制相同的裁剪区域。
        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            Printer_Plane.PrintPlane(layer, thing.TrueCenter(), drawSize, subGraphic.MatSingleFor(thing),
                extraRotation, false, RestaurantAtlasGeometry.Coordinates(ConnectionMask(thing)));
            ShadowGraphic?.Print(layer, thing, 0f);
        }

        //读取四向连接位，职责是让具体建筑决定邻接规则并让无实体预览显示独立图块。
        private int ConnectionMask(Thing thing)
        {
            if (thing?.Spawned != true) return 0;
            int links = 0;
            for (int i = 0; i < 4; i++)
                if (ShouldLinkWith(thing.Position + GenAdj.CardinalDirections[i], thing)) links |= 1 << i;
            return links;
        }
    }
}
