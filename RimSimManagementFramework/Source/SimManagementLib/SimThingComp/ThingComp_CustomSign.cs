using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimDialog;
using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义建筑自定义招牌图层的默认绘制比例和断电显示策略。
    /// </summary>
    public class CompProperties_CustomSign : CompProperties
    {
        public float layerWidthRatio = CustomSignDrawUtility.DefaultLayerWidthRatio;
        public float layerHeightRatio = CustomSignDrawUtility.DefaultLayerHeightRatio;
        public bool hideWhenPowerOff;

        /// <summary>
        /// 负责绑定自定义招牌组件类型。
        /// </summary>
        public CompProperties_CustomSign()
        {
            compClass = typeof(ThingComp_CustomSign);
        }
    }

    /// <summary>
    /// 挂在建筑上的自定义招牌组件，负责保存三面图层、提供编辑按钮并在地图上绘制图片。
    /// </summary>
    public class ThingComp_CustomSign : ThingComp
    {
        private SignFaceData southFace = new SignFaceData();
        private SignFaceData eastFace = new SignFaceData();
        private SignFaceData northFace = new SignFaceData();

        private CompProperties_CustomSign SignProps => props as CompProperties_CustomSign;

        /// <summary>
        /// 返回南面图层数据。
        /// </summary>
        public SignFaceData SouthFace => southFace;

        /// <summary>
        /// 返回东面图层数据。
        /// </summary>
        public SignFaceData EastFace => eastFace;

        /// <summary>
        /// 返回北面图层数据。
        /// </summary>
        public SignFaceData NorthFace => northFace;

        /// <summary>
        /// 保存和读取招牌三面的轻量图层配置。
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref southFace, "southFace");
            Scribe_Deep.Look(ref eastFace, "eastFace");
            Scribe_Deep.Look(ref northFace, "northFace");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureFaces();
        }

        /// <summary>
        /// 在建筑生成后补齐缺失的招牌面数据。
        /// </summary>
        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureFaces();
        }

        /// <summary>
        /// 在地图绘制阶段把玩家配置的图片图层叠加到建筑底图上。
        /// </summary>
        public override void PostDraw()
        {
            base.PostDraw();
            if (ShouldSkipDrawForPower())
                return;

            Vector3 drawLoc = parent.DrawPos + (parent.def.graphicData?.DrawOffsetForRot(parent.Rotation) ?? Vector3.zero);
            CustomSignDrawUtility.DrawCurrentFaceLayers(parent, southFace, eastFace, northFace, drawLoc, LayerWidthRatio, LayerHeightRatio);
        }

        /// <summary>
        /// 提供编辑、导出和导入招牌的操作按钮。
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.CustomSign.Gizmo.EditLabel"),
                defaultDesc = SimTranslation.T("RSMF.CustomSign.Gizmo.EditDesc"),
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true),
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_CustomSignEditor(this));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.CustomSign.Gizmo.ExportLabel"),
                defaultDesc = SimTranslation.T("RSMF.CustomSign.Gizmo.ExportDesc"),
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
                defaultLabel = SimTranslation.T("RSMF.CustomSign.Gizmo.ImportLabel"),
                defaultDesc = SimTranslation.T("RSMF.CustomSign.Gizmo.ImportDesc"),
                icon = TexButton.Paste,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_CustomSignImport(ApplyImportedFaces));
                }
            };
        }

        /// <summary>
        /// 把导入或编辑器保存的三面数据应用到组件。
        /// </summary>
        public void SetFaces(SignFaceData south, SignFaceData east, SignFaceData north)
        {
            southFace = south ?? new SignFaceData();
            eastFace = east ?? new SignFaceData();
            northFace = north ?? new SignFaceData();
            SanitizeAllFaces();
        }

        /// <summary>
        /// 返回指定编辑面对应的数据。
        /// </summary>
        public SignFaceData GetFace(SignFaceKind faceKind)
        {
            if (faceKind == SignFaceKind.East) return eastFace;
            if (faceKind == SignFaceKind.North) return northFace;
            return southFace;
        }

        /// <summary>
        /// 返回当前组件用于横向计算图片图层尺寸的比例。
        /// </summary>
        public float LayerWidthRatio => Mathf.Max(0.01f, SignProps?.layerWidthRatio ?? CustomSignDrawUtility.DefaultLayerWidthRatio);

        /// <summary>
        /// 返回当前组件用于纵向计算图片图层尺寸的比例。
        /// </summary>
        public float LayerHeightRatio => Mathf.Max(0.01f, SignProps?.layerHeightRatio ?? CustomSignDrawUtility.DefaultLayerHeightRatio);

        /// <summary>
        /// 把导入的招牌数据写入组件并提示玩家。
        /// </summary>
        private void ApplyImportedFaces(SignFaceData south, SignFaceData east, SignFaceData north)
        {
            SetFaces(south, east, north);
            Messages.Message(SimTranslation.T("RSMF.CustomSign.Imported"), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// 确保三个可编辑面始终存在，并清理不合法图层参数。
        /// </summary>
        private void EnsureFaces()
        {
            if (southFace == null) southFace = new SignFaceData();
            if (eastFace == null) eastFace = new SignFaceData();
            if (northFace == null) northFace = new SignFaceData();
            SanitizeAllFaces();
        }

        /// <summary>
        /// 清理三个编辑面的图层数据。
        /// </summary>
        private void SanitizeAllFaces()
        {
            CustomSignDrawUtility.SanitizeFace(southFace);
            CustomSignDrawUtility.SanitizeFace(eastFace);
            CustomSignDrawUtility.SanitizeFace(northFace);
        }

        /// <summary>
        /// 根据电力配置判断当前是否应该跳过自定义图层绘制。
        /// </summary>
        private bool ShouldSkipDrawForPower()
        {
            if (SignProps?.hideWhenPowerOff != true)
                return false;

            CompPowerTrader power = parent?.GetComp<CompPowerTrader>();
            return power != null && !power.PowerOn;
        }
    }
}
