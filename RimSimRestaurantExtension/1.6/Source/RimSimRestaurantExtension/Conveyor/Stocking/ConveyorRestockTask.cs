using RimSimRestaurantExtension.Conveyor.Transport;
using RimSimRestaurantExtension.Models;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Stocking
{
    //保存单盘补餐任务，职责是独立记录制作需求、容量占用和搬运阶段。
    public sealed class ConveyorRestockTask : RestaurantProductionRequest, IExposable
    {
        public string key, ruleId;
        public int shopId;
        public Thing stove;
        public Thing cleaningFood;
        public Building_SushiConveyor destination;
        public float cost;
        public bool produced, cleaning, finished;

        //保存任务状态，职责是从原版Job恢复搬运和制作而不重新生成餐品。
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref ruleId, "ruleId");
            Scribe_Values.Look(ref shopId, "shopId");
            Scribe_References.Look(ref stove, "stove");
            Scribe_References.Look(ref cleaningFood, "cleaningFood");
            Scribe_References.Look(ref destination, "destination");
            Scribe_Values.Look(ref cost, "cost");
            Scribe_Values.Look(ref produced, "produced");
            Scribe_Values.Look(ref cleaning, "cleaning");
            Scribe_Values.Look(ref finished, "finished");
            Scribe_Defs.Look(ref mealDef, "mealDef");
            Scribe_Values.Look(ref mealCount, "mealCount", 1);
            Scribe_Defs.Look(ref recipe, "recipe");
            Scribe_Collections.Look(ref ingredients, "ingredients", LookMode.Deep);
        }
    }
}
