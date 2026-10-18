using SimManagementLib.Pojo;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 收藏品展台渲染部分，职责是绘制展台本体和槽位内收藏品。
    /// </summary>
    public partial class Building_CollectibleDisplayStand
    {
        /// <summary>
        /// 实时绘制展台和上层收藏品，职责是让槽位调整立即反映在地图上。
        /// </summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            DrawStoredCollectibles();
        }

        /// <summary>
        /// 绘制每个槽位保存的收藏品，职责是按槽位参数叠加到展台上方。
        /// </summary>
        private void DrawStoredCollectibles()
        {
            EnsureSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                CollectibleDisplaySlotData slot = slots[i];
                Thing thing = slot?.StoredThing;
                if (thing?.Graphic == null)
                    continue;

                Graphic graphic = thing.Graphic;
                float drawScale = Mathf.Clamp(slot.scale, 0.2f, 3f);
                if (Mathf.Abs(drawScale - 1f) > 0.001f)
                    graphic = graphic.GetCopy(graphic.drawSize * drawScale, null);

                Vector3 loc = DrawPos;
                loc.x += Mathf.Clamp(slot.offsetX, -2.5f, 2.5f);
                loc.z += Mathf.Clamp(slot.offsetZ, -2.5f, 2.5f);
                loc.y = AltitudeLayer.BuildingOnTop.AltitudeFor() + Mathf.Clamp(slot.height, 0f, 1.5f) + i * 0.002f;
                graphic.Draw(loc, Rot4.South, thing, slot.rotation);
            }
        }
    }
}
