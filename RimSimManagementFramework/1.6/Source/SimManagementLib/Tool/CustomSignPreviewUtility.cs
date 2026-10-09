using SimManagementLib.Pojo;
using SimManagementLib.SimDialog;
using SimManagementLib.SimThingComp;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    //用地图中的尺寸、偏移和旋转规则绘制招牌预览，并提供对应的鼠标坐标换算。
    internal static class CustomSignPreviewUtility
    {
        //按建筑实际网格的旋转后尺寸计算统一缩放，留出图层拖动空间。
        public static float PixelsPerUnit(Rect area, Thing thing, Rot4 rotation)
        {
            Vector2 size = BaseMeshSize(thing.Graphic, rotation);
            float angle = thing.Graphic.QuatFromRot(rotation).eulerAngles.y * Mathf.Deg2Rad;
            float width = Mathf.Abs(Mathf.Cos(angle)) * size.x + Mathf.Abs(Mathf.Sin(angle)) * size.y;
            float height = Mathf.Abs(Mathf.Sin(angle)) * size.x + Mathf.Abs(Mathf.Cos(angle)) * size.y;
            return Mathf.Min(area.width / Mathf.Max(0.1f, width), area.height / Mathf.Max(0.1f, height)) * 0.72f;
        }

        //底图使用 Graphic 自己的旋转与镜像规则，避免把多方向底图再次旋转。
        public static void DrawBase(Vector2 center, float pixels, Thing thing, Rot4 rotation)
        {
            Graphic graphic = thing.Graphic;
            Material material = graphic.MatAt(rotation, thing);
            Vector2 size = BaseMeshSize(graphic, rotation) * pixels;
            bool flip = rotation == Rot4.West && graphic.WestFlipped || rotation == Rot4.East && graphic.EastFlipped;
            DrawTexture(new Rect(center.x - size.x / 2f, center.y - size.y / 2f, size.x, size.y),
                material.mainTexture, graphic.QuatFromRot(rotation).eulerAngles.y, flip, thing.DrawColor, false, Color.white);
        }

        //从地图网格和朝向得到底图未旋转时的尺寸。
        private static Vector2 BaseMeshSize(Graphic graphic, Rot4 rotation)
        {
            return rotation.IsHorizontal && !graphic.ShouldDrawRotated
                ? new Vector2(graphic.drawSize.y, graphic.drawSize.x) : graphic.drawSize;
        }

        //使用与地图相同的偏移换算，返回图层绕中心旋转前的矩形。
        public static Rect LayerRect(Vector2 center, float pixels, ThingComp_CustomSign sign,
            SignImageLayerData layer, Rot4 rotation, bool mirror)
        {
            Vector2 baseSize = sign.parent.def.graphicData?.drawSize ?? sign.parent.def.size.ToVector2();
            Vector2 size = CustomSignDrawUtility.CalculateLayerSize(baseSize, sign.LayerWidthRatio,
                sign.LayerHeightRatio, layer, SignTextureCache.GetTexture(layer.imageId)) * pixels;
            Vector3 offset = CustomSignDrawUtility.RotateOffset(
                new Vector3(layer.x * baseSize.x * 0.5f, 0f, -layer.y * baseSize.y * 0.5f), rotation, mirror);
            center += new Vector2(offset.x, -offset.z) * pixels;
            return new Rect(center.x - size.x / 2f, center.y - size.y / 2f, size.x, size.y);
        }

        //绘制与地图一致的图层，包括西面的纹理镜像及旋转方向。
        public static void DrawLayer(Rect rect, SignImageLayerData layer, Rot4 rotation, bool mirror, bool selected, Color border)
        {
            DrawTexture(rect, SignTextureCache.GetTexture(layer.imageId),
                CustomSignDrawUtility.AngleForRotation(rotation) + layer.angle, mirror, Color.white, selected, border);
        }

        //鼠标命中使用逆旋转，允许直接选中画布上实际可见的图层。
        public static bool Contains(Rect rect, Vector2 mouse, Rot4 rotation, SignImageLayerData layer)
        {
            return rect.Contains(rect.center + Rotate(mouse - rect.center,
                -CustomSignDrawUtility.AngleForRotation(rotation) - layer.angle));
        }

        //把画布拖动距离还原为图层本地偏移，西面镜像时同样跟随鼠标移动。
        public static Vector2 LocalDrag(Vector2 delta, float pixels, Thing thing, Rot4 rotation, bool mirror)
        {
            Vector2 local = Rotate(delta, -CustomSignDrawUtility.AngleForRotation(rotation)) / pixels;
            if (mirror) local.x = -local.x;
            Vector2 size = thing.def.graphicData?.drawSize ?? thing.def.size.ToVector2();
            return new Vector2(local.x * 2f / size.x, local.y * 2f / size.y);
        }

        //只在重绘时调整矩阵，使用原版支持 UI 缩放的旋转入口并恢复全部绘制状态。
        private static void DrawTexture(Rect rect, Texture texture, float angle, bool mirror, Color color, bool selected, Color border)
        {
            if (Event.current.type != EventType.Repaint) return;
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            try
            {
                GUI.color = color;
                if (!Mathf.Approximately(angle, 0f)) UI.RotateAroundPivot(angle, rect.center);
                GUI.DrawTextureWithTexCoords(rect, texture, mirror ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f), true);
                if (selected) SimUiStyle.DrawBorder(rect, border, 2f);
            }
            finally { GUI.matrix = matrix; GUI.color = previous; }
        }

        //在屏幕坐标中旋转二维向量。
        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }
    }
}
