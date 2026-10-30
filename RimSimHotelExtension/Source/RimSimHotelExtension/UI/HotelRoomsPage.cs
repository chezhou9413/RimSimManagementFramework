using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //客房首页展示床位清单，房价和单床登记在二级窗口编辑。
    public sealed class HotelRoomsPage : ShopManagerPageWorker
    {
        private const string Key = "RSH.RoomDraft";
        //床位与报价只属于当前窗口草稿。
        private sealed class Draft : PageScroll
        {
            public bool enabled, renewals;
            public int single, dual, sort;
            public HashSet<Building_HotelBed> beds;
        }

        //床位尚未登记时也保留配置入口。
        public override bool CanShow(ShopUiContext context) => base.CanShow(context) && HotelUi.HasHotel(context);

        //窗口草稿与实际经营设置分离。
        private static Draft State(ShopManagerUiContext context) => context.GetOrCreatePageState(Key, () => {
            var settings = HotelMapComponent.For(context.Shop.Map).SettingsFor(context.Shop);
            return new Draft { enabled = settings.enabled, renewals = settings.acceptRenewals,
                single = settings.singlePrice, dual = settings.doublePrice,
                beds = new HashSet<Building_HotelBed>(settings.registeredBeds) };
        });

        //首屏容纳摘要、操作栏和多张床，搜索与排序只改变显示顺序。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            Draft draft = State(context);
            var manager = HotelMapComponent.For(context.Shop.Map);
            var beds = context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelBed>()
                .Where(b => context.Shop.ContainsCell(b.Position)).ToList();
            var stays = manager.Stays.Where(s => !s.completed && s.shopZoneId == context.Shop.ID).ToList();
            var sorts = new[] { HotelText.T("RSH.Ui.SortName"), HotelText.T("RSH.Ui.UnregisteredFirst"), HotelText.T("RSH.Ui.VacantFirst") };
            var ordered = beds.OrderBy(b => draft.sort == 1 ? (draft.beds.Contains(b) ? 1 : 0)
                : draft.sort == 2 ? (stays.Any(s => s.bed == b) ? 1 : 0) : 0).ThenBy(b => b.LabelCap.ToString()).ThenBy(b => b.thingIDNumber);
            HotelUi.Scroll(rect, draft, width => {
                float y = 0;
                HotelUi.Summary(width, ref y, HotelText.T("RSH.Ui.RoomSummary", beds.Count.Named("TOTAL"),
                    beds.Count(draft.beds.Contains).Named("RENTED"), stays.Count.Named("OCCUPIED"),
                    HotelText.T(draft.enabled ? "RSH.Ui.Open" : "RSH.Ui.Paused").Named("STATE"), draft.single.Named("SINGLE"), draft.dual.Named("DOUBLE")));
                HotelUi.Actions(width, ref y, HotelText.T("RSH.Ui.ChangeRoomPrices"), () => OpenSettings(draft, manager, context),
                    sorts[draft.sort], () => HotelUi.SortMenu(sorts, draft.sort, value => { draft.sort = value; draft.position = Vector2.zero; }));
                HotelUi.TableHeader(width, ref y, new[] { HotelText.T("RSH.Ui.Bed"), HotelText.T("RSH.Ui.NightlyPriceDraft"),
                    HotelText.T("RSH.Text.Guests"), HotelText.T("RSH.Ui.Rental") });
                int row = 0;
                foreach (var bed in ordered)
                {
                    HotelStay stay = stays.FirstOrDefault(s => s.bed == bed);
                    Room room = bed.GetRoom();
                    string occupants = stay == null ? HotelText.T("RSH.Text.Vacant") : string.Join(", ", stay.guests.Where(g => g.pawn != null).Select(g => g.pawn.LabelShortCap.ToString()));
                    string name = bed.LabelCap + " " + bed.Position;
                    string roomName = room?.Role?.LabelCap.ToString() ?? HotelText.T("RSH.Text.Outdoors");
                    if (!HotelUi.Matches(context.SearchText, name + " " + roomName + " " + occupants)) continue;
                    HotelUi.TableRow(width, ref y, row++, new[] { name, (bed.SleepingSlotsCount > 1 ? draft.dual : draft.single).ToString(), occupants,
                        HotelText.T(draft.beds.Contains(bed) ? "RSH.Ui.Registered" : "RSH.Ui.Unregistered") }, bed,
                        () => OpenBed(draft, manager, context, bed), HotelText.T("RSH.Ui.Edit"));
                }
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingHotelBedsInThisShopZone"));
                draft.height = y + 8f;
            });
        }

        //参数确认后只回写本页草稿，取消保留原值。
        private static void OpenSettings(Draft target, HotelMapComponent manager, ShopManagerUiContext context)
        {
            bool enabled = target.enabled, renewals = target.renewals;
            int single = target.single, dual = target.dual;
            string singleBuffer = single.ToString(), dualBuffer = dual.ToString();
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Ui.RoomSettings"), width => {
                float y = 0;
                HotelUi.Check(width, ref y, HotelText.T("RSH.Text.AcceptNewStays"), ref enabled);
                HotelUi.Check(width, ref y, HotelText.T("RSH.Text.AllowEligibleGuestsToExtendTheirStay"), ref renewals);
                HotelUi.Number(width, ref y, HotelText.T("RSH.Text.SingleBedSilverNight"), ref single, ref singleBuffer, 10000);
                HotelUi.Number(width, ref y, HotelText.T("RSH.Text.DoubleBedSilverWholeBedNight"), ref dual, ref dualBuffer, 10000);
                HotelUi.Notice(width, ref y, HotelText.T("RSH.Ui.RoomPricingHint"));
                string issue = manager.GetIssue(context.Shop);
                if (!issue.NullOrEmpty()) HotelUi.Notice(width, ref y, issue, true);
                return y;
            }, () => { target.enabled = enabled; target.renewals = renewals; target.single = single; target.dual = dual; }));
        }

        //房间详情可编辑对应床型报价，合同只锁定床位登记和已成交价格。
        private static void OpenBed(Draft draft, HotelMapComponent manager, ShopManagerUiContext context, Building_HotelBed bed)
        {
            bool registered = draft.beds.Contains(bed);
            bool doubleBed = bed.SleepingSlotsCount > 1;
            int price = doubleBed ? draft.dual : draft.single;
            string priceBuffer = price.ToString();
            Find.WindowStack.Add(new Dialog_HotelDetails(bed.LabelCap + " " + bed.Position, width => {
                float y = 0;
                if (!bed.Spawned || !context.Shop.ContainsCell(bed.Position)) { HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.BedUnavailable"), true); return y; }
                HotelStay stay = manager.Stays.FirstOrDefault(s => !s.completed && s.bed == bed);
                HotelUi.Number(width, ref y, HotelText.T(doubleBed ? "RSH.Text.DoubleBedSilverWholeBedNight" : "RSH.Text.SingleBedSilverNight"),
                    ref price, ref priceBuffer, 10000);
                HotelUi.Notice(width, ref y, HotelText.T("RSH.Ui.BedPriceScope"));
                HotelUi.Check(width, ref y, HotelText.T("RSH.Text.OfferThisBedForRent"), ref registered, stay != null);
                var room = bed.GetRoom();
                HotelUi.Value(width, ref y, HotelText.T("RSH.Text.RoomImpressiveness"),
                    (room?.Role?.LabelCap.ToString() ?? HotelText.T("RSH.Text.Outdoors")) + $" · {room?.GetStat(RoomStatDefOf.Impressiveness) ?? 0:0}");
                HotelUi.Value(width, ref y, HotelText.T("RSH.Text.Guests"), stay == null ? HotelText.T("RSH.Text.Vacant")
                    : string.Join(", ", stay.guests.Where(g => g.pawn != null).Select(g => g.pawn.LabelShortCap.ToString())));
                if (stay != null)
                {
                    HotelUi.Value(width, ref y, HotelText.T("RSH.Text.BookingDaysLeft"), $"#{stay.id} · {Mathf.Max(0, (stay.checkoutTick - Find.TickManager.TicksGame) / 60000f):0.0}");
                    HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.ThisBedIsReservedByABookingUnregister"));
                }
                return y;
            }, () => {
                if (!bed.Spawned || !context.Shop.ContainsCell(bed.Position)) return;
                if (doubleBed) draft.dual = price; else draft.single = price;
                if (manager.Stays.Any(s => !s.completed && s.bed == bed)) return;
                if (registered) draft.beds.Add(bed); else draft.beds.Remove(bed);
            }));
        }

        //统一保存时再次检查床位所属区域及合同锁定状态。
        public override void OnSave(ShopUiContext context)
        {
            if (context is not ShopManagerUiContext shopContext) return;
            Draft draft = context.GetPageState<Draft>(Key);
            if (draft == null) return;
            var manager = HotelMapComponent.For(shopContext.Shop.Map);
            var settings = manager.SettingsFor(shopContext.Shop);
            settings.enabled = draft.enabled; settings.acceptRenewals = draft.renewals;
            settings.singlePrice = draft.single; settings.doublePrice = draft.dual;
            foreach (var bed in settings.registeredBeds.Concat(draft.beds).Distinct().ToList())
                manager.RegisterBed(shopContext.Shop, bed, draft.beds.Contains(bed) && bed != null && bed.Spawned && shopContext.Shop.ContainsCell(bed.Position));
        }

        //成交合同保留原价，编辑仅影响新报价。
        public override string GetSaveTip(ShopUiContext context) => HotelText.T("RSH.Ui.RoomPricingHint");
    }
}
