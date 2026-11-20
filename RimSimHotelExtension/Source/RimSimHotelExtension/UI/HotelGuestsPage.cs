using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //住客页以头像卡片展示实时状态、满意度条、基本需求与钱包余额，详情页支持查看体验构成和提前退房。
    public sealed class HotelGuestsPage : ShopManagerPageWorker
    {
        private sealed class State : PageScroll
        {
            public int sort;
            public int filter;
        }

        //设施或未结合同存在时保留住客列表。
        public override bool CanShow(ShopUiContext context) => base.CanShow(context) && HotelUi.HasHotel(context);

        //只读页面无需额外提交。
        public override bool ShowSaveButton(ShopUiContext context) => false;

        //顶部汇总合同与满意度，下方以卡片列出每位在住或办理中的旅客。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            var state = context.GetOrCreatePageState("RSH.Guests", () => new State());
            var manager = HotelMapComponent.For(context.Shop.Map);
            var stays = manager.ActiveStays.Where(s => !s.completed && s.shopZoneId == context.Shop.ID).ToList();
            var guests = stays.SelectMany(stay => stay.guests.Where(g => g.pawn != null).Select(guest => new { stay, guest })).ToList();
            var filters = new[]
            {
                HotelText.T("RSH.Ui.FilterAll"),
                HotelText.T("RSH.Text.Staying"),
                HotelText.T("RSH.Ui.FilterPendingReception"),
                HotelText.T("RSH.Ui.FilterLowSat")
            };
            var sorts = new[]
            {
                HotelText.T("RSH.Ui.SortName"),
                HotelText.T("RSH.Ui.LowSatisfactionFirst"),
                HotelText.T("RSH.Ui.CheckoutFirst")
            };

            HotelUi.Scroll(rect, state, width =>
            {
                float y = 0f;
                HotelUi.Metrics(width, ref y,
                    new[] { HotelText.T("RSH.Text.OpenBookings"), HotelText.T("RSH.Text.GuestsStaying"),
                        HotelText.T("RSH.Text.AverageSatisfaction"), HotelText.T("RSH.Text.HeldPrepayments") },
                    stays.Count.ToString(), guests.Count.ToString(),
                    guests.Count == 0 ? "—" : $"{guests.Average(g => g.guest.satisfaction):0} / 100",
                    stays.Sum(s => s.escrow).ToString());

                HotelUi.FilterAndSortBar(width, ref y, filters, ref state.filter, sorts[state.sort],
                    () => HotelUi.SortMenu(sorts, state.sort, value => { state.sort = value; state.position = Vector2.zero; }),
                    () => state.position = Vector2.zero);

                var ordered = guests
                    .Where(entry => MatchesFilter(state.filter, entry.stay, entry.guest))
                    .OrderBy(g => state.sort == 1 ? g.guest.satisfaction : state.sort == 2 ? g.stay.checkoutTick : 0f)
                    .ThenBy(g => g.guest.pawn.LabelShortCap.ToString());

                int count = 0;
                foreach (var entry in ordered)
                {
                    var guest = entry.guest;
                    var stay = entry.stay;
                    string activity = guest.pawn.jobs?.curDriver?.GetReport() ?? HotelText.Display(guest.status);
                    if (!HotelUi.Matches(context.SearchText, guest.pawn.LabelShortCap + " " + activity + " " + stay.bed?.LabelCap + " #" + stay.id))
                        continue;
                    DrawGuestCard(width, ref y, count++, manager, stay, guest, activity);
                }
                if (count == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingGuests"));
                state.height = y + 8f;
            });
        }

        //绘制单个住客卡片：左侧肖像、中间三行住店状态与满意度条、右侧操作按钮。
        private static void DrawGuestCard(float width, ref float y, int index, HotelMapComponent manager,
            HotelStay stay, HotelGuestState guest, string activity)
        {
            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float barH = tinyH + 4f;
            float ctrlH = HotelUi.Line;
            float cardH = smallH + tinyH + barH + 26f;

            Rect cardRect = new Rect(0f, y, width, cardH);
            ShopUiVisualUtility.DrawTableRowBackground(cardRect, index, stay.closing || guest.satisfaction < 45f);
            ShopUiVisualUtility.DrawBorder(cardRect, new Color(1f, 1f, 1f, 0.08f));

            //左侧住客头像。
            float portraitSize = Mathf.Min(64f, cardH - 12f);
            Rect portraitRect = new Rect(8f, y + (cardH - portraitSize) * 0.5f, portraitSize, portraitSize);
            if (!guest.pawn.Destroyed)
            {
                RenderTexture portrait = PortraitsCache.Get(guest.pawn, new Vector2(96f, 96f), Rot4.South);
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit, true);
            }

            //右侧详情与定位按钮。
            string detailLabel = HotelText.T("RSH.Ui.Details");
            string locateLabel = HotelText.T("RSH.Ui.Locate");
            float detailW = Mathf.Max(56f, Text.CalcSize(detailLabel).x + 16f);
            float locateW = Mathf.Max(56f, Text.CalcSize(locateLabel).x + 16f);
            float btnW = Mathf.Max(detailW, locateW);
            float rightX = width - btnW - 8f;
            bool stackBtns = cardH >= ctrlH * 2f + 16f;
            if (stackBtns)
            {
                float topY = y + (cardH - ctrlH * 2f - 4f) * 0.5f;
                if (HotelUi.Button(new Rect(rightX, topY, btnW, ctrlH), detailLabel))
                    OpenGuestDetails(manager, stay, guest);
                if (HotelUi.Button(new Rect(rightX, topY + ctrlH + 4f, btnW, ctrlH), locateLabel, enabled: guest.pawn.Spawned))
                    CameraJumper.TryJumpAndSelect(guest.pawn);
            }
            else
            {
                rightX = width - detailW - locateW - 18f;
                float btnY = y + (cardH - ctrlH) * 0.5f;
                if (HotelUi.Button(new Rect(rightX, btnY, detailW, ctrlH), detailLabel))
                    OpenGuestDetails(manager, stay, guest);
                if (HotelUi.Button(new Rect(rightX + detailW + 6f, btnY, locateW, ctrlH), locateLabel, enabled: guest.pawn.Spawned))
                    CameraJumper.TryJumpAndSelect(guest.pawn);
            }

            //中间信息区。
            float midX = portraitRect.xMax + 10f;
            float midW = Mathf.Max(100f, rightX - midX - 10f);
            float rowY = y + 6f;

            //第 1 行：姓名 + 合同编号 + 同行者 + 阶段状态徽章。
            string companion = stay.guests.Count > 1
                ? " · " + HotelText.T("RSH.Ui.WithCompanion",
                    string.Join(", ", stay.guests.Where(g => g.pawn != null && g.pawn != guest.pawn).Select(g => g.pawn.LabelShortCap.ToString())).Named("NAME"))
                : "";
            string stageText = stay.closing ? HotelText.T("RSH.Text.CheckingOut")
                : !stay.active ? HotelText.T("RSH.Text.CheckingIn")
                : stay.renewalRequested ? HotelText.T("RSH.Ui.RenewingTag")
                : HotelText.Display(guest.status);
            Color stageFill = stay.closing ? HotelUi.WarningBadgeFill
                : !stay.active ? HotelUi.InfoBadgeFill
                : HotelUi.PositiveBadgeFill;
            float stageW = Mathf.Min(midW * 0.36f, Mathf.Max(68f, Text.CalcSize(stageText).x + 18f));
            ShopUiVisualUtility.DrawCellLabel(new Rect(midX, rowY, Mathf.Max(1f, midW - stageW - 6f), smallH),
                $"{guest.pawn.LabelShortCap} #{stay.id}{companion}", Color.white);
            HotelUi.Badge(new Rect(midX + midW - stageW, rowY, stageW, smallH), stageText, stageFill);
            rowY += smallH + 4f;

            //第 2 行：当前活动 + 客房床位 + 晚数与剩余天数。
            float daysLeft = Mathf.Max(0f, (stay.checkoutTick - Find.TickManager.TicksGame) / 60000f);
            string bedName = stay.bed?.LabelCap.ToString() ?? HotelText.T("RSH.Text.BedUnavailable");
            string subLine = $"{activity} · {bedName} · {stay.earnedNights}/{stay.bookedNights} ({daysLeft:0.0}d)";
            ShopUiVisualUtility.DrawCellLabel(new Rect(midX, rowY, midW, tinyH), subLine, ShopUiVisualUtility.MutedText, GameFont.Tiny);
            rowY += tinyH + 5f;

            //第 3 行：满意度进度条 + 饱食/精力/娱乐 + 钱包/账户余额。
            float satBarW = Mathf.Min(130f, midW * 0.34f);
            HotelUi.ProgressBar(new Rect(midX, rowY, satBarW, barH), guest.satisfaction / 100f,
                HotelText.T("RSH.Ui.SatShort", guest.satisfaction.ToString("0").Named("SAT")),
                HotelUi.SatisfactionColor(guest.satisfaction));

            string foodStr = guest.pawn.needs?.food != null ? $"{guest.pawn.needs.food.CurLevelPercentage * 100f:0}%" : "—";
            string restStr = guest.pawn.needs?.rest != null ? $"{guest.pawn.needs.rest.CurLevelPercentage * 100f:0}%" : "—";
            string joyStr = guest.pawn.needs?.joy != null ? $"{guest.pawn.needs.joy.CurLevelPercentage * 100f:0}%" : "—";
            string statsText = HotelText.T("RSH.Ui.GuestNeedsAndFunds",
                foodStr.Named("FOOD"), restStr.Named("REST"), joyStr.Named("JOY"),
                guest.wallet.Named("WALLET"), guest.account.Named("ACCOUNT"));
            bool needWarn = (guest.pawn.needs?.food?.CurLevelPercentage ?? 1f) < 0.25f
                || (guest.pawn.needs?.rest?.CurLevelPercentage ?? 1f) < 0.25f;
            ShopUiVisualUtility.DrawCellLabel(new Rect(midX + satBarW + 8f, rowY, Mathf.Max(1f, midW - satBarW - 8f), barH),
                statsText, needWarn ? ShopUiVisualUtility.WarningText : ShopUiVisualUtility.MutedText, GameFont.Tiny);

            y += cardH + 6f;
        }

        //打开单个住客详情弹窗，展示环境体验说明、资金流水与提前退房入口。
        private static void OpenGuestDetails(HotelMapComponent manager, HotelStay stay, HotelGuestState guest)
        {
            Find.WindowStack.Add(new Dialog_HotelDetails(guest.pawn.LabelShortCap + " #" + stay.id, w =>
            {
                float detailY = 0f;
                HotelUi.Metrics(w, ref detailY,
                    new[] { HotelText.T("RSH.Ui.Satisfaction"), HotelText.T("RSH.Ui.Funds"),
                        HotelText.T("RSH.Text.BookedStartedNights"), HotelText.T("RSH.Text.HeldPrepaymentSilver") },
                    $"{guest.satisfaction:0} / 100", $"{guest.wallet} / {guest.account}",
                    $"{stay.earnedNights} / {stay.bookedNights}", guest.escrow.ToString());
                HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.CurrentActivity"),
                    guest.pawn.jobs?.curDriver?.GetReport() ?? HotelText.Display(guest.status));
                if (!guest.lastExperienceNote.NullOrEmpty())
                    HotelUi.Notice(w, ref detailY, HotelText.T("RSH.Ui.RecentExperienceLine", guest.lastExperienceNote.Named("NOTE")));
                HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.BedCurrentPosition"),
                    (stay.bed?.LabelCap.ToString() ?? HotelText.T("RSH.Text.BedUnavailable")) + " · " + guest.pawn.Position);
                HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.StayStatus"), HotelText.Display(guest.status));
                HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.TotalSpentSilver"), $"{guest.totalSpent:0}");
                HotelUi.Value(w, ref detailY, HotelText.T("RSH.Text.DiscretionarySpendingUsedAllowance"),
                    $"{guest.optionalSpent} / {guest.optionalDailyLimit}");

                if (!stay.completed && !stay.closing)
                {
                    if (HotelUi.Button(new Rect(0f, detailY, Mathf.Min(w, 180f), HotelUi.Line),
                        HotelText.T("RSH.Ui.RequestEarlyCheckout"), danger: true))
                        manager.RequestCheckout(stay, "RSH.Text.EarlyCheckoutRequestedByHost");
                    detailY += HotelUi.Line + 8f;
                }

                DrawMoneyLedger(w, ref detailY, manager, guest);
                return detailY;
            }));
        }

        //绘制住客最近八笔资金明细。
        private static void DrawMoneyLedger(float width, ref float y, HotelMapComponent manager, HotelGuestState guest)
        {
            var records = manager.MoneyRecords.Where(r => r.pawn == guest.pawn && r.kind != "Room").Reverse().Take(8).ToList();
            HotelUi.Section(width, ref y, HotelText.T("RSH.Text.RecentMoneyMovementsSilver"));
            int row = 0;
            foreach (var record in records)
            {
                string kind = record.kind switch
                {
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

        private static bool MatchesFilter(int filter, HotelStay stay, HotelGuestState guest) => filter switch
        {
            1 => stay.active && !stay.closing,
            2 => !stay.active || stay.closing || stay.renewalRequested,
            3 => guest.satisfaction < 55f,
            _ => true
        };
    }
}
