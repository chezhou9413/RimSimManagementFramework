using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Spa;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimDialog;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //按旅馆汇总空床、入住率、前台值班、预付款和当天房费，提供直达管理与店员配置入口。
    public sealed class HotelOverviewPage : BusinessManagerPageWorker
    {
        private const int FacilityCountCacheTicks = 30;

        private sealed class OverviewState : PageScroll
        {
            public int cacheTick = -1;
            public int cacheMapId = -1;
            public readonly Dictionary<int, int> spaCounts = new Dictionary<int, int>();
            public readonly Dictionary<int, int> atmCounts = new Dictionary<int, int>();
        }

        //有旅馆设施或未结束的住宿时才显示分类，暂停接待不隐藏入口。
        public override bool CanShow(ShopUiContext context) => base.CanShow(context)
            && context is BusinessManagerUiContext business && business.GetAllShops().Any(IsHotelShop);

        //按实际设施和合同识别旅馆，不在可见性查询中创建默认经营设置或全图扫床。
        private static bool IsHotelShop(Zone_Shop shop)
        {
            var manager = HotelMapComponent.For(shop.Map);
            if (manager == null) return false;
            if (manager.DeskFor(shop) != null || manager.HasRegisteredBedInShop(shop)) return true;
            for (int i = 0; i < manager.ActiveStays.Count; i++)
                if (!manager.ActiveStays[i].completed && manager.ActiveStays[i].shopZoneId == shop.ID) return true;
            return false;
        }

        //总览只有即时跳转操作，不产生需要保存的配置。
        public override bool ShowSaveButton(ShopUiContext context) => false;

        //顶部展示全地图旅馆汇总，下方按卡片呈现每家旅馆的实时住店状态。
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            using var scope = new HotelUi();
            OverviewState state = context.GetOrCreatePageState("RSH.Overview", () => new OverviewState());
            List<Zone_Shop> allHotels = context.GetAllShops().Where(IsHotelShop).ToList();
            List<Zone_Shop> visibleHotels = allHotels.Where(s => HotelUi.Matches(context.SearchText, s.label)).ToList();
            RefreshFacilityCountsIfNeeded(state, allHotels);

            HotelUi.Scroll(rect, state, width =>
            {
                float y = 0f;
                HotelUi.Heading(width, ref y, HotelText.T("RSH.Text.HotelOperations"),
                    HotelText.T("RSH.Text.MonitorOccupancyRoomRevenueAndOperatingIssuesAcross"));

                if (allHotels.Count > 0)
                    DrawGlobalMetrics(width, ref y, allHotels);

                for (int i = 0; i < visibleHotels.Count; i++)
                    DrawHotelCard(width, ref y, visibleHotels[i], context, state);

                if (visibleHotels.Count == 0)
                    HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingHotelsCreateAShopZoneBuild"));
                state.height = y + 8f;
            });
        }

        //按短间隔一次统计每家旅馆内的温泉与取款机数量，避免 OnGUI 每帧重复全图枚举殖民地建筑。
        private static void RefreshFacilityCountsIfNeeded(OverviewState state, List<Zone_Shop> hotels)
        {
            Map map = hotels.Count > 0 ? hotels[0].Map : null;
            if (map == null) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (state.cacheMapId == map.uniqueID && state.cacheTick >= 0 && now - state.cacheTick < FacilityCountCacheTicks)
                return;
            state.cacheMapId = map.uniqueID;
            state.cacheTick = now;
            state.spaCounts.Clear();
            state.atmCounts.Clear();
            var buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building is Building_HotelSpa)
                {
                    if (map.zoneManager.ZoneAt(building.Position) is Zone_Shop shop)
                    {
                        state.spaCounts.TryGetValue(shop.ID, out int count);
                        state.spaCounts[shop.ID] = count + 1;
                    }
                }
                else if (building is Building_HotelAtm)
                {
                    if (map.zoneManager.ZoneAt(building.Position) is Zone_Shop shop)
                    {
                        state.atmCounts.TryGetValue(shop.ID, out int count);
                        state.atmCounts[shop.ID] = count + 1;
                    }
                }
            }
        }

        //绘制跨旅馆总览指标卡。
        private static void DrawGlobalMetrics(float width, ref float y, List<Zone_Shop> hotels)
        {
            int totalBeds = 0;
            int vacantBeds = 0;
            int activeGuests = 0;
            int todayRevenue = 0;
            int heldPrepay = 0;
            foreach (var shop in hotels)
            {
                var manager = HotelMapComponent.For(shop.Map);
                var settings = manager.SettingsFor(shop);
                totalBeds += settings.registeredBeds.Count(b => b != null && b.Spawned && shop.ContainsCell(b.Position));
                vacantBeds += manager.AvailableBeds(shop).Count();
                var current = manager.ActiveStays.Where(s => !s.completed && s.shopZoneId == shop.ID).ToList();
                activeGuests += current.Where(s => s.active).Sum(s => s.guests.Count);
                heldPrepay += current.Sum(s => s.escrow);
                todayRevenue += settings.earnedRoomRevenueToday;
            }
            HotelUi.Metrics(width, ref y,
                new[] { HotelText.T("RSH.Ui.TotalHotels"), HotelText.T("RSH.Text.VacantRegisteredBeds"),
                    HotelText.T("RSH.Text.GuestsStaying"), HotelText.T("RSH.Ui.RevenueAndPrepay") },
                hotels.Count.ToString(), $"{vacantBeds} / {totalBeds}", activeGuests.ToString(),
                $"{todayRevenue} / {heldPrepay}");
            y += 4f;
        }

        //绘制单家旅馆的卡片：标题状态、入住率条、四项核心指标、设施摘要与快捷操作。
        private static void DrawHotelCard(float width, ref float y, Zone_Shop shop, BusinessManagerUiContext context, OverviewState state)
        {
            HotelMapComponent manager = HotelMapComponent.For(shop.Map);
            var settings = manager.SettingsFor(shop);
            var desk = manager.DeskFor(shop);
            var current = manager.ActiveStays.Where(s => !s.completed && s.shopZoneId == shop.ID).ToList();
            int beds = settings.registeredBeds.Count(b => b != null && b.Spawned && shop.ContainsCell(b.Position));
            int vacant = manager.AvailableBeds(shop).Count();
            int occupied = current.Count(s => s.active);
            int pending = current.Count(s => !s.active || s.closing);
            var activeGuestStates = current.Where(s => s.active).SelectMany(s => s.guests).ToList();
            int guests = activeGuestStates.Count;
            float avgSat = guests > 0 ? activeGuestStates.Average(g => g.satisfaction) : 0f;
            state.spaCounts.TryGetValue(shop.ID, out int spas);
            state.atmCounts.TryGetValue(shop.ID, out int atms);
            string issue = manager.GetIssue(shop);

            float pad = 12f;
            float innerW = Mathf.Max(1f, width - pad * 2f);
            float cardStartY = y;
            float innerY = cardStartY + pad;
            float titleH = ShopUiVisualUtility.LineHeight(GameFont.Medium);
            float barH = ShopUiVisualUtility.LineHeight(GameFont.Tiny) + 8f;
            float lineH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float ctrlH = HotelUi.Line;

            //顶部：旅馆名称、状态徽章与操作按钮，窄窗口自动将按钮放到下一行避免重叠。
            string manageLabel = HotelText.T("RSH.Text.ManageHotel");
            string staffLabel = HotelText.T("RSH.Ui.StaffButton");
            string locateLabel = HotelText.T("RSH.Ui.Locate");
            float manageW = Mathf.Max(78f, Text.CalcSize(manageLabel).x + 20f);
            float staffW = Mathf.Max(64f, Text.CalcSize(staffLabel).x + 18f);
            float locateW = Mathf.Max(56f, Text.CalcSize(locateLabel).x + 18f);
            float buttonsW = manageW + staffW + locateW + 12f;

            string stateText = !settings.enabled ? HotelText.T("RSH.Text.AdmissionsPaused")
                : beds > 0 && vacant == 0 ? HotelText.T("RSH.Ui.FullHouse")
                : HotelText.T("RSH.Text.AdmissionsOpen");
            Color badgeFill = !settings.enabled ? HotelUi.WarningBadgeFill
                : beds > 0 && vacant == 0 ? HotelUi.InfoBadgeFill : HotelUi.PositiveBadgeFill;
            float badgeW = Mathf.Min(110f, Text.CalcSize(stateText).x + 20f);
            bool stackButtons = innerW < buttonsW + badgeW + 120f;

            float metricsH = EstimateMetricsHeight(innerW, 4);
            string deskText = desk == null ? HotelText.T("RSH.Text.NoHotelReceptionDesk")
                : desk.IsManned ? HotelText.T("RSH.Ui.DeskStaffedBy", desk.CurrentCashier.LabelShortCap.Named("STAFF"))
                : HotelText.T("RSH.Text.ReceptionIsUnstaffed");
            string facilityLine = HotelText.T("RSH.Ui.HotelFacilitySummary",
                deskText.Named("DESK"), $"{settings.sleepStartHour:00}:00–{settings.wakeHour:00}:00".Named("SLEEP"),
                spas.Named("SPAS"), atms.Named("ATMS"));
            float facilityH = Mathf.Max(lineH, Text.CalcHeight(facilityLine, innerW));
            float issueH = issue.NullOrEmpty() ? 0f : ShopUiVisualUtility.NoticeHeight(issue, innerW) + 8f;
            float headerH = Mathf.Max(titleH, ctrlH);
            float extraButtonsH = stackButtons ? ctrlH + 8f : 0f;
            float cardH = pad + headerH + extraButtonsH + 8f + barH + 10f + metricsH + 6f + facilityH + 8f + issueH + pad;

            ShopUiVisualUtility.DrawSection(new Rect(0f, cardStartY, width, cardH));

            float titleW = stackButtons
                ? Mathf.Max(1f, innerW - badgeW - 8f)
                : Mathf.Max(1f, innerW - buttonsW - badgeW - 16f);

            ShopUiVisualUtility.DrawCellLabel(new Rect(pad, innerY, titleW, headerH), shop.label, Color.white, GameFont.Medium);
            float badgeX = stackButtons ? width - pad - badgeW : pad + titleW + 6f;
            HotelUi.Badge(new Rect(badgeX, innerY + (headerH - ctrlH) * 0.5f, badgeW, ctrlH), stateText, badgeFill);

            float btnRowY = stackButtons ? innerY + headerH + 6f : innerY + (headerH - ctrlH) * 0.5f;
            if (stackButtons)
            {
                float bw = Mathf.Max(1f, (innerW - 12f) / 3f);
                if (HotelUi.Button(new Rect(pad, btnRowY, bw, ctrlH), manageLabel, primary: true))
                    context.OpenShop(shop);
                if (HotelUi.Button(new Rect(pad + bw + 6f, btnRowY, bw, ctrlH), staffLabel))
                    Find.WindowStack.Add(new Dialog_ShopStaffManager(shop));
                if (HotelUi.Button(new Rect(pad + (bw + 6f) * 2f, btnRowY, bw, ctrlH), locateLabel))
                    context.JumpToShop(shop);
            }
            else
            {
                float bx = width - pad - locateW;
                if (HotelUi.Button(new Rect(bx, btnRowY, locateW, ctrlH), locateLabel))
                    context.JumpToShop(shop);
                bx -= staffW + 6f;
                if (HotelUi.Button(new Rect(bx, btnRowY, staffW, ctrlH), staffLabel))
                    Find.WindowStack.Add(new Dialog_ShopStaffManager(shop));
                bx -= manageW + 6f;
                if (HotelUi.Button(new Rect(bx, btnRowY, manageW, ctrlH), manageLabel, primary: true))
                    context.OpenShop(shop);
            }

            innerY += headerH + extraButtonsH + 8f;

            //入住率进度条。
            float occupancy01 = beds <= 0 ? 0f : Mathf.Clamp01(occupied / (float)beds);
            string barLabel = HotelText.T("RSH.Ui.OccupancyBarLabel",
                occupied.Named("OCCUPIED"), beds.Named("TOTAL"), (occupancy01 * 100f).ToString("0").Named("PCT"),
                (guests > 0 ? avgSat.ToString("0") : "—").Named("SAT"));
            HotelUi.ProgressBar(new Rect(pad, innerY, innerW, barH), occupancy01, barLabel,
                guests > 0 ? HotelUi.SatisfactionColor(avgSat) : ShopUiVisualUtility.Accent);
            innerY += barH + 10f;

            //四项关键指标。
            string guestCountText = pending > 0 ? $"{guests} (+{pending})" : guests.ToString();
            float usedMetrics = ShopUiVisualUtility.DrawMetrics(new Rect(pad, innerY, innerW, metricsH),
                new[] { HotelText.T("RSH.Text.VacantRegisteredBeds"), HotelText.T("RSH.Text.GuestsStaying"),
                    HotelText.T("RSH.Text.TodaySArrivalsDepartures"), HotelText.T("RSH.Ui.RevenueAndPrepay") },
                new[] { $"{vacant} / {beds}", guestCountText,
                    $"{settings.arrivalsToday} / {settings.departuresToday}",
                    $"{settings.earnedRoomRevenueToday} / {current.Sum(s => s.escrow)}" },
                compact: false);
            innerY += usedMetrics + 8f;

            //前台、作息与配套设施摘要。
            Color oldColor = GUI.color;
            try
            {
                GUI.color = desk != null && desk.IsManned ? ShopUiVisualUtility.MutedText : ShopUiVisualUtility.WarningText;
                Widgets.Label(new Rect(pad, innerY, innerW, facilityH), facilityLine);
            }
            finally { GUI.color = oldColor; }
            innerY += facilityH + 8f;

            if (!issue.NullOrEmpty())
            {
                float nh = ShopUiVisualUtility.NoticeHeight(issue, innerW);
                ShopUiVisualUtility.DrawNotice(new Rect(pad, innerY, innerW, nh), issue, true);
            }

            y = cardStartY + cardH + 12f;
        }

        private static float EstimateMetricsHeight(float width, int count)
        {
            int cols = width < 600f ? Mathf.Min(2, count) : count;
            int rows = Mathf.CeilToInt(count / (float)Mathf.Max(1, cols));
            return rows * (ShopUiVisualUtility.MetricHeight + 8f) - 8f;
        }
    }
}
