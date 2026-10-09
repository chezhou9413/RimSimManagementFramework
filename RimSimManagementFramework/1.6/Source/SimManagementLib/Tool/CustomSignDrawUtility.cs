using SimManagementLib.Pojo;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    //负责为自定义招牌组件提供共享的图层校验、尺寸计算和地图绘制逻辑。
    public static class CustomSignDrawUtility
    {
        public const float DefaultLayerWidthRatio = 0.30f;
        public const float DefaultLayerHeightRatio = 0.55f;

        //负责按建筑当前朝向绘制对应招牌面的所有图片图层。
        public static void DrawCurrentFaceLayers(Thing thing, SignFaceData southFace, SignFaceData eastFace, SignFaceData northFace, Vector3 drawLoc, float layerWidthRatio, float layerHeightRatio)
        {
            if (thing == null)
                return;

            SignFaceData face = FaceForRotation(thing.Rotation, southFace, eastFace, northFace, out bool mirror);
            if (!HasDrawableLayer(face) && thing.Rotation != Rot4.South)
            {
                face = southFace;
                mirror = false;
            }

            if (!HasDrawableLayer(face))
                return;

            Vector2 baseSize = thing.def.graphicData?.drawSize ?? thing.def.size.ToVector2();
            List<SignImageLayerData> orderedLayers = face.layers
                .Where(layer => layer != null && layer.enabled && !string.IsNullOrEmpty(layer.imageId))
                .OrderBy(layer => layer.drawOrder)
                .ToList();

            for (int i = 0; i < orderedLayers.Count; i++)
            {
                SignImageLayerData layer = orderedLayers[i];
                Texture2D texture = SignTextureCache.GetTexture(layer.imageId);
                Vector2 layerSize = CalculateLayerSize(baseSize, layerWidthRatio, layerHeightRatio, layer, texture);
                Vector3 localOffset = new Vector3(layer.x * baseSize.x * 0.5f, 0f, -layer.y * baseSize.y * 0.5f);
                Vector3 worldOffset = RotateOffset(localOffset, thing.Rotation, mirror);
                Vector3 loc = drawLoc + worldOffset;
                loc.y = AltitudeLayer.Item.AltitudeFor() + 0.01f + i * 0.002f;
                Mesh mesh = MeshPool.GridPlane(layerSize, mirror);
                Material material = SignTextureCache.GetMaterial(layer.imageId);
                Quaternion quat = Quaternion.AngleAxis(AngleForRotation(thing.Rotation) + layer.angle, Vector3.up);
                Graphics.DrawMesh(mesh, loc, quat, material, 0);
            }
        }

        //负责把图层目标框收缩为保持图片原始宽高比的绘制尺寸。
        public static Vector2 KeepAspectSize(Vector2 targetSize, Texture2D texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0 || targetSize.x <= 0f || targetSize.y <= 0f)
                return targetSize;

            float textureAspect = texture.width / (float)texture.height;
            float targetAspect = targetSize.x / targetSize.y;
            if (targetAspect > textureAspect)
                targetSize.x = targetSize.y * textureAspect;
            else
                targetSize.y = targetSize.x / textureAspect;

            return targetSize;
        }

        //计算图层最终尺寸，负责让默认尺寸保持图片比例，同时让 X/Y 拉伸滑条都能真实生效。
        internal static Vector2 CalculateLayerSize(Vector2 baseSize, float layerWidthRatio, float layerHeightRatio, SignImageLayerData layer, Texture2D texture)
        {
            Vector2 aspectSize = KeepAspectSize(new Vector2(
                baseSize.x * Mathf.Max(0.01f, layerWidthRatio),
                baseSize.y * Mathf.Max(0.01f, layerHeightRatio)), texture);
            return new Vector2(
                Mathf.Max(0.001f, aspectSize.x * Mathf.Max(0.05f, layer?.scaleX ?? 1f)),
                Mathf.Max(0.001f, aspectSize.y * Mathf.Max(0.05f, layer?.scaleY ?? 1f)));
        }

        //负责清理单个招牌面的空图层和越界变换参数。
        public static void SanitizeFace(SignFaceData face)
        {
            if (face == null)
                return;

            if (face.layers == null)
                face.layers = new List<SignImageLayerData>();

            face.layers.RemoveAll(layer => layer == null);
            while (face.layers.Count > SignImageLibrary.MaxFaceLayerCount)
                face.layers.RemoveAt(face.layers.Count - 1);

            for (int i = 0; i < face.layers.Count; i++)
            {
                SignImageLayerData layer = face.layers[i];
                layer.drawOrder = i;
                layer.x = Mathf.Clamp(layer.x, -1f, 1f);
                layer.y = Mathf.Clamp(layer.y, -1f, 1f);
                layer.scaleX = Mathf.Clamp(layer.scaleX, 0.05f, 4f);
                layer.scaleY = Mathf.Clamp(layer.scaleY, 0.05f, 4f);
            }
        }

        //负责判断指定招牌面是否存在可绘制图层。
        internal static bool HasDrawableLayer(SignFaceData face)
        {
            if (face?.layers == null)
                return false;

            for (int i = 0; i < face.layers.Count; i++)
            {
                SignImageLayerData layer = face.layers[i];
                if (layer != null && layer.enabled && !string.IsNullOrEmpty(layer.imageId))
                    return true;
            }

            return false;
        }

        //负责根据建筑朝向选择当前需要显示的招牌面。
        private static SignFaceData FaceForRotation(Rot4 rot, SignFaceData southFace, SignFaceData eastFace, SignFaceData northFace, out bool mirror)
        {
            mirror = false;
            if (rot == Rot4.North) return northFace;
            if (rot == Rot4.East) return eastFace;
            if (rot == Rot4.West)
            {
                mirror = true;
                return eastFace;
            }

            return southFace;
        }

        //负责把招牌面内的二维偏移转换为建筑朝向下的地图偏移。
        internal static Vector3 RotateOffset(Vector3 offset, Rot4 rot, bool mirror)
        {
            if (mirror)
                offset.x = -offset.x;
            if (rot == Rot4.East) return new Vector3(offset.z, 0f, -offset.x);
            if (rot == Rot4.West) return new Vector3(-offset.z, 0f, offset.x);
            if (rot == Rot4.North) return new Vector3(-offset.x, 0f, -offset.z);
            return offset;
        }

        //负责返回图片图层跟随建筑朝向需要旋转的基础角度。
        internal static float AngleForRotation(Rot4 rot)
        {
            if (rot == Rot4.East) return 90f;
            if (rot == Rot4.West) return 270f;
            if (rot == Rot4.North) return 180f;
            return 0f;
        }

    }
}
