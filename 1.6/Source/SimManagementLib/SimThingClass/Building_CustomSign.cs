using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDialog;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 负责保存玩家招牌三面图层配置，并在地图上绘制自定义图案。
    /// </summary>
    public class Building_CustomSign : Building
    {
        public const float LayerWidthRatio = 0.30f;
        public const float LayerHeightRatio = 0.55f;

        private SignFaceData southFace = new SignFaceData();
        private SignFaceData eastFace = new SignFaceData();
        private SignFaceData northFace = new SignFaceData();

        /// <summary>
        /// 负责返回南面图层数据。
        /// </summary>
        public SignFaceData SouthFace => southFace;

        /// <summary>
        /// 负责返回东面图层数据。
        /// </summary>
        public SignFaceData EastFace => eastFace;

        /// <summary>
        /// 负责返回北面图层数据。
        /// </summary>
        public SignFaceData NorthFace => northFace;

        /// <summary>
        /// 负责保存和读取招牌三面的轻量图层配置。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref southFace, "southFace");
            Scribe_Deep.Look(ref eastFace, "eastFace");
            Scribe_Deep.Look(ref northFace, "northFace");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (southFace == null) southFace = new SignFaceData();
                if (eastFace == null) eastFace = new SignFaceData();
                if (northFace == null) northFace = new SignFaceData();
                SanitizeAllFaces();
            }
        }

        /// <summary>
        /// 负责提供编辑、导出和导入招牌的操作按钮。
        /// </summary>
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            yield return new Command_Action
            {
                defaultLabel = "编辑招牌",
                defaultDesc = "打开招牌编辑器，配置南面、东面和北面的图案图层。",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true),
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_CustomSignEditor(this));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "导出招牌",
                defaultDesc = "导出当前招牌三面图案配置，分享文本会包含用到的图片。",
                icon = TexButton.Copy,
                action = delegate
                {
                    if (!SignImageLibrary.TryExportShareText(southFace, eastFace, northFace, out string text, out string error))
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    Find.WindowStack.Add(new Dialog_CustomSignTransfer(text));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "导入招牌",
                defaultDesc = "从其他玩家分享的文本导入招牌配置，并自动同步图片到本地图库。",
                icon = TexButton.Paste,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_CustomSignImport(ApplyImportedFaces));
                }
            };
        }

        /// <summary>
        /// 负责把导入或编辑器保存的三面数据应用到建筑。
        /// </summary>
        public void SetFaces(SignFaceData south, SignFaceData east, SignFaceData north)
        {
            southFace = south ?? new SignFaceData();
            eastFace = east ?? new SignFaceData();
            northFace = north ?? new SignFaceData();
            SanitizeAllFaces();
        }

        /// <summary>
        /// 负责强制走实时绘制流程，避免地图网格绘制阶段吞掉动态图片。
        /// </summary>
        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (phase == DrawPhase.Draw)
                DrawAt(drawLoc, flip);
        }

        /// <summary>
        /// 负责按建筑朝向绘制当前可见面的全部图片图层。
        /// </summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            DrawCurrentFaceLayers(drawLoc);
        }

        /// <summary>
        /// 负责返回指定编辑面对应的数据。
        /// </summary>
        public SignFaceData GetFace(SignFaceKind faceKind)
        {
            if (faceKind == SignFaceKind.East) return eastFace;
            if (faceKind == SignFaceKind.North) return northFace;
            return southFace;
        }

        private void ApplyImportedFaces(SignFaceData south, SignFaceData east, SignFaceData north)
        {
            SetFaces(south, east, north);
            Messages.Message("招牌图案已导入。", MessageTypeDefOf.PositiveEvent, false);
        }

        private void DrawCurrentFaceLayers(Vector3 drawLoc)
        {
            SignFaceData face = FaceForRotation(Rotation, out bool mirror);
            if (!HasDrawableLayer(face) && Rotation != Rot4.South)
            {
                face = southFace;
                mirror = false;
            }

            if (!HasDrawableLayer(face))
                return;

            Vector2 baseSize = def.graphicData?.drawSize ?? def.size.ToVector2();
            List<SignImageLayerData> orderedLayers = face.layers
                .Where(layer => layer != null && layer.enabled && !string.IsNullOrEmpty(layer.imageId))
                .OrderBy(layer => layer.drawOrder)
                .ToList();

            for (int i = 0; i < orderedLayers.Count; i++)
            {
                SignImageLayerData layer = orderedLayers[i];
                Texture2D texture = SignTextureCache.GetTexture(layer.imageId);
                Vector2 layerSize = KeepAspectSize(new Vector2(
                    baseSize.x * LayerWidthRatio * Mathf.Max(0.05f, layer.scaleX),
                    baseSize.y * LayerHeightRatio * Mathf.Max(0.05f, layer.scaleY)), texture);
                Vector3 localOffset = new Vector3(layer.x * baseSize.x * 0.5f, 0f, -layer.y * baseSize.y * 0.5f);
                Vector3 worldOffset = RotateOffset(localOffset, Rotation, mirror);
                Vector3 loc = drawLoc + worldOffset;
                loc.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + 0.005f + i * 0.002f;
                Mesh mesh = MeshPool.GridPlane(layerSize, mirror);
                Material material = SignTextureCache.GetMaterial(layer.imageId);
                Quaternion quat = Quaternion.AngleAxis(AngleForRotation(Rotation, mirror) + layer.angle, Vector3.up);
                Graphics.DrawMesh(mesh, loc, quat, material, 0);
            }
        }

        private SignFaceData FaceForRotation(Rot4 rot, out bool mirror)
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

        private static Vector3 RotateOffset(Vector3 offset, Rot4 rot, bool mirror)
        {
            if (mirror)
                offset.x = -offset.x;
            if (rot == Rot4.East) return new Vector3(offset.z, 0f, -offset.x);
            if (rot == Rot4.West) return new Vector3(-offset.z, 0f, offset.x);
            if (rot == Rot4.North) return new Vector3(-offset.x, 0f, -offset.z);
            return offset;
        }

        private static float AngleForRotation(Rot4 rot, bool mirror)
        {
            if (rot == Rot4.East) return 90f;
            if (rot == Rot4.West) return 270f;
            if (rot == Rot4.North) return 180f;
            return 0f;
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

        private void SanitizeAllFaces()
        {
            SanitizeFace(southFace);
            SanitizeFace(eastFace);
            SanitizeFace(northFace);
        }

        private static void SanitizeFace(SignFaceData face)
        {
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
    }

    /// <summary>
    /// 负责标识招牌编辑器中可编辑的三个面。
    /// </summary>
    public enum SignFaceKind
    {
        South,
        East,
        North
    }
}
