using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Debug
{
    //为调试商圈登记旅馆、配置真实商品，并接入正常店员岗位。
    public static class HotelDistrictBusinessUtility
    {
        private const string FoodCategory = "Goods_Foods";
        private const string ApparelCategory = "Goods_ApparelBasic";
        private static readonly string[] ApparelNames = { "Apparel_BasicShirt", "Apparel_Pants", "Apparel_Parka" };

        //建造前检查经营所需定义和当前商品目录，不调整玩家客源配置。
        public static bool TryValidate(out string reason)
        {
            reason = "";
            if (DefDatabase<WorkTypeDef>.GetNamedSilentFail("Sim_ServiceWork") == null
                || DefDatabase<WorkGiverDef>.GetNamedSilentFail("Sim_WorkGiver_ManCashRegister") == null
                || DefDatabase<JobDef>.GetNamedSilentFail("Sim_ManCashRegister") == null
                || DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("RSH_Role_Reception") == null
                || DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("SimShopRole_Cashier") == null)
            {
                reason = HotelText.T("RSH.Text.HotelReceptionOrCashierDefinitionsAreMissing");
                return false;
            }
            GoodsCatalog.EnsureInitialized();
            foreach (string name in ApparelNames.Concat(new[] { "MealSurvivalPack" }))
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                string category = name == "MealSurvivalPack" ? FoodCategory : ApparelCategory;
                if (def != null && GoodsCatalog.Contains(category, def)) continue;
                reason = HotelText.T("RSH.Text.ARequiredProductIsMissingFromTheCurrent") + name;
                return false;
            }
            return true;
        }

        //登记实际出租床，沿用中央默认房价和接待时段。
        public static void ConfigureHotel(Zone_Shop shop, IEnumerable<Building_Bed> beds)
        {
            HotelMapComponent manager = HotelMapComponent.For(shop.Map);
            var settings = manager.SettingsFor(shop);
            settings.enabled = settings.spaEnabled = settings.acceptRenewals = true;
            foreach (Building_Bed bed in beds)
            {
                if (!manager.RegisterBed(shop, bed, true, out string reason)) throw new InvalidOperationException(reason);
            }
            OpenShop(shop);
        }

        //食品货柜按件补满，服饰货柜逐件上架普通品质的真实衣物。
        public static void ConfigureStore(Zone_Shop shop, IEnumerable<Building_SimContainer> storages, bool food)
        {
            string category = food ? FoodCategory : ApparelCategory;
            List<ThingDef> goods = (food ? new[] { "MealSurvivalPack" } : ApparelNames)
                .Select(name => DefDatabase<ThingDef>.GetNamed(name)).ToList();
            foreach (Building_SimContainer storage in storages)
            {
                if (storage == null || !storage.Spawned || storage.Map != shop.Map || !shop.ContainsCell(storage.Position))
                    throw new InvalidOperationException(HotelText.T("RSH.Text.AProductShelfIsOutsideItsShopZone"));
                ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                if (comp == null || !comp.AllowsGoodsCategory(category) || goods.Any(def => !comp.AllowsThingDef(def)))
                    throw new InvalidOperationException(HotelText.T("RSH.Text.TheShelfCannotSellTheRequiredProducts"));
                int capacity = storage is Building_UniqueGoodsContainer unique ? unique.UniqueSlotCount : storage.MaxTotalCapacity;
                var items = new Dictionary<string, GoodsItemData>();
                for (int i = 0; i < goods.Count; i++)
                {
                    int count = capacity / goods.Count + (i < capacity % goods.Count ? 1 : 0);
                    items[goods[i].defName] = new GoodsItemData { enabled = true, count = count,
                        restockThreshold = count, price = Mathf.Max(1f, Mathf.Round(goods[i].BaseMarketValue)) };
                }
                comp.ApplySettings(category, items);
                if (storage is Building_UniqueGoodsContainer clothes) FillClothes(clothes, goods);
                else if (ShopStorageUtility.FillConfiguredTargets(storage) <= 0)
                    throw new InvalidOperationException(HotelText.T("RSH.Text.NoPhysicalFoodStockCouldBeAdded"));
            }
            OpenShop(shop);
        }

        //创建一名有正常作息的员工，长期分配与首次上岗由同一入口处理。
        public static Pawn SpawnStaff(Map map, Zone_Shop shop, Building_CashRegister register,
            Building_Bed staffBed, bool reception)
            => HotelDistrictStaffUtility.Spawn(map, shop, register, staffBed, reception);

        //全天开启店铺开关，实际接待仍取决于值班人员与旅馆接待时段。
        private static void OpenShop(Zone_Shop shop)
        {
            var schedule = new ShopScheduleData { manualOpen = true, useSchedule = false };
            schedule.SetAllHours(true);
            shop.ApplySchedule(schedule);
            ShopDataUtility.NotifyBuildingChanged(shop.Map, shop.Cells[0]);
        }

        //专业服饰货柜按真实槽位收货，价格由框架读取该件衣物的正常市价。
        private static void FillClothes(Building_UniqueGoodsContainer storage, IReadOnlyList<ThingDef> goods)
        {
            for (int i = 0; i < storage.UniqueSlotCount; i++)
            {
                ThingDef def = goods[i % goods.Count];
                Thing clothing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                clothing.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                if (storage.TryStoreDirectly(clothing)) continue;
                clothing.Destroy(DestroyMode.Vanish);
                throw new InvalidOperationException(HotelText.T("RSH.Text.ClothingCouldNotBeStockedInAShelf") + def.defName);
            }
        }
    }
}
