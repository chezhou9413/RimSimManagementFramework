using RimWorld;
using SimManagementLib.Pojo;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //服装假人货柜基类，职责是按指定原版体型约束衣帽槽位并把穿戴贴图绘制到假人上。
    public abstract class Building_MannequinClothesStorageBox : Building_UniqueGoodsContainer
    {
        private const int BodyApparelSlotIndex = 0;
        private const int HeadgearSlotIndex = 1;
        private const float BodyOffsetZ = 0.45f;
        private const float BodyAltitudeOffset = 0.001f;
        private const float HeadgearAltitudeOffset = 0.002f;

        protected abstract BodyTypeDef MannequinBodyType { get; }
        protected abstract float BodyDisplayScale { get; }
        protected abstract Vector2 BodyDisplayOffset { get; }
        protected abstract float HeadgearDisplayScale { get; }
        protected abstract Vector2 HeadgearDisplayOffset { get; }

        //判断物品是否属于该假人可展示的衣服或帽子，并拒绝缺少目标体型贴图的衣服。
        protected override bool IsEligibleUniqueThing(Thing thing)
        {
            if (!base.IsEligibleUniqueThing(thing) || !(thing is Apparel apparel))
                return false;

            return IsHeadgear(apparel)
                ? HasWornGraphic(apparel, null)
                : HasWornGraphic(apparel, MannequinBodyType);
        }

        //判断物品是否匹配固定槽位，职责是保证零号槽只放衣服、一号槽只放帽子。
        protected override bool IsEligibleUniqueThingForSlot(Thing thing, int slotIndex)
        {
            if (!IsEligibleUniqueThing(thing) || !(thing is Apparel apparel))
                return false;

            if (slotIndex == BodyApparelSlotIndex)
                return !IsHeadgear(apparel);
            if (slotIndex == HeadgearSlotIndex)
                return IsHeadgear(apparel);
            return false;
        }

        //绘制假人当前上架的衣服和帽子，职责是让侧面朝向与空槽保持不显示。
        protected override void DrawUniqueGoods(Vector3 drawLoc, bool flip)
        {
            if (Rotation != Rot4.South && Rotation != Rot4.North)
                return;

            DrawSlotApparel(drawLoc, BodyApparelSlotIndex, false);
            DrawSlotApparel(drawLoc, HeadgearSlotIndex, true);
        }

        //绘制指定衣帽槽位，职责是使用原版穿戴图形解析器取得正确的体型和颜色变体。
        private void DrawSlotApparel(Vector3 drawLoc, int slotIndex, bool headgear)
        {
            UniqueGoodsSlotData slot = GetUniqueSlot(slotIndex);
            if (!(GetStoredThing(slot) is Apparel apparel))
                return;
            if (!ApparelGraphicRecordGetter.TryGetGraphicApparel(
                    apparel,
                    MannequinBodyType,
                    false,
                    out ApparelGraphicRecord record)
                || record.graphic == null)
                return;

            Vector3 apparelLoc = drawLoc;
            Vector2 displayOffset = headgear ? HeadgearDisplayOffset : BodyDisplayOffset;
            apparelLoc.x += displayOffset.x;
            apparelLoc.z += BodyOffsetZ + displayOffset.y;
            if (headgear)
                apparelLoc.z += MannequinBodyType.headOffset.y;
            apparelLoc.y = AltitudeLayer.BuildingOnTop.AltitudeFor()
                + (headgear ? HeadgearAltitudeOffset : BodyAltitudeOffset);
            float displayScale = headgear ? HeadgearDisplayScale : BodyDisplayScale;
            Graphic displayGraphic = Mathf.Abs(displayScale - 1f) > 0.001f
                ? record.graphic.GetCopy(record.graphic.drawSize * displayScale, null)
                : record.graphic;
            displayGraphic.Draw(apparelLoc, Rot4.South, apparel, 0f);
        }

        //判断服装是否属于头部穿戴层，职责是把头盔、帽子和眼部覆盖物归入帽子槽。
        private static bool IsHeadgear(Apparel apparel)
        {
            ApparelLayerDef layer = apparel?.def?.apparel?.LastLayer;
            return layer == ApparelLayerDefOf.Overhead || layer == ApparelLayerDefOf.EyeCover;
        }

        //判断服装是否存在可绘制的穿戴贴图，职责是对身体服装严格检查指定体型后缀。
        private static bool HasWornGraphic(Apparel apparel, BodyTypeDef bodyType)
        {
            string wornPath = apparel?.WornGraphicPath;
            if (wornPath.NullOrEmpty())
                return false;

            string resolvedPath = bodyType == null
                ? wornPath
                : wornPath + "_" + bodyType.defName;
            return ContentFinder<Texture2D>.Get(resolvedPath + "_south", false) != null;
        }
    }
}
