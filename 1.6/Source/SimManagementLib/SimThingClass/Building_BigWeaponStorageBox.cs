using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    //大武器货柜，职责是在南面柜面按两行十槽绘制当前上架的武器。
    public sealed class Building_BigWeaponStorageBox : Building_UniqueGoodsContainer
    {
        private const int DisplaySlotCount = 10;
        private const int ColumnCount = 5;
        private const float MaxWeaponWidth = 0.14f;
        private const float MaxWeaponHeight = 0.12f;
        private const float CenterX = 0f;
        private const float CenterY = 0.78f;
        private const float WeaponScale = 5.5f;
        private const float WeaponAngle = 180f;
        private const float ColumnSpacing = 0.34f;
        private const float RowSpacing = 0.34f;

        //绘制南面柜面的两行武器，职责是让其他朝向、空槽和已售槽位不显示武器。
        protected override void DrawUniqueGoods(Vector3 drawLoc, bool flip)
        {
            if (Rotation != Rot4.South)
                return;

            IReadOnlyList<UniqueGoodsSlotData> slots = UniqueSlots;
            int count = Math.Min(DisplaySlotCount, slots.Count);
            for (int i = 0; i < count; i++)
            {
                Thing weapon = GetStoredThing(slots[i]);
                if (weapon?.Graphic == null)
                    continue;

                DrawWeaponInSlot(weapon, drawLoc, i);
            }
        }

        //绘制单个武器槽位，职责是按五列两行布局纵向武器贴图。
        private static void DrawWeaponInSlot(Thing weapon, Vector3 drawLoc, int slotIndex)
        {
            Graphic graphic = ResolveDrawableGraphic(weapon.Graphic);
            if (graphic == null)
                return;

            Vector2 originalSize = graphic.drawSize;
            float widthScale = MaxWeaponWidth / Mathf.Max(0.01f, originalSize.x);
            float heightScale = MaxWeaponHeight / Mathf.Max(0.01f, originalSize.y);
            float scale = Mathf.Min(widthScale, heightScale) * WeaponScale;
            Graphic displayGraphic = graphic.GetCopy(originalSize * scale, null);

            int row = slotIndex / ColumnCount;
            int column = slotIndex % ColumnCount;
            Vector3 weaponLoc = drawLoc;
            weaponLoc.x += CenterX + (column - (ColumnCount - 1) * 0.5f) * ColumnSpacing;
            weaponLoc.z += CenterY + (0.5f - row) * RowSpacing;
            weaponLoc.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + slotIndex * 0.002f;
            displayGraphic.Draw(weaponLoc, Rot4.South, weapon, WeaponAngle);
        }

        //提取可复制的实际武器贴图，职责是跳过没有无参构造函数的随机旋转包装层。
        private static Graphic ResolveDrawableGraphic(Graphic graphic)
        {
            while (graphic is Graphic_RandomRotated randomRotated)
                graphic = randomRotated.SubGraphic;
            return graphic;
        }
    }
}
