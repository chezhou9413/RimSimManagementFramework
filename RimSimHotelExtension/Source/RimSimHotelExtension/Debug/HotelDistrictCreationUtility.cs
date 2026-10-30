using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using Verse;

namespace RimSimHotelExtension.Debug
{
    //一次配置能实际经营的样板商圈，入住和购物仍由原有业务流程处理。
    public static partial class HotelDistrictCreationUtility
    {
        private const int Width = 37;
        private const int Height = 31;
        private static readonly string[] BuildingNames =
        {
            "Wall", "Door", "Bed", "StandingLamp", "Heater", "Table1x2c", "DiningChair",
            "PowerConduit", "VanometricPowerCell", "RSH_ServiceDesk", "RSH_Atm", "RSH_Locker",
            "RSH_SingleBed", "RSH_DoubleBed", "RSH_WoodSpa", "RSH_StoneSpa",
            "Sim_CashRegister", "BigStorageBox", "ClothesStorageBox"
        };

        //以点击位置为中心生成完整设施，并返回旅馆区供开发工具继续定位。
        public static bool TryCreateHotelDistrict(Map map, IntVec3 center, out Zone_Shop hotel, out string result)
        {
            hotel = null;
            result = "";
            if (!HotelDistrictRestaurantUtility.TryResolve(out var validateRestaurant, out var createRestaurant, out result)) return false;
            int width = createRestaurant == null ? Width : Width + HotelDistrictRestaurantUtility.Size + 2;
            IntVec3 origin = center - new IntVec3(width / 2, 0, Height / 2);
            CellRect district = Rect(origin, 0, 0, width, Height);
            IntVec3 restaurantCenter = At(origin, Width + 1 + HotelDistrictRestaurantUtility.Size / 2, 14);
            if (!CanCreate(map, district, out result) || !HotelDistrictBusinessUtility.TryValidate(out result)) return false;
            if (validateRestaurant != null && !validateRestaurant(map, restaurantCenter, out result))
            {
                result = HotelText.T("RSH.Debug.RestaurantPreflightFailed", result.Named("REASON"));
                return false;
            }

            PrepareGround(map, district);
            BuildRooms(map, origin);
            hotel = CreateShop(map, Rect(origin, 1, 9, 23, 19), HotelText.T("RSH.Text.DebugSpaHotel"));
            Zone_Shop food = CreateShop(map, Rect(origin, 1, 1, 10, 5), HotelText.T("RSH.Text.DebugFoodStore"));
            Zone_Shop apparel = CreateShop(map, Rect(origin, 14, 1, 10, 5), HotelText.T("RSH.Text.DebugClothingStore"));

            var desk = (Building_HotelDesk)Spawn(map, origin, "RSH_ServiceDesk", 6, 11, Rot4.South);
            Spawn(map, origin, "RSH_Atm", 20, 11);
            Spawn(map, origin, "RSH_WoodSpa", 5, 18);
            Spawn(map, origin, "RSH_StoneSpa", 17, 18);
            for (int i = 0; i < 5; i++)
            {
                Spawn(map, origin, "RSH_Locker", 2 + 2 * i, 22);
                Spawn(map, origin, "RSH_Locker", 14 + 2 * i, 22);
            }

            var guestBeds = new List<Building_HotelBed>();
            for (int i = 0; i < 4; i++)
            {
                guestBeds.Add((Building_HotelBed)Spawn(map, origin, i < 2 ? "RSH_SingleBed" : "RSH_DoubleBed", 3 + i * 6, 26));
                Spawn(map, origin, "StandingLamp", 1 + i * 6, 26);
                Spawn(map, origin, "Heater", 5 + i * 6, 24);
            }
            HotelDistrictBusinessUtility.ConfigureHotel(hotel, guestBeds);
            Building_CashRegister foodRegister = BuildStore(map, origin, 0, true, food);
            Building_CashRegister apparelRegister = BuildStore(map, origin, 13, false, apparel);
            Building_Bed[] staffBeds = BuildStaffRoom(map, origin);
            BuildCommonFurniture(map, origin);
            BuildPower(map, origin, district);

            HotelDistrictBusinessUtility.SpawnStaff(map, hotel, desk, staffBeds[0], true);
            HotelDistrictBusinessUtility.SpawnStaff(map, food, foodRegister, staffBeds[1], false);
            HotelDistrictBusinessUtility.SpawnStaff(map, apparel, apparelRegister, staffBeds[2], false);
            foreach (Zone_Shop shop in new[] { hotel, food, apparel })
            {
                shop.InvalidateShopRuntimeCache();
                if (!shop.IsValidShop())
                {
                    result = HotelText.T("RSH.Text.FacilitiesWereCreatedButAShopIsInvalid")
                        + shop.label + " — " + shop.GetValidationMessage();
                    return false;
                }
            }
            HotelTuningDef tuning = HotelTuningDef.Current;
            result = HotelText.T("RSH.Text.4GuestRooms2SingleAnd2Double");
            result += HotelText.T("RSH.Text.ReceptionHours", tuning.receptionOpenHour.Named("OPEN"), tuning.receptionCloseHour.Named("CLOSE"));
            if (createRestaurant != null)
            {
                if (!createRestaurant(map, restaurantCenter, out Zone_Shop restaurant, out string restaurantResult))
                {
                    result = HotelText.T("RSH.Debug.RestaurantCreationFailed", restaurantResult.Named("REASON"));
                    return false;
                }
                restaurant.label = HotelText.T("RSH.Debug.RestaurantName");
                result += "\n" + HotelText.T("RSH.Debug.RestaurantCreated");
            }
            return true;
        }

