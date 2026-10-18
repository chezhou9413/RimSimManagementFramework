using UnityEngine;
using Verse;
namespace RimSimRestaurantExtension.Buildings
{
    //绘制墙前柜体，职责是将指向支撑墙的逻辑朝向映射为朝向柜前的家具贴图。
    public sealed class Graphic_RestaurantWallCabinet : Graphic_Multi
    {
        public override Material MatNorth => base.MatSouth;
        public override Material MatSouth => base.MatNorth;
        public override Material MatEast => base.MatWest;
        public override Material MatWest => base.MatEast;
        public override bool EastFlipped => base.WestFlipped;
        public override bool WestFlipped => base.EastFlipped;
        public override bool ShouldDrawRotated => false;

        //生成染色版本，职责是保留墙体依附的方向映射和蒙版。
        public override Graphic GetColoredVersion(Shader shader, Color first, Color second) =>
            GraphicDatabase.Get<Graphic_RestaurantWallCabinet>(path, shader, drawSize, first, second, data);
    }
}
