using System.Linq;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //住客首页以紧凑行比较状态，合同和流水在独立明细页查看。
    public sealed class HotelGuestsPage : ShopManagerPageWorker
    {
        //搜索和排序只改变列表显示。
        private sealed class State : PageScroll { public int sort; }

        //设施或未结合同存在时保留住客列表。
        public override bool CanShow(ShopUiContext context) => base.CanShow(context) && HotelUi.HasHotel(context);

        //只读页面无需额外提交。
        public override bool ShowSaveButton(ShopUiContext context) => false;

        //同行住客分别列出，按姓名、满意度或退房时间排序。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            var state = context.GetOrCreatePageState("RSH.Guests", () => new State());
            var manager = HotelMapComponent.For(context.Shop.Map);
            var stays = manager.Stays.Where(s => !s.completed && s.shopZoneId == context.Shop.ID).ToList();
            var guests = stays.SelectMany(stay => stay.guests.Where(g => g.pawn != null).Select(guest => new { stay, guest })).ToList();
            var sorts = new[] { HotelText.T("RSH.Ui.SortName"), HotelText.T("RSH.Ui.LowSatisfactionFirst"), HotelText.T("RSH.Ui.CheckoutFirst") };
            HotelUi.Scroll(rect, state, width => {
                float y = 0;
                HotelUi.Summary(width, ref y, HotelText.T("RSH.Ui.GuestSummary", stays.Count.Named("BOOKINGS"), guests.Count.Named("GUESTS"),
                    (guests.Count == 0 ? "—" : guests.Average(g => g.guest.satisfaction).ToString("0")).Named("SATISFACTION")));
                float buttonWidth = Mathf.Min(width, Text.CalcSize(sorts[state.sort]).x + 28f);
                if (HotelUi.Button(new Rect(0, y, buttonWidth, HotelUi.Line), sorts[state.sort]))
                    HotelUi.SortMenu(sorts, state.sort, value => { state.sort = value; state.position = Vector2.zero; });
                y += HotelUi.Line + 8f;
                HotelUi.TableHeader(width, ref y, new[] { HotelText.T("RSH.Text.Guests"), HotelText.T("RSH.Text.CurrentActivity"),
                    HotelText.T("RSH.Ui.Funds"), HotelText.T("RSH.Ui.Satisfaction") });
                int row = 0;
                foreach (var entry in guests.OrderBy(g => state.sort == 1 ? g.guest.satisfaction : state.sort == 2 ? g.stay.checkoutTick : 0)
                    .ThenBy(g => g.guest.pawn.LabelShortCap.ToString()))
                {
                    var guest = entry.guest;
                    var stay = entry.stay;
                    string activity = guest.pawn.jobs?.curDriver?.GetReport() ?? HotelText.Display(guest.status);
                    if (!HotelUi.Matches(context.SearchText, guest.pawn.LabelShortCap + " " + activity + " " + stay.bed?.LabelCap + " #" + stay.id)) continue;
                    HotelUi.TableRow(width, ref y, row++, new[] { guest.pawn.LabelShortCap + " #" + stay.id, activity,
                        $"{guest.wallet} / {guest.account}", $"{guest.satisfaction:0}" }, guest.pawn,
                        () => Find.WindowStack.Add(new Dialog_HotelDetails(guest.pawn.LabelShortCap, w => {
                            float detailY = 0;
                            HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.CurrentActivity"), guest.pawn.jobs?.curDriver?.GetReport() ?? HotelText.Display(guest.status));
                            HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.WalletTravelAccountSilver"), $"{guest.wallet} / {guest.account}");
                            HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.SatisfactionDaysLeft"),
                                $"{guest.satisfaction:0} / 100 · {Mathf.Max(0, (stay.checkoutTick - Find.TickManager.TicksGame) / 60000f):0.0}");
                            DrawDetails(w, ref detailY, manager, stay, guest);
                            return detailY;
                        })));
                }
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingGuests"));
                state.height = y + 8f;
            });
        }

        //住宿预付款、日常花费和取款本金分别展示。
        private static void DrawDetails(float width, ref float y, HotelMapComponent manager, HotelStay stay, HotelGuestState guest)
        {
            HotelUi.Value(width, ref y, HotelText.T("RSH.Text.BedCurrentPosition"),
                (stay.bed?.LabelCap.ToString() ?? HotelText.T("RSH.Text.BedUnavailable")) + " · " + guest.pawn.Position);
            HotelUi.Value(width, ref y, HotelText.T("RSH.Text.StayStatus"), HotelText.Display(guest.status));
            HotelUi.Value(width, ref y, HotelText.T("RSH.Text.BookedStartedNights"), $"{stay.bookedNights} / {stay.earnedNights}");
            HotelUi.Value(width, ref y, HotelText.T("RSH.Text.HeldPrepaymentSilver"), guest.escrow.ToString());
            HotelUi.Value(width, ref y, HotelText.T("RSH.Text.TotalSpentSilver"), $"{guest.totalSpent:0}");
            HotelUi.Value(width, ref y, HotelText.T("RSH.Text.DiscretionarySpendingUsedAllowance"), $"{guest.optionalSpent} / {guest.optionalDailyLimit}");
            var records = manager.MoneyRecords.Where(r => r.pawn == guest.pawn && r.kind != "Room").Reverse().Take(8).ToList();
            HotelUi.Section(width, ref y, HotelText.T("RSH.Text.RecentMoneyMovementsSilver"));
            int row = 0;
            foreach (var record in records)
            {
                string kind = record.kind switch {
                    "Spend" => HotelText.T("RSH.Text.Spending"),
                    "Refund" => HotelText.T("RSH.Text.Refund"),
                    "Prepay" => HotelText.T("RSH.Text.RoomPrepayment"),
                    "withdrawal" => HotelText.T("RSH.Text.WithdrawalFee"),
                    _ => record.kind
                };
                HotelUi.MoneyRow(width, ref y, kind,
                    record.amount + (record.kind == "withdrawal" ? " / " + record.fee : ""),
                    HotelText.T("RSH.Ui.RecordDay", (record.tick / 60000 + 1).Named("DAY")) +
                    (record.label.NullOrEmpty() ? "" : " · " + HotelText.Display(record.label)), row++);
            }
            if (records.Count == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMoneyMovementsRecorded"));
        }
    }
}