        //在任何修改前检查整块用地、定义和电力事件，已有物品或建筑不会被清除。
        private static bool CanCreate(Map map, CellRect district, out string reason)
        {
            reason = "";
            if (map == null || !district.FullyContainedWithin(CellRect.WholeMap(map)))
                reason = HotelText.T("RSH.Debug.RequiredArea", district.Width.Named("WIDTH"), district.Height.Named("HEIGHT"));
            else if (map.gameConditionManager.ElectricityDisabled(map))
                reason = HotelText.T("RSH.Text.ElectricityIsDisabledByACurrentGameCondition");
            if (!reason.NullOrEmpty()) return false;
            foreach (string name in BuildingNames)
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) == null)
                {
                    reason = HotelText.T("RSH.Text.MissingBuildingDefinition") + name;
                    return false;
                }
            foreach (IntVec3 cell in district)
            {
                if (cell.Fogged(map) || map.zoneManager.ZoneAt(cell) != null
                    || !cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy))
                {
                    reason = HotelText.T("RSH.Text.TheSiteMustBeUnfoggedUnzonedAndSupport") + cell;
                    return false;
                }
                Thing conflict = map.thingGrid.ThingsListAt(cell).FirstOrDefault(t => !(t is Plant) && !(t is Filth)
                    && t.def.category != ThingCategory.Mote && t.def.category != ThingCategory.Ethereal);
                if (conflict == null) continue;
                reason = HotelText.T("RSH.Text.SiteOccupied") + conflict.LabelCap + " " + cell;
                return false;
            }
            return true;
        }

        //生成两个货柜并使用真实商品配置填充，顾客沿用普通购买和收银。
        private static Building_CashRegister BuildStore(Map map, IntVec3 origin, int xOffset, bool food, Zone_Shop shop)
        {
            var register = (Building_CashRegister)Spawn(map, origin, "Sim_CashRegister", xOffset + 5, 2, Rot4.South);
            string storageName = food ? "BigStorageBox" : "ClothesStorageBox";
            var storages = new[]
            {
                (Building_SimContainer)Spawn(map, origin, storageName, xOffset + 2, 4),
                (Building_SimContainer)Spawn(map, origin, storageName, xOffset + 8, 4)
            };
            Spawn(map, origin, "StandingLamp", xOffset + 5, 5);
            Spawn(map, origin, "Heater", xOffset + 10, 1);
            HotelDistrictBusinessUtility.ConfigureStore(shop, storages, food);
            return register;
        }

        //让员工有自己的床和口粮，不占用已登记出租床或货柜中出售的库存。
        private static Building_Bed[] BuildStaffRoom(Map map, IntVec3 origin)
        {
            var beds = new Building_Bed[3];
            for (int i = 0; i < beds.Length; i++) beds[i] = (Building_Bed)Spawn(map, origin, "Bed", 29 + i * 2, 6);
            Spawn(map, origin, "StandingLamp", 28, 4);
            Spawn(map, origin, "Heater", 34, 4);
            Spawn(map, origin, "Table1x2c", 32, 2);
            Spawn(map, origin, "DiningChair", 31, 2, Rot4.East);
            Spawn(map, origin, "DiningChair", 33, 2, Rot4.West);
            for (int i = 0; i < 9; i++)
            {
                Thing meals = ThingMaker.MakeThing(ThingDefOf.MealSurvivalPack);
                meals.stackCount = meals.def.stackLimit;
                GenSpawn.Spawn(meals, At(origin, 28 + i % 3, 1 + i / 3), map);
            }
            return beds;
        }
    }
}
