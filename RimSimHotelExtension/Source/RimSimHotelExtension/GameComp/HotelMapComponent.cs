using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Settings;
using RimSimHotelExtension.Spa;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.SimAI;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;
using RimWorld;

namespace RimSimHotelExtension.GameComp
{
    //管理地图上的旅馆合同、登记床位和按晚释放的房费。
    public sealed partial class HotelMapComponent : MapComponent
    {
        private List<HotelShopSettings> settings = new List<HotelShopSettings>();
        private List<HotelStay> stays = new List<HotelStay>();
        private readonly List<HotelStay> activeStays = new List<HotelStay>();
        private int nextId = 1;
        private readonly HashSet<int> arrivalBeds = new HashSet<int>();
        private readonly HashSet<int> registeredBedIds = new HashSet<int>();
        private int lastReceptionOpenState = -1;
        public IReadOnlyList<HotelStay> Stays => stays;
        public IReadOnlyList<HotelStay> ActiveStays => activeStays;
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
                atmFee = t.atmFee, atmDailyLimit = t.atmDailyLimit,
                sleepStartHour = t.sleepStartHour, wakeHour = t.wakeHour };
            settings.Add(result);
            RefreshMetrics(result);
            return result;
        }

        //合同沿用创建时所属旅馆的设置，区域移除后也能完成收尾。
        internal HotelShopSettings SettingsFor(HotelStay stay) => settings.First(s => s.shopZoneId == stay.shopZoneId);

        //按区域编号找回当前商店，区域被移除时返回空。
        public Zone_Shop ShopFor(HotelStay stay) => map.zoneManager.AllZones.OfType<Zone_Shop>().FirstOrDefault(z => z.ID == stay.shopZoneId);

        //返回本旅馆的一个有效前台，优先有人值守的服务台；独立取款机不占用前台岗位。
        public Building_HotelDesk DeskFor(Zone_Shop shop)
        {
            if (shop == null) return null;
            Building_HotelDesk fallback = null;
            var registers = ShopDataUtility.GetCashRegisterSnapshotInZone(shop);
            for (int i = 0; i < registers.Count; i++)
            {
                if (!(registers[i] is Building_HotelDesk desk) || !desk.Spawned || desk.Destroyed
                    || desk.Faction != Faction.OfPlayer) continue;
                if (desk.IsManned) return desk;
                if (fallback == null) fallback = desk;
            }
            return fallback;
        }

        //返回指定合同当前关联的有效前台，用于对齐该前台的服务工时与超时判定。
        public Building_HotelDesk DeskForStay(HotelStay stay, Zone_Shop shop)
        {
            Building_HotelDesk desk = stay?.receptionDesk;
            if (desk != null && desk.Spawned && !desk.Destroyed && desk.Faction == Faction.OfPlayer
                && shop != null && shop.ContainsCell(desk.Position))
                return desk;
            return DeskFor(shop);
        }

        //为顾客挑选当前最合适的可达前台，按有人值守优先、负载更轻优先、距离更近优先分流。
        public Building_HotelDesk BestDeskFor(Zone_Shop shop, Pawn customer, out IntVec3 serviceCell,
            bool requireManned = false, Danger maxDanger = Danger.None)
        {
            serviceCell = IntVec3.Invalid;
            if (shop == null || customer == null) return null;
            Building_HotelDesk best = null;
            IntVec3 bestCell = IntVec3.Invalid;
            int bestLoad = int.MaxValue;
            int bestDistance = int.MaxValue;
            var registers = ShopDataUtility.GetCashRegisterSnapshotInZone(shop);
            for (int i = 0; i < registers.Count; i++)
            {
                if (!(registers[i] is Building_HotelDesk desk) || !desk.Spawned || desk.Destroyed
                    || desk.Faction != Faction.OfPlayer) continue;
                if (requireManned && !desk.IsManned) continue;
                IntVec3 cell = CheckoutQueueCellUtility.FindServiceCell(desk, customer);
                if (!cell.IsValid || !customer.CanReach(cell, PathEndMode.OnCell, maxDanger)) continue;
                int load = DeskLoad(desk, customer);
                int distance = desk.Position.DistanceToSquared(customer.Position);
                if (IsBetterDesk(desk, load, distance, best, bestLoad, bestDistance))
                {
                    best = desk;
                    bestCell = cell;
                    bestLoad = load;
                    bestDistance = distance;
                }
            }
            serviceCell = bestCell;
            return best;
        }

        //统计单个前台当前排队、正在前往办理接待以及正在前往购票的顾客总数。
        private int DeskLoad(Building_HotelDesk desk, Pawn excludeCustomer)
        {
            int load = SimShopCheckoutApi.GetRegisterLoad(desk, excludeCustomer);
            var sessions = SpaMapComponent.For(map)?.Sessions;
            if (sessions != null)
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.IsTerminal || session.stage != SpaSessionStage.BuyingTicket
                        || session.ticketDesk != desk || session.customer == null || session.customer == excludeCustomer)
                        continue;
                    if (!SimShopCheckoutApi.IsQueuedForRegister(session.customer, desk))
                        load++;
                }
            }
            return load;
        }

        //比较候选前台优劣：优先有人值守，其次负载更轻，再看距离更近。
        private static bool IsBetterDesk(Building_HotelDesk candidate, int load, int distance,
            Building_HotelDesk best, int bestLoad, int bestDistance)
        {
            if (best == null) return true;
            if (candidate.IsManned != best.IsManned) return candidate.IsManned;
            if (load != bestLoad) return load < bestLoad;
            return distance < bestDistance;
        }

        //通知框架刷新该旅馆的营业校验、经营指标和到客匹配快照，不依赖店内存在货柜。
        public void NotifyShopChanged(Zone_Shop shop)
        {
            if (shop == null || shop.Map != map) return;
            shop.InvalidateShopRuntimeCache();
            map.GetComponent<CustomerArrivalManager>()?.NotifyShopDirty(shop);
            Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?.InvalidateShopMetrics(shop);
        }

        //返回已登记且没有入住和预订占用的床位。
        public IEnumerable<Building_Bed> AvailableBeds(Zone_Shop shop)
        {
            return SettingsFor(shop).registeredBeds.Where(b => SupportsRentalBed(b) && b.Spawned && b.Map == map
                && shop.ContainsCell(b.Position) && !b.Medical && !b.ForPrisoners && !b.ForSlaves && !b.IsBurning()
                && b.OwnersForReading.Count == 0 && !arrivalBeds.Contains(b.thingIDNumber)
                && !activeStays.Any(s => !s.completed && s.bed == b));
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
                RebuildGuestIndex();
                RebuildRegisteredBedIndex();
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

        //按低频率巡检合同，并在前台接待时段切换时主动刷新旅馆来客快照。
        public override void MapComponentTick()
        {
            RefreshRoomPrivacy();
            if (Find.TickManager.TicksGame % 150 != 0) return;
            CheckReceptionHoursTransition();
            activeStays.RemoveAll(stay => stay.completed);
            for (int i = 0, count = activeStays.Count; i < count; i++)
                if (!activeStays[i].completed) TickStay(activeStays[i]);
            if (stays.RemoveAll(s => s.completed && Find.TickManager.TicksGame - s.closingTick > 60000) > 0)
                roomPrivacyDirty = true;
        }

        //当地图时间跨入或跨出前台接待时段时，立即刷新所有旅馆的到客匹配快照。
        private void CheckReceptionHoursTransition()
        {
            HotelTuningDef tuning = HotelTuningDef.Current;
            int hour = GenLocalDate.HourOfDay(map);
            int openState = hour >= tuning.receptionOpenHour && hour < tuning.receptionCloseHour ? 1 : 0;
            if (lastReceptionOpenState == openState) return;
            lastReceptionOpenState = openState;
            var zones = map?.zoneManager?.AllZones;
            if (zones == null) return;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Shop shop && DeskFor(shop) != null)
                    NotifyShopChanged(shop);
            }
        }
    }
}
