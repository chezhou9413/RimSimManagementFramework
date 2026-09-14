using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Rendering
{
    //提供线路共用的渲染插值时钟，职责是平滑显示已完成的运输步而不提前移动实际物品。
    public sealed class ConveyorRenderClock
    {
        private int tick = -1;
        private int frame = -1;
        private float tickTime;
        private float fraction = 1f;

        //登记真实运输步，职责是让餐盘和带面从同一时间点开始插值。
        public void NotifyTick()
        {
            tick = Find.TickManager.TicksGame;
            tickTime = Time.realtimeSinceStartup;
            frame = -1;
        }

        //取得当前渲染帧的步内进度，职责是让同线所有格共用结果，并在暂停或断电后停在实物位置。
        public float Fraction()
        {
            if (Find.TickManager.Paused || tick != Find.TickManager.TicksGame) return 1f;
            if (frame != RealTime.frameCount)
            {
                fraction = Mathf.Clamp01((Time.realtimeSinceStartup - tickTime) * 60f * Find.TickManager.TickRateMultiplier);
                frame = RealTime.frameCount;
            }
            return fraction;
        }
    }
}
