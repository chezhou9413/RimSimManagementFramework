using System.Linq;
using LudeonTK;
using RimWorld;
using SimManagementLib.SimThingComp;
using Verse;

namespace SimManagementLib.Debug
{
    //用原版大小储物架生成可直接试卖、定价和补货的商店。
    public static partial class DebugActions_SimShop
    {
        //以点击位置为中心布置两座销售架、一座备货架和两名在岗店员。
        [DebugAction("SimShop", "生成原版货架测试商店（点选位置）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnStorageSalesShopAtCell()
        {
            Map map = Find.CurrentMap;
            IntVec3 center = UI.MouseCell();
            CellRect outer = CellRect.CenteredOn(center, 4);
            if (!CanPlaceShelfShop(map, outer))
            {
                Messages.Message("请选择一块 9×9 的可建造空地，避开已有区划、建筑、人物和物品，并留出南侧入口。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var registerDef = DefDatabase<ThingDef>.GetNamed("Sim_CashRegister");
            var shirtDef = DefDatabase<ThingDef>.GetNamed("Apparel_BasicShirt");
            var medicine = ThingDefOf.MedicineIndustrial;
            var meals = ThingDefOf.MealSurvivalPack;
            if (!IsSalesShelfDef(ThingDefOf.Shelf) || !IsSalesShelfDef(ThingDefOf.ShelfSmall))
            {
                Messages.Message("原版大小储物架需要储物格和货柜销售组件，请检查货架补丁。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var inner = outer.ContractedBy(1);
            ClearAreaForShop(map, outer);
            BuildRoomShell(map, outer, ThingDefOf.Wall, ThingDefOf.Door);
            SetFloorTerrain(map, inner, TerrainDefOf.PavedTile);
            SetConstructedRoof(map, inner);
            var register = SpawnRegister(map, registerDef, center + IntVec3.South);
            var groceries = SpawnShelfShopStorage(map, ThingDefOf.Shelf, center + IntVec3.West * 2 + IntVec3.North * 2, medicine, meals);
            var clothes = SpawnShelfShopStorage(map, ThingDefOf.ShelfSmall, center + IntVec3.East * 2 + IntVec3.North * 2, shirtDef);
            var reserve = SpawnShelfShopStorage(map, ThingDefOf.ShelfSmall, center + IntVec3.East * 2 + IntVec3.South * 2, medicine);
            if (register == null || groceries == null || clothes == null || reserve == null)
            {
                Messages.Message("测试店未能放下全部货架和收银台。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            //原版货架生成时会移除脚下的区划，先摆好货架再划商店。
            var shop = CreateShopZone(map, inner);
            shop.label = "原版货架测试商店";
            if (!PlaceShelfShopStock(groceries, medicine, 6) || !PlaceShelfShopStock(groceries, meals, 8)
                || !PlaceShelfShopStock(clothes, shirtDef, 1, QualityCategory.Normal)
                || !PlaceShelfShopStock(clothes, shirtDef, 1, QualityCategory.Excellent)
                || !PlaceShelfShopStock(reserve, medicine, 20)) return;

            groceries.ApplyRule(medicine, new GoodsItemData { enabled = true, price = 15f, count = 20, restockThreshold = 8 });
            groceries.ToggleSales();
            clothes.ToggleSales();

            var cashier = SpawnScenarioStaff(map, register.InteractionCell, "SimShopRole_Cashier", shop);
            if (cashier == null) return;
            var restocker = SpawnScenarioStaff(map, center + IntVec3.West * 2 + IntVec3.South * 2, "SimShopRole_Restocker", shop);
            if (restocker == null) return;
            ConfigureRestockStaffWork(restocker, DefDatabase<WorkTypeDef>.GetNamed("Restocking"), WorkTypeDefOf.Hauling);
            if (!StartScenarioCashierJob(cashier, register))
            {
                Messages.Message("测试店与两名店员已生成，收银员暂时无法在收银台开始值班。", register, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(groceries.Host);
            Messages.Message("原版货架测试商店已生成。大架出售药品和口粮，小架出售两种品质的衬衫。补货员可从备货架将药品从 6 件补到 20 件。",
                groceries.Host, MessageTypeDefOf.TaskCompletion, false);
        }

        //保留现有区划和财物，只清理所选空地的植物和污渍。
        private static bool CanPlaceShelfShop(Map map, CellRect outer)
        {
            if (map == null || !outer.InBounds(map)) return false;
            var entrance = outer.GetCenterCellOnEdge(Rot4.South) + IntVec3.South;
            return entrance.InBounds(map) && entrance.Walkable(map) && outer.Cells.All(cell =>
                !cell.Fogged(map) && cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy)
                && map.zoneManager.ZoneAt(cell) == null && cell.GetThingList(map).All(thing =>
                    thing.def.category == ThingCategory.Plant || thing.def.category == ThingCategory.Filth));
        }

        //在改动地图前确认两个原版定义都能提供货架库存。
        private static bool IsSalesShelfDef(ThingDef def) => typeof(ISlotGroupParent).IsAssignableFrom(def.thingClass)
            && def.GetCompProperties<CompProperties_StorageSales>() != null;

        //按用途设置原版储存筛选，备货架和销售架使用相同搬运优先级。
        private static ThingComp_StorageSales SpawnShelfShopStorage(Map map, ThingDef def, IntVec3 cell, params ThingDef[] goods)
        {
            var shelf = SpawnFurnitureForShop(map, def, cell, Rot4.North) as Building;
            if (shelf is not ISlotGroupParent slots) return null;
            var settings = slots.GetStoreSettings();
            settings.Priority = StoragePriority.Preferred;
            settings.filter.SetDisallowAll();
            foreach (var item in goods) settings.filter.SetAllow(item, true);
            return shelf.GetComp<ThingComp_StorageSales>();
        }

        //把测试商品放进有效的原版储物格，两件衬衫只改变品质。
        private static bool PlaceShelfShopStock(ThingComp_StorageSales sales, ThingDef def, int count, QualityCategory? quality = null)
        {
            var thing = MakeThingWithDefaultStuff(def);
            thing.stackCount = count;
            if (quality.HasValue) thing.TryGetComp<CompQuality>().SetQuality(quality.Value, ArtGenerationContext.Colony);
            var slots = ((ISlotGroupParent)sales.Host).GetSlotGroup();
            foreach (var cell in slots.CellsList)
                if (cell.IsValidStorageFor(sales.Host.Map, thing) && GenPlace.TryPlaceThing(thing, cell, sales.Host.Map, ThingPlaceMode.Direct))
                    return true;

            thing.Destroy();
            Messages.Message($"测试货架无法存放{def.LabelCap}，请检查宿主的储存筛选和容量。", sales.Host, MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
