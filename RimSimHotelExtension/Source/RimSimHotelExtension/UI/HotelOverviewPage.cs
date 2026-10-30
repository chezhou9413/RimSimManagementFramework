using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //按旅馆汇总空床、合同预付款和当天房费，提供直接管理入口。
    public sealed class HotelOverviewPage : BusinessManagerPageWorker
    {
        //有旅馆设施或未结束的住宿时才显示分类，暂停接待不隐藏入口。
        public override bool CanShow(ShopUiContext context) => base.CanShow(context)
            && context is BusinessManagerUiContext business && business.GetAllShops().Any(IsHotelShop);

        //按实际设施和合同识别旅馆，不在可见性查询中创建默认经营设置。
        private static bool IsHotelShop(Zone_Shop shop)
        {
            var manager = HotelMapComponent.For(shop.Map);
            return manager.DeskFor(shop) != null
                || manager.Stays.Any(stay => stay.shopZoneId == shop.ID && !stay.completed)
                || shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelBed>()
                    .Any(bed => shop.ContainsCell(bed.Position));
        }

        //总览只有即时跳转操作，不产生需要保存的配置。
        public override bool ShowSaveButton(ShopUiContext context) => false;

        //从合同与资金记录计算可核对的当前经营状态。
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            using var scope = new HotelUi();
            PageScroll state = context.GetOrCreatePageState("RSH.Overview", () => new PageScroll());
            HotelUi.Scroll(rect, state, width => {
                float y = 0;
                HotelUi.Heading(width, ref y, HotelText.T("RSH.Text.HotelOperations"),
                    HotelText.T("RSH.Text.MonitorOccupancyRoomRevenueAndOperatingIssuesAcross"));
                int count = 0;
                foreach (var shop in context.GetAllShops().Where(IsHotelShop).Where(s => HotelUi.Matches(context.SearchText, s.label)))
                {
                    HotelMapComponent manager = HotelMapComponent.For(shop.Map);
                    var settings = manager.SettingsFor(shop);
                    count++;
                    var stays = manager.Stays.Where(s => s.shopZoneId == shop.ID).ToList();
                    var current = stays.Where(s => !s.completed).ToList();
                    int beds = settings.registeredBeds.Count(b => b != null && b.Spawned);
                    int occupied = current.Count(s => s.active);
                    int guests = current.Where(s => s.active).Sum(s => s.guests.Count);
                    HotelUi.Section(width, ref y, shop.label);
                    HotelUi.Status(width, ref y, settings.enabled ? HotelText.T("RSH.Text.AdmissionsOpen") : HotelText.T("RSH.Text.AdmissionsPaused"), settings.enabled);
                    HotelUi.Metrics(width, ref y,
                        new[] { HotelText.T("RSH.Text.VacantRegisteredBeds"), HotelText.T("RSH.Text.GuestsStaying"),
                            HotelText.T("RSH.Text.Occupancy"), HotelText.T("RSH.Text.HeldPrepayments") },
                        $"{manager.AvailableBeds(shop).Count()} / {beds}", guests.ToString(),
                        $"{(beds == 0 ? 0 : occupied * 100f / beds):0}%", current.Sum(s => s.escrow).ToString());
                    HotelUi.Value(width, ref y, HotelText.T("RSH.Text.TodaySArrivalsDepartures"), $"{settings.arrivalsToday} / {settings.departuresToday}");
                    HotelUi.Value(width, ref y, HotelText.T("RSH.Text.RoomRevenueToday"), settings.earnedRoomRevenueToday.ToString());
                    string issue = manager.GetIssue(shop);
                    if (!issue.NullOrEmpty()) HotelUi.Notice(width, ref y, issue, true);
                    HotelUi.Actions(width, ref y, HotelText.T("RSH.Text.ManageHotel"), () => context.OpenShop(shop),
                        HotelText.T("RSH.Text.LocateHotel"), () => context.JumpToShop(shop));
                    y += 12f;
                }
                if (count == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingHotelsCreateAShopZoneBuild"));
                state.height = y + 8f;
            });
        }
    }
}
