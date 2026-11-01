using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using Verse;
using RimWorld;

namespace RimSimHotelExtension.GameComp
{
    //管理地图上的旅馆合同、登记床位和按晚释放的房费。
    public sealed partial class HotelMapComponent : MapComponent
    {
        private List<HotelShopSettings> settings = new List<HotelShopSettings>();
        private List<HotelStay> stays = new List<HotelStay>();
        private int nextId = 1;
        private readonly HashSet<int> arrivalBeds = new HashSet<int>();
        public IReadOnlyList<HotelStay> Stays => stays;
        public List<HotelMoneyRecord> MoneyRecords = new List<HotelMoneyRecord>();

        //由地图构造持久化的旅馆管理器。
        public HotelMapComponent(Map map) : base(map) { }

        //取得地图对应的旅馆管理器。
        public static HotelMapComponent For(Map map) => map?.GetComponent<HotelMapComponent>();

        //取得商店的旅馆设置，首次访问使用统一调参默认值。
        public HotelShopSettings SettingsFor(Zone_Shop shop)
        {
            HotelShopSettings result = settings.FirstOrDefault(s => s.shopZoneId == shop.ID);
            if (result != null) { RefreshMetrics(result); return result; }
            HotelTuningDef t = HotelTuningDef.Current;
            result = new HotelShopSettings { shopZoneId = shop.ID, singlePrice = t.singlePrice,
                doublePrice = t.doublePrice, woodSpaPrice = t.woodSpaPrice, stoneSpaPrice = t.stoneSpaPrice,
                atmFee = t.atmFee, atmDailyLimit = t.atmDailyLimit };
            settings.Add(result);
            RefreshMetrics(result);
            return result;
        }

        //按顾客查找尚未结束的入住记录。
        public HotelStay FindStay(Pawn pawn) => stays.FirstOrDefault(s => !s.completed && s.guests.Any(g => g.pawn == pawn));

        //按顾客查找个人余额和活动进度。
        public HotelGuestState GuestFor(Pawn pawn) => FindStay(pawn)?.guests.FirstOrDefault(g => g.pawn == pawn);

        //按区域编号找回当前商店，区域被移除时返回空。
        public Zone_Shop ShopFor(HotelStay stay) => map.zoneManager.AllZones.OfType<Zone_Shop>().FirstOrDefault(z => z.ID == stay.shopZoneId);

        //返回本旅馆的前台，独立取款机不占用前台岗位。
        public Building_HotelDesk DeskFor(Zone_Shop shop) => map.listerBuildings.allBuildingsColonist
            .OfType<Building_HotelDesk>().FirstOrDefault(b => b.Spawned && shop != null && shop.Cells.Contains(b.Position));

        //登记床位时确保一张床仅属于一家旅馆。
        public void RegisterBed(Zone_Shop shop, Building_HotelBed bed, bool register)
        {
            if (shop == null || bed == null || bed.Map != map || (register && !shop.Cells.Contains(bed.Position))) return;
            if (stays.Any(s => !s.completed && s.bed == bed)) return;
            foreach (HotelShopSettings item in settings) item.registeredBeds.Remove(bed);
            if (register) SettingsFor(shop).registeredBeds.Add(bed);
        }

        //返回已登记且没有入住和预订占用的床位。
        public IEnumerable<Building_HotelBed> AvailableBeds(Zone_Shop shop)
        {
            return SettingsFor(shop).registeredBeds.Where(b => b != null && b.Spawned && b.Map == map
                && shop.Cells.Contains(b.Position) && !b.Medical && !b.ForPrisoners && !b.ForSlaves && !b.IsBurning()
                && b.OwnersForReading.Count == 0 && !arrivalBeds.Contains(b.thingIDNumber)
                && !stays.Any(s => !s.completed && s.bed == b));
        }

        //提供经营面板和到客校验共同使用的阻塞原因。
        public string GetIssue(Zone_Shop shop)
        {
            if (shop == null) return HotelText.T("RSH.Text.ShopAreaWasRemoved");
            if (!SettingsFor(shop).enabled) return HotelText.T("RSH.Text.HotelReceptionIsPaused");
            HotelTuningDef tuning = HotelTuningDef.Current;
            int hour = GenLocalDate.HourOfDay(map);
            if (hour < tuning.receptionOpenHour || hour >= tuning.receptionCloseHour)
                return HotelText.T("RSH.Text.ReceptionHours", tuning.receptionOpenHour.Named("OPEN"), tuning.receptionCloseHour.Named("CLOSE"));
            Building_HotelDesk desk = DeskFor(shop);
            if (desk == null) return HotelText.T("RSH.Text.NoHotelReceptionDesk");
            //允许先建立待接待合同，由服务台需求触发员工补位；实际接待仍等待员工到岗。
            if (!AvailableBeds(shop).Any()) return HotelText.T("RSH.Text.NoVacantRegisteredBeds");
            return "";
        }

        //保存全部合同和设置，临时同步到客预留不跨游戏帧。
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref settings, "hotelSettings", LookMode.Deep);
            Scribe_Collections.Look(ref stays, "hotelStays", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextHotelStayId", 1);
            Scribe_Collections.Look(ref MoneyRecords, "hotelMoneyRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                settings ??= new List<HotelShopSettings>();
                stays ??= new List<HotelStay>();
                MoneyRecords ??= new List<HotelMoneyRecord>();
            }
        }

        //只保留最近的交易明细，长期旅馆不会无限增加存档体积。
        public void AddMoneyRecord(HotelMoneyRecord record)
        {
            MoneyRecords.Add(record);
            if (MoneyRecords.Count > 200) MoneyRecords.RemoveAt(0);
        }

        //按日重置统计口径，交易明细截断不会影响今日经营总数。
        private static void RefreshMetrics(HotelShopSettings config)
        {
            int day = GenDate.DaysPassed;
            if (config.metricsDay == day) return;
            config.metricsDay = day;
            config.arrivalsToday = config.departuresToday = config.earnedRoomRevenueToday = 0;
        }

        //按低频率巡检合同，处理入离店期限和每晚房费。
        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 150 != 0) return;
            foreach (HotelStay stay in stays.ToList())
                if (!stay.completed) TickStay(stay);
            stays.RemoveAll(s => s.completed && Find.TickManager.TicksGame - s.closingTick > 60000);
        }
    }
}
