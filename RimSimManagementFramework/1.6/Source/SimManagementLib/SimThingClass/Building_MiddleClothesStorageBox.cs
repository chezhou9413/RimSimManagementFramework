using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //中服装货柜，职责是在南北柜面按单行十槽绘制当前上架的服装。
    public sealed class Building_MiddleClothesStorageBox : Building_UniqueGoodsContainer
    {
        private const int DisplaySlotCount = 10;
        private const float MaxApparelWidth = 0.14f;
        private const float MaxApparelHeight = 0.14f;
        private const float CenterX = 0f;
        private const float CenterY = 0.3f;
        private const float ApparelScale = 4f;
        private const float ApparelAngle = 180f;
        private const float SlotSpacing = 0.18f;

        //绘制南北柜面的单行服装，职责是让侧面、空槽和已售槽位不显示服装。
        protected override void DrawUniqueGoods(Vector3 drawLoc, bool flip)
        {
            if (Rotation != Rot4.South && Rotation != Rot4.North)
                return;

            IReadOnlyList<UniqueGoodsSlotData> slots = UniqueSlots;
            int count = Math.Min(DisplaySlotCount, slots.Count);
            for (int i = 0; i < count; i++)
            {
                Thing apparel = GetStoredThing(slots[i]);
                if (apparel?.Graphic == null)
                    continue;

                DrawApparelInSlot(apparel, drawLoc, i);
            }
        }

        //绘制单件服装槽位，职责是保持服装贴图比例并按十个横向槽位排列。
        private static void DrawApparelInSlot(Thing apparel, Vector3 drawLoc, int slotIndex)
        {
            Graphic graphic = ResolveDrawableGraphic(apparel.Graphic);
            if (graphic == null)
                return;

            Vector2 originalSize = graphic.drawSize;
            float widthScale = MaxApparelWidth / Mathf.Max(0.01f, originalSize.x);
            float heightScale = MaxApparelHeight / Mathf.Max(0.01f, originalSize.y);
            float scale = Mathf.Min(widthScale, heightScale) * ApparelScale;
            Graphic displayGraphic = graphic.GetCopy(originalSize * scale, null);

            Vector3 apparelLoc = drawLoc;
            apparelLoc.x += CenterX + (slotIndex - (DisplaySlotCount - 1) * 0.5f) * SlotSpacing;
            apparelLoc.z += CenterY;
            apparelLoc.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + slotIndex * 0.002f;
            displayGraphic.Draw(apparelLoc, Rot4.South, apparel, ApparelAngle);
        }

        //提取可绘制的实际服装贴图，职责是跳过随机旋转包装层以避免无效泛型构造。
        private static Graphic ResolveDrawableGraphic(Graphic graphic)
        {
            while (graphic is Graphic_RandomRotated randomRotated)
                graphic = randomRotated.SubGraphic;
            return graphic;
        }
    }
}
