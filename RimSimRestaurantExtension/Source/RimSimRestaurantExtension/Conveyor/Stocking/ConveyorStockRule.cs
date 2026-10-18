using SimManagementLib.Tool;
using System;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Models;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.Stocking
{
    //保存线路单项上架配置，职责是区分制作菜谱、指定食品柜和后厨现货。
    public sealed class ConveyorStockRule : IExposable
    {
        public string id = Guid.NewGuid().ToString("N");
        public RestaurantMenuItem menu;
        public ThingDef food;
        public Building_RestaurantStorage cabinet;
        public bool enabled = true;
        public int portions = 1, target = 1;
        public float price = 1f;
        public ThingDef Food => menu?.MealDef ?? food;
        public string Label => menu?.DisplayLabel ?? food?.LabelCap.RawText ?? SimTranslation.T("RSR.Food.InvalidFood");

        //复制线路规则，职责是让拆分线路保留来源但独立调整数量和售价。
        public ConveyorStockRule Clone() => new ConveyorStockRule
        { id = id, menu = menu?.Clone(), food = food, cabinet = cabinet, enabled = enabled,
            portions = portions, target = target, price = price };

        //持久化规则及食品柜引用，职责是保留各线路独立菜单。
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Deep.Look(ref menu, "menu");
            Scribe_Defs.Look(ref food, "food");
            Scribe_References.Look(ref cabinet, "cabinet");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref portions, "portions", 1);
            Scribe_Values.Look(ref target, "target", 1);
            Scribe_Values.Look(ref price, "price", 1f);
        }
    }
}

