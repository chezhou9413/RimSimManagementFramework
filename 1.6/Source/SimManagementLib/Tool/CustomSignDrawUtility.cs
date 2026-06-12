using SimManagementLib.Pojo;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责为自定义招牌组件提供共享的图层校验、尺寸计算和地图绘制逻辑。
    /// </summary>
    public static class CustomSignDrawUtility
    {
        public const float DefaultLayerWidthRatio = 0.30f;
        public const float DefaultLayerHeightRatio = 0.55f;

        /// <summary>
        /// 负责按建筑当前朝向绘制对应招牌面的所有图片图层。
        /// </summary>
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

        /// <summary>
        /// 负责按指定朝向绘制预览中的招牌图片图层，并复用地图上的朝向和镜像规则。
        /// </summary>
        public static void DrawPreviewFaceLayers(Rect displayRect, Rot4 rot, SignFaceData southFace, SignFaceData eastFace, SignFaceData northFace, float layerWidthRatio, float layerHeightRatio, int selectedLayerIndex, Color selectedBorderColor)
        {
            SignFaceData face = FaceForRotation(rot, southFace, eastFace, northFace, out bool mirror);
            if (!HasDrawableLayer(face) && rot != Rot4.South)
            {
                face = southFace;
                mirror = false;
            }

            if (!HasDrawableLayer(face))
                return;

            Rect signRect = LogicalPreviewRect(displayRect, rot);
            Matrix4x4 oldMatrix = GUI.matrix;
            float baseAngle = AngleForRotation(rot);

            List<SignImageLayerData> orderedLayers = face.layers
                .Where(layer => layer != null && layer.enabled && !string.IsNullOrEmpty(layer.imageId))
                .OrderBy(layer => layer.drawOrder)
                .ToList();

            for (int i = 0; i < orderedLayers.Count; i++)
            {
                SignImageLayerData layer = orderedLayers[i];
                Texture2D texture = SignTextureCache.GetTexture(layer.imageId);
                Vector2 imageSize = CalculateLayerSize(new Vector2(signRect.width, signRect.height), layerWidthRatio, layerHeightRatio, layer, texture);
                float x = mirror ? -layer.x : layer.x;
                Vector2 logicalCenter = new Vector2(
                    signRect.center.x + x * signRect.width * 0.5f,
                    signRect.center.y + layer.y * signRect.height * 0.5f);
                Vector2 screenCenter = RotatePoint(logicalCenter, displayRect.center, baseAngle);
                Rect imageRect = new Rect(
                    screenCenter.x - imageSize.x * 0.5f,
                    screenCenter.y - imageSize.y * 0.5f,
                    imageSize.x,
                    imageSize.y);

                GUI.matrix = oldMatrix;
                GUIUtility.RotateAroundPivot(baseAngle + (mirror ? -layer.angle : layer.angle), imageRect.center);
                GUI.DrawTexture(imageRect, texture, ScaleMode.StretchToFill, true);

                if (face.layers.IndexOf(layer) == selectedLayerIndex)
                    SimDialog.SimUiStyle.DrawBorder(imageRect, selectedBorderColor, 2f);
            }

            GUI.matrix = oldMatrix;
        }

        /// <summary>
        /// 负责把图层目标框收缩为保持图片原始宽高比的绘制尺寸。
        /// </summary>
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
        private static Vector2 CalculateLayerSize(Vector2 baseSize, float layerWidthRatio, float layerHeightRatio, SignImageLayerData layer, Texture2D texture)
        {
            Vector2 aspectSize = KeepAspectSize(new Vector2(
                baseSize.x * Mathf.Max(0.01f, layerWidthRatio),
                baseSize.y * Mathf.Max(0.01f, layerHeightRatio)), texture);
            return new Vector2(
                Mathf.Max(0.001f, aspectSize.x * Mathf.Max(0.05f, layer?.scaleX ?? 1f)),
                Mathf.Max(0.001f, aspectSize.y * Mathf.Max(0.05f, layer?.scaleY ?? 1f)));
        }

        /// <summary>
        /// 负责清理单个招牌面的空图层和越界变换参数。
        /// </summary>
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

        /// <summary>
        /// 负责判断指定招牌面是否存在可绘制图层。
        /// </summary>
        private static bool HasDrawableLayer(SignFaceData face)
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

        /// <summary>
        /// 负责根据建筑朝向选择当前需要显示的招牌面。
        /// </summary>
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

        /// <summary>
        /// 负责把招牌面内的二维偏移转换为建筑朝向下的地图偏移。
        /// </summary>
        private static Vector3 RotateOffset(Vector3 offset, Rot4 rot, bool mirror)
        {
            if (mirror)
                offset.x = -offset.x;
            if (rot == Rot4.East) return new Vector3(offset.z, 0f, -offset.x);
            if (rot == Rot4.West) return new Vector3(-offset.z, 0f, offset.x);
            if (rot == Rot4.North) return new Vector3(-offset.x, 0f, -offset.z);
            return offset;
        }

        /// <summary>
        /// 负责返回图片图层跟随建筑朝向需要旋转的基础角度。
        /// </summary>
        private static float AngleForRotation(Rot4 rot)
        {
            if (rot == Rot4.East) return 90f;
            if (rot == Rot4.West) return 270f;
            if (rot == Rot4.North) return 180f;
            return 0f;
        }

        /// <summary>
        /// 返回预览中旋转前的逻辑招牌框。
        /// </summary>
        private static Rect LogicalPreviewRect(Rect displayRect, Rot4 rot)
        {
            if (rot != Rot4.East && rot != Rot4.West)
                return displayRect;

            return new Rect(
                displayRect.center.x - displayRect.height * 0.5f,
                displayRect.center.y - displayRect.width * 0.5f,
                displayRect.height,
                displayRect.width);
        }

        /// <summary>
        /// 负责把逻辑预览坐标绕显示框中心转换为屏幕坐标。
        /// </summary>
        private static Vector2 RotatePoint(Vector2 point, Vector2 pivot, float angle)
        {
            if (Mathf.Abs(angle) < 0.001f)
                return point;

            float radians = angle * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            Vector2 delta = point - pivot;
            return new Vector2(
                pivot.x + delta.x * cos - delta.y * sin,
                pivot.y + delta.x * sin + delta.y * cos);
        }
    }
}
