using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Models;
using RimSimHotelExtension.Services;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //客房页支持内联调整接待开关、续住、作息、房价和一键床位登记，同时展示每间客房的环境评价预览。
    public sealed class HotelRoomsPage : ShopManagerPageWorker
    {
        private const string Key = "RSH.RoomDraft";
        private const int PreviewCacheTicks = 30;

        private sealed class Draft : PageScroll
        {
            public bool enabled, renewals;
            public int single, dual, sort, filter, sleepStartHour, wakeHour;
            public string singleBuf, dualBuf;
            public HashSet<Building_Bed> beds;
            public int cacheTick = -1;
            public int cacheShopId = -1;
            public readonly List<Building_Bed> cachedBeds = new List<Building_Bed>();
            public readonly Dictionary<int, HotelRoomPreview> previewByBedId = new Dictionary<int, HotelRoomPreview>();
        }

        //床位尚未登记时也保留配置入口。
        public override bool CanShow(ShopUiContext context) => base.CanShow(context) && HotelUi.HasHotel(context);

        //窗口草稿与实际经营设置分离。
        private static Draft State(ShopManagerUiContext context) => context.GetOrCreatePageState(Key, () =>
        {
            var settings = HotelMapComponent.For(context.Shop.Map).SettingsFor(context.Shop);
            return new Draft
            {
                enabled = settings.enabled,
                renewals = settings.acceptRenewals,
                single = settings.singlePrice,
                dual = settings.doublePrice,
                singleBuf = settings.singlePrice.ToString(),
                dualBuf = settings.doublePrice.ToString(),
                sleepStartHour = settings.sleepStartHour,
                wakeHour = settings.wakeHour,
                beds = new HashSet<Building_Bed>(settings.registeredBeds)
            };
        });

        //绘制内联经营配置卡、筛选排序栏与客房床位卡片清单。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            Draft draft = State(context);
            var manager = HotelMapComponent.For(context.Shop.Map);
            var desk = manager.DeskFor(context.Shop);
            RefreshRoomCacheIfNeeded(draft, context.Shop);
            var beds = draft.cachedBeds;
            var stays = manager.ActiveStays.Where(s => !s.completed && s.shopZoneId == context.Shop.ID).ToList();
            var filters = new[]
            {
                HotelText.T("RSH.Ui.FilterAll"),
                HotelText.T("RSH.Ui.Registered"),
                HotelText.T("RSH.Ui.Unregistered"),
                HotelText.T("RSH.Ui.FilterOccupied"),
                HotelText.T("RSH.Text.Vacant")
            };
            var sorts = new[]
            {
                HotelText.T("RSH.Ui.SortName"),
                HotelText.T("RSH.Ui.UnregisteredFirst"),
                HotelText.T("RSH.Ui.VacantFirst"),
                HotelText.T("RSH.Ui.BestRoomFirst")
            };

            HotelUi.Scroll(rect, draft, width =>
            {
                float y = 0f;
                DrawControlCard(width, ref y, draft, manager, desk, beds, stays, context);

                string issue = manager.GetIssue(context.Shop);
                if (!issue.NullOrEmpty()) HotelUi.Notice(width, ref y, issue, true);

                HotelUi.FilterAndSortBar(width, ref y, filters, ref draft.filter, sorts[draft.sort],
                    () => HotelUi.SortMenu(sorts, draft.sort, value => { draft.sort = value; draft.position = Vector2.zero; }),
                    () => draft.position = Vector2.zero);

                var rows = beds.Select(b => new
                {
                    bed = b,
                    stay = stays.FirstOrDefault(s => s.bed == b),
                    registered = draft.beds.Contains(b),
                    preview = draft.previewByBedId.TryGetValue(b.thingIDNumber, out HotelRoomPreview cached)
                        ? cached : HotelQualityUtility.EvaluateRoomPreview(b)
                }).Where(item => MatchesFilter(draft.filter, item.registered, item.stay != null));

                rows = draft.sort switch
                {
                    1 => rows.OrderBy(i => i.registered ? 1 : 0).ThenBy(i => i.bed.LabelCap.ToString()),
                    2 => rows.OrderBy(i => i.stay != null ? 1 : 0).ThenBy(i => i.bed.LabelCap.ToString()),
                    3 => rows.OrderByDescending(i => i.preview.impressiveness).ThenBy(i => i.bed.LabelCap.ToString()),
                    _ => rows.OrderBy(i => i.bed.LabelCap.ToString()).ThenBy(i => i.bed.thingIDNumber)
                };

                int count = 0;
                foreach (var item in rows)
                {
                    string occupants = item.stay == null ? HotelText.T("RSH.Text.Vacant")
                        : string.Join(", ", item.stay.guests.Where(g => g.pawn != null).Select(g => g.pawn.LabelShortCap.ToString()));
                    if (!HotelUi.Matches(context.SearchText, item.bed.LabelCap + " " + item.preview.roleLabel + " " + occupants)) continue;
                    DrawBedRow(width, ref y, count++, draft, manager, context, item.bed, item.stay, item.preview);
                }
                if (count == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingHotelBedsInThisShopZone"));
                draft.height = y + 8f;
            });
        }

        //绘制顶部内联控制面板：开关、作息、单双人房价和批量床位操作。
        private static void DrawControlCard(float width, ref float y, Draft draft, HotelMapComponent manager,
            Buildings.Building_HotelDesk desk, List<Building_Bed> beds, List<HotelStay> stays, ShopManagerUiContext context)
        {
            float pad = 10f;
            float innerW = Mathf.Max(1f, width - pad * 2f);
            float ctrlH = HotelUi.Line;
            bool stackTwo = innerW < 520f;
            float cardH = pad + ctrlH + 8f + (stackTwo ? (ctrlH + 6f) * 2f : ctrlH + 8f)
                + (stackTwo ? (ctrlH + 6f) * 2f : ctrlH + 8f) + ctrlH + pad;
            float startY = y;
            ShopUiVisualUtility.DrawSection(new Rect(0f, startY, width, cardH));

            float cy = startY + pad;
            //第一行：床位与前台状态摘要。
            int registeredCount = beds.Count(draft.beds.Contains);
            string deskStatus = desk == null ? HotelText.T("RSH.Text.NoHotelReceptionDesk")
                : desk.IsManned ? HotelText.T("RSH.Ui.DeskStaffedBy", desk.CurrentCashier.LabelShortCap.Named("STAFF"))
                : HotelText.T("RSH.Text.ReceptionIsUnstaffed");
            float badgeW = Mathf.Min(innerW * 0.45f, Mathf.Max(120f, Text.CalcSize(deskStatus).x + 22f));
            string statsLine = HotelText.T("RSH.Ui.RoomBriefStats", beds.Count.Named("TOTAL"),
                registeredCount.Named("RENTED"), stays.Count.Named("OCCUPIED"));
            ShopUiVisualUtility.DrawCellLabel(new Rect(pad, cy, Mathf.Max(1f, innerW - badgeW - 8f), ctrlH), statsLine, Color.white);
            HotelUi.Badge(new Rect(width - pad - badgeW, cy, badgeW, ctrlH), deskStatus,
                desk != null && desk.IsManned ? HotelUi.PositiveBadgeFill : HotelUi.WarningBadgeFill);
            cy += ctrlH + 8f;

            //第二行：接受新入住 + 允许续住 胶囊开关。
            if (stackTwo)
            {
                if (HotelUi.TogglePill(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Text.AcceptNewStays"), draft.enabled,
                    HotelText.T("RSH.Ui.Open"), HotelText.T("RSH.Ui.Paused"))) draft.enabled = !draft.enabled;
                cy += ctrlH + 6f;
                if (HotelUi.TogglePill(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Text.AllowEligibleGuestsToExtendTheirStay"), draft.renewals,
                    HotelText.T("RSH.Ui.Enabled"), HotelText.T("RSH.Ui.Disabled"))) draft.renewals = !draft.renewals;
                cy += ctrlH + 6f;
            }
            else
            {
                float halfW = (innerW - 8f) * 0.5f;
                if (HotelUi.TogglePill(new Rect(pad, cy, halfW, ctrlH), HotelText.T("RSH.Text.AcceptNewStays"), draft.enabled,
                    HotelText.T("RSH.Ui.Open"), HotelText.T("RSH.Ui.Paused"))) draft.enabled = !draft.enabled;
                if (HotelUi.TogglePill(new Rect(pad + halfW + 8f, cy, halfW, ctrlH), HotelText.T("RSH.Text.AllowEligibleGuestsToExtendTheirStay"), draft.renewals,
                    HotelText.T("RSH.Ui.Enabled"), HotelText.T("RSH.Ui.Disabled"))) draft.renewals = !draft.renewals;
                cy += ctrlH + 8f;
            }

            //第三行：单人床与双人床价格内联编辑。
            string unit = HotelText.T("RSH.Ui.SilverPerNightShort");
            if (stackTwo)
            {
                HotelUi.InlineNumber(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Ui.SinglePriceShort"), ref draft.single, ref draft.singleBuf, 10000, unit);
                cy += ctrlH + 6f;
                HotelUi.InlineNumber(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Ui.DoublePriceShort"), ref draft.dual, ref draft.dualBuf, 10000, unit);
                cy += ctrlH + 6f;
            }
            else
            {
                float halfW = (innerW - 12f) * 0.5f;
                HotelUi.InlineNumber(new Rect(pad, cy, halfW, ctrlH), HotelText.T("RSH.Ui.SinglePriceShort"), ref draft.single, ref draft.singleBuf, 10000, unit);
                HotelUi.InlineNumber(new Rect(pad + halfW + 12f, cy, halfW, ctrlH), HotelText.T("RSH.Ui.DoublePriceShort"), ref draft.dual, ref draft.dualBuf, 10000, unit);
                cy += ctrlH + 8f;
            }

            //第四行：作息时间 + 批量登记/取消空床登记。
            string sleepLabel = HotelText.T("RSH.Ui.SleepScheduleButton", $"{draft.sleepStartHour:00}:00–{draft.wakeHour:00}:00".Named("RANGE"));
            string regAllLabel = HotelText.T("RSH.Ui.RegisterAllBeds");
            string clearVacantLabel = HotelText.T("RSH.Ui.UnregisterVacantBeds");
            float sleepW = Mathf.Min(innerW * 0.44f, Mathf.Max(140f, Text.CalcSize(sleepLabel).x + 20f));
            float remW = Mathf.Max(1f, innerW - sleepW - 12f);
            float btnW = remW * 0.5f;

            if (HotelUi.Button(new Rect(pad, cy, sleepW, ctrlH), sleepLabel))
                OpenSleepScheduleDialog(draft);
            if (HotelUi.Button(new Rect(pad + sleepW + 6f, cy, btnW, ctrlH), regAllLabel))
                BatchToggleBeds(draft, manager, context, beds, true);
            if (HotelUi.Button(new Rect(pad + sleepW + 6f + btnW + 6f, cy, btnW, ctrlH), clearVacantLabel))
                BatchToggleBeds(draft, manager, context, beds, false);

            y = startY + cardH + 10f;
        }

        //绘制单张客房床位卡片行，含图标、房间品质评分、住客与一键登记切换。
        private static void DrawBedRow(float width, ref float y, int index, Draft draft, HotelMapComponent manager,
            ShopManagerUiContext context, Building_Bed bed, HotelStay stay, HotelRoomPreview preview)
        {
            float ctrlH = HotelUi.Line;
            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float rowH = smallH + tinyH + 18f;
            Rect rowRect = new Rect(0f, y, width, rowH);
            ShopUiVisualUtility.DrawTableRowBackground(rowRect, index, stay != null);
            ShopUiVisualUtility.DrawBorder(rowRect, new Color(1f, 1f, 1f, 0.06f));

            float iconSize = Mathf.Min(36f, rowH - 12f);
            Widgets.ThingIcon(new Rect(8f, y + (rowH - iconSize) * 0.5f, iconSize, iconSize), bed);

            bool registered = draft.beds.Contains(bed);
            bool doubleBed = bed.SleepingSlotsCount > 1;
            int price = doubleBed ? draft.dual : draft.single;

            string toggleText = HotelText.T(registered ? "RSH.Ui.Registered" : "RSH.Ui.Unregistered");
            string detailText = HotelText.T("RSH.Ui.Details");
            string locateText = HotelText.T("RSH.Ui.Locate");
            float toggleW = Mathf.Max(68f, Text.CalcSize(toggleText).x + 18f);
            float detailW = Mathf.Max(52f, Text.CalcSize(detailText).x + 16f);
            float locateW = Mathf.Max(52f, Text.CalcSize(locateText).x + 16f);
            float rightW = toggleW + detailW + locateW + 18f;

            float textX = 8f + iconSize + 8f;
            float textW = Mathf.Max(80f, width - textX - rightW - 8f);

            //第一行：床位名称 + 单/双人徽章 + 价格 + 住客状态。
            string typeTag = HotelText.T(doubleBed ? "RSH.Ui.DoubleBedTag" : "RSH.Ui.SingleBedTag");
            string guestSummary = stay == null ? HotelText.T("RSH.Text.Vacant")
                : $"{string.Join(", ", stay.guests.Where(g => g.pawn != null).Select(g => g.pawn.LabelShortCap.ToString()))} ({Mathf.Max(0f, (stay.checkoutTick - Find.TickManager.TicksGame) / 60000f):0.0}d)";
            string titleLine = $"{bed.LabelCap} · [{typeTag}] · {price}{HotelText.T("RSH.Ui.SilverPerNightShort")} · {guestSummary}";
            ShopUiVisualUtility.DrawCellLabel(new Rect(textX, y + 6f, textW, smallH), titleLine,
                stay != null ? ShopUiVisualUtility.PositiveText : Color.white);

            //第二行：房间类型、印象度、温度与预估每日满意度增减。
            string deltaStr = preview.estimatedDailyDelta >= 0f ? $"+{preview.estimatedDailyDelta:0.#}" : $"{preview.estimatedDailyDelta:0.#}";
            string roomLine = HotelText.T("RSH.Ui.RoomQualityLine",
                preview.roleLabel.Named("ROOM"), preview.impressiveness.ToString("0").Named("IMP"),
                preview.impressivenessLabel.Named("STAGE"), preview.temperature.ToString("0").Named("TEMP"), deltaStr.Named("DELTA"));
            Color roomColor = preview.estimatedDailyDelta >= 2f ? ShopUiVisualUtility.PositiveText
                : preview.estimatedDailyDelta < 0f ? ShopUiVisualUtility.WarningText : ShopUiVisualUtility.MutedText;
            ShopUiVisualUtility.DrawCellLabel(new Rect(textX, y + smallH + 10f, textW, tinyH), roomLine, roomColor, GameFont.Tiny);

            //右侧操作按钮：一键登记切换、详情、定位。
            float btnY = y + (rowH - ctrlH) * 0.5f;
            float bx = width - locateW - 6f;
            if (HotelUi.Button(new Rect(bx, btnY, locateW, ctrlH), locateText, enabled: bed.Spawned))
                CameraJumper.TryJumpAndSelect(bed);
            bx -= detailW + 6f;
            if (HotelUi.Button(new Rect(bx, btnY, detailW, ctrlH), detailText))
                OpenBedDetails(draft, manager, context, bed);
            bx -= toggleW + 6f;
            if (HotelUi.Button(new Rect(bx, btnY, toggleW, ctrlH), toggleText, primary: registered, enabled: stay == null))
            {
                bool next = !registered;
                string issue = manager.BedRegistrationIssue(context.Shop, bed, next);
                if (!issue.NullOrEmpty()) Messages.Message(issue, bed, MessageTypeDefOf.RejectInput, false);
                else if (next) draft.beds.Add(bed);
                else draft.beds.Remove(bed);
            }

            y += rowH + 4f;
        }

        //批量登记或取消登记当前区域内的非锁定床位。
        private static void BatchToggleBeds(Draft draft, HotelMapComponent manager, ShopManagerUiContext context,
            List<Building_Bed> beds, bool register)
        {
            int changed = 0;
            int skipped = 0;
            foreach (var bed in beds)
            {
                bool has = draft.beds.Contains(bed);
                if (has == register) continue;
                if (!manager.BedRegistrationIssue(context.Shop, bed, register).NullOrEmpty()) { skipped++; continue; }
                if (register) draft.beds.Add(bed); else draft.beds.Remove(bed);
                changed++;
            }
            if (changed > 0 || skipped > 0)
                Messages.Message(HotelText.T("RSH.Ui.BatchBedResult", changed.Named("CHANGED"), skipped.Named("SKIPPED")),
                    MessageTypeDefOf.NeutralEvent, false);
        }

        //编辑住客作息时间段弹窗。
        private static void OpenSleepScheduleDialog(Draft target)
        {
            int sleepStart = target.sleepStartHour;
            int wake = target.wakeHour;
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Ui.SleepSchedule"), width =>
            {
                float y = 0f;
                HotelUi.Actions(width, ref y,
                    HotelText.T("RSH.Ui.SleepStartHour", $"{sleepStart:00}:00".Named("TIME")),
                    () => SelectHour(sleepStart, wake, value => sleepStart = value),
                    HotelText.T("RSH.Ui.WakeHour", $"{wake:00}:00".Named("TIME")),
                    () => SelectHour(wake, sleepStart, value => wake = value));
                HotelUi.Notice(width, ref y, HotelText.T("RSH.Ui.SleepScheduleHint"));
                return y;
            }, () => { target.sleepStartHour = sleepStart; target.wakeHour = wake; }));
        }

        //按当地整点选择作息，禁止首尾相同造成空时段。
        private static void SelectHour(int selected, int excluded, Action<int> select)
        {
            Find.WindowStack.Add(new FloatMenu(Enumerable.Range(0, 24).Where(hour => hour != excluded)
                .Select(hour => new FloatMenuOption((hour == selected ? "✓ " : "") + $"{hour:00}:00", () => select(hour))).ToList()));
        }

        //房间详情展示完整环境指标与当前合同，入住中支持发起提前退房。
        private static void OpenBedDetails(Draft draft, HotelMapComponent manager, ShopManagerUiContext context, Building_Bed bed)
        {
            Find.WindowStack.Add(new Dialog_HotelDetails(bed.LabelCap + " " + bed.Position, width =>
            {
                float y = 0f;
                if (!bed.Spawned || !context.Shop.ContainsCell(bed.Position))
                {
                    HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.BedUnavailable"), true);
                    return y;
                }
                HotelStay stay = manager.ActiveStays.FirstOrDefault(s => !s.completed && s.bed == bed);
                var preview = HotelQualityUtility.EvaluateRoomPreview(bed);
                string deltaStr = preview.estimatedDailyDelta >= 0f ? $"+{preview.estimatedDailyDelta:0.#}" : $"{preview.estimatedDailyDelta:0.#}";
                HotelUi.Metrics(width, ref y,
                    new[] { HotelText.T("RSH.Text.RoomImpressiveness"), HotelText.T("RSH.Ui.Cleanliness"),
                        HotelText.T("RSH.Ui.RoomTemp"), HotelText.T("RSH.Ui.EstSatisfactionDelta") },
                    $"{preview.impressiveness:0} ({preview.impressivenessLabel})", $"{preview.cleanliness:0.0}",
                    $"{preview.temperature:0}°C", deltaStr);
                HotelUi.Value(width, ref y, HotelText.T("RSH.Ui.RoomTypeAndSpace"),
                    $"{preview.roleLabel} · {preview.space:0} · {HotelText.T(preview.isPrivateRoom ? "RSH.Ui.PrivateRoomTag" : "RSH.Ui.SharedRoomTag")}");
                HotelUi.Value(width, ref y, HotelText.T("RSH.Text.Guests"), stay == null ? HotelText.T("RSH.Text.Vacant")
                    : string.Join(", ", stay.guests.Where(g => g.pawn != null).Select(g => g.pawn.LabelShortCap.ToString())));
                if (stay != null)
                {
                    HotelUi.Value(width, ref y, HotelText.T("RSH.Text.BookingDaysLeft"),
                        $"#{stay.id} · {stay.earnedNights}/{stay.bookedNights} · {Mathf.Max(0f, (stay.checkoutTick - Find.TickManager.TicksGame) / 60000f):0.0}");
                    HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.ThisBedIsReservedByABookingUnregister"));
                    if (!stay.closing)
                    {
                        if (HotelUi.Button(new Rect(0f, y, Mathf.Min(width, 180f), HotelUi.Line),
                            HotelText.T("RSH.Ui.RequestEarlyCheckout"), danger: true))
                            manager.RequestCheckout(stay, "RSH.Text.EarlyCheckoutRequestedByHost");
                        y += HotelUi.Line + 8f;
                    }
                }
                return y;
            }));
        }

        //按短间隔刷新当前旅馆的床位列表与房间品质预览缓存，避免 OnGUI 每帧重复计算 Room.GetStat。
        private static void RefreshRoomCacheIfNeeded(Draft draft, SimManagementLib.SimZone.Zone_Shop shop)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (draft.cacheShopId == shop.ID && draft.cacheTick >= 0 && now - draft.cacheTick < PreviewCacheTicks)
                return;
            draft.cacheShopId = shop.ID;
            draft.cacheTick = now;
            draft.cachedBeds.Clear();
            draft.previewByBedId.Clear();
            var buildings = shop.Map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_Bed bed && HotelMapComponent.SupportsRentalBed(bed) && shop.ContainsCell(bed.Position))
                {
                    draft.cachedBeds.Add(bed);
                    draft.previewByBedId[bed.thingIDNumber] = HotelQualityUtility.EvaluateRoomPreview(bed);
                }
            }
        }

        private static bool MatchesFilter(int filter, bool registered, bool occupied) => filter switch
        {
            1 => registered,
            2 => !registered,
            3 => occupied,
            4 => registered && !occupied,
            _ => true
        };

        //统一保存时再次检查床位所属区域及合同锁定状态，并立即刷新旅馆来客快照。
        public override void OnSave(ShopUiContext context)
        {
            if (context is not ShopManagerUiContext shopContext) return;
            Draft draft = context.GetPageState<Draft>(Key);
            if (draft == null) return;
            var manager = HotelMapComponent.For(shopContext.Shop.Map);
            var settings = manager.SettingsFor(shopContext.Shop);
            settings.enabled = draft.enabled;
            settings.acceptRenewals = draft.renewals;
            settings.singlePrice = draft.single;
            settings.doublePrice = draft.dual;
            settings.sleepStartHour = draft.sleepStartHour;
            settings.wakeHour = draft.wakeHour;
            foreach (var bed in settings.registeredBeds.Concat(draft.beds).Distinct().ToList())
            {
                bool register = draft.beds.Contains(bed) && bed != null && bed.Spawned && shopContext.Shop.ContainsCell(bed.Position);
                if (manager.RegisterBed(shopContext.Shop, bed, register, out string reason)) continue;
                if (!manager.IsRegisteredBed(bed)) draft.beds.Remove(bed);
                else draft.beds.Add(bed);
                Messages.Message(reason, bed, MessageTypeDefOf.RejectInput, false);
            }
            manager.NotifyShopChanged(shopContext.Shop);
        }

        //成交合同保留原价，编辑仅影响新报价。
        public override string GetSaveTip(ShopUiContext context) => HotelText.T("RSH.Ui.RoomPricingHint");
    }
}
