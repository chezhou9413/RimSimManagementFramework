using System.Collections.Generic;
using System.Linq;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Spa;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //温泉页提供内联开关、票价配置、池位进度条与实时泡汤客清单，衣柜详情可一键查看。
    public sealed class HotelSpaPage : ShopManagerPageWorker
    {
        private const string Key = "RSH.SpaDraft";

        private sealed class Draft : PageScroll
        {
            public bool enabled;
            public int wood, stone, sort;
            public string woodBuf, stoneBuf;
        }

        //设施存在即可配置，断电和暂停接待不隐藏入口。
        public override bool CanShow(ShopUiContext context)
        {
            var shop = (context as ShopManagerUiContext)?.Shop;
            return base.CanShow(context) && shop?.Map != null && shop.Map.listerBuildings.allBuildingsColonist
                .OfType<Building_HotelSpa>().Any(spa => shop.ContainsCell(spa.Position));
        }

        //首次打开时复制本页价格和接待状态。
        private static Draft State(ShopManagerUiContext context) => context.GetOrCreatePageState(Key, () =>
        {
            var settings = HotelMapComponent.For(context.Shop.Map).SettingsFor(context.Shop);
            return new Draft
            {
                enabled = settings.spaEnabled,
                wood = settings.woodSpaPrice,
                stone = settings.stoneSpaPrice,
                woodBuf = settings.woodSpaPrice.ToString(),
                stoneBuf = settings.stoneSpaPrice.ToString()
            };
        });

        //绘制顶部指标、内联开关与票价卡、温泉池进度行。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            var draft = State(context);
            var spas = context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelSpa>()
                .Where(b => context.Shop.ContainsCell(b.Position)).ToList();
            var lockers = context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelLocker>()
                .Where(b => context.Shop.ContainsCell(b.Position)).ToList();
            var manager = SpaMapComponent.For(context.Shop.Map);
            var sorts = new[]
            {
                HotelText.T("RSH.Ui.SortName"),
                HotelText.T("RSH.Ui.UnavailableFirst"),
                HotelText.T("RSH.Ui.BusiestFirst")
            };

            HotelUi.Scroll(rect, draft, width =>
            {
                float y = 0f;
                int usedSlots = spas.Sum(s => manager.OccupiedCount(s));
                int totalSlots = spas.Sum(s => s.Capacity);
                int waiting = spas.Sum(s => manager.WaitingCount(s));
                int freeLockers = lockers.Count(l => l.Available);

                HotelUi.Metrics(width, ref y,
                    new[] { HotelText.T("RSH.Text.OccupiedTotalSlots"), HotelText.T("RSH.Text.WaitingCustomers"),
                        HotelText.T("RSH.Text.FreeTotalLockers"), HotelText.T("RSH.Ui.Status") },
                    $"{usedSlots} / {totalSlots}", waiting.ToString(), $"{freeLockers} / {lockers.Count}",
                    HotelText.T(draft.enabled ? "RSH.Ui.Open" : "RSH.Ui.Paused"));

                DrawControlCard(width, ref y, draft, sorts, context);

                if (lockers.Count == 0)
                    HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.TheSpaNeedsAnApparelLocker"), true);

                int row = 0;
                foreach (var spa in spas.OrderBy(s => draft.sort == 1 ? (s.Operational ? 1 : 0)
                    : draft.sort == 2 ? -manager.OccupiedCount(s) : 0).ThenBy(s => s.LabelCap.ToString()).ThenBy(s => s.thingIDNumber))
                {
                    if (!HotelUi.Matches(context.SearchText, spa.LabelCap + " " + spa.Position)) continue;
                    DrawSpaRow(width, ref y, row++, spa, draft, manager);
                }
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingHotSpringsInThisZone"));
                draft.height = y + 8f;
            });
        }

        //绘制内联开关、木制/石制票价输入框与衣柜/排序按钮。
        private static void DrawControlCard(float width, ref float y, Draft draft, string[] sorts, ShopManagerUiContext context)
        {
            float pad = 10f;
            float innerW = Mathf.Max(1f, width - pad * 2f);
            float ctrlH = HotelUi.Line;
            bool stackTwo = innerW < 500f;
            float cardH = pad + ctrlH + 8f + (stackTwo ? (ctrlH + 6f) * 2f : ctrlH + 8f) + ctrlH + pad;
            float startY = y;
            ShopUiVisualUtility.DrawSection(new Rect(0f, startY, width, cardH));

            float cy = startY + pad;
            if (HotelUi.TogglePill(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Text.AcceptSpaAdmissions"), draft.enabled,
                HotelText.T("RSH.Ui.Open"), HotelText.T("RSH.Ui.Paused"), HotelText.T("RSH.Text.PaymentFollowsReservationOfABathingSlotAnd")))
                draft.enabled = !draft.enabled;
            cy += ctrlH + 8f;

            string unit = HotelText.T("RSH.Ui.SilverPerVisitShort");
            if (stackTwo)
            {
                HotelUi.InlineNumber(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Ui.WoodSpaShort"), ref draft.wood, ref draft.woodBuf, 10000, unit);
                cy += ctrlH + 6f;
                HotelUi.InlineNumber(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Ui.StoneSpaShort"), ref draft.stone, ref draft.stoneBuf, 10000, unit);
                cy += ctrlH + 6f;
            }
            else
            {
                float halfW = (innerW - 12f) * 0.5f;
                HotelUi.InlineNumber(new Rect(pad, cy, halfW, ctrlH), HotelText.T("RSH.Ui.WoodSpaShort"), ref draft.wood, ref draft.woodBuf, 10000, unit);
                HotelUi.InlineNumber(new Rect(pad + halfW + 12f, cy, halfW, ctrlH), HotelText.T("RSH.Ui.StoneSpaShort"), ref draft.stone, ref draft.stoneBuf, 10000, unit);
                cy += ctrlH + 8f;
            }

            string lockerLabel = HotelText.T("RSH.Text.ApparelLockers");
            string sortLabel = sorts[draft.sort];
            float lockerW = Mathf.Min(innerW * 0.48f, Mathf.Max(110f, Text.CalcSize(lockerLabel).x + 24f));
            float sortW = Mathf.Min(innerW * 0.48f, Mathf.Max(110f, Text.CalcSize(sortLabel).x + 24f));
            if (HotelUi.Button(new Rect(pad, cy, lockerW, ctrlH), lockerLabel))
                OpenLockers(context);
            if (HotelUi.Button(new Rect(width - pad - sortW, cy, sortW, ctrlH), sortLabel))
                HotelUi.SortMenu(sorts, draft.sort, value => { draft.sort = value; draft.position = Vector2.zero; });

            y = startY + cardH + 10f;
        }

        //绘制单个温泉池卡片行，含图标、状态徽章、池位进度条与在池住客名。
        private static void DrawSpaRow(float width, ref float y, int index, Building_HotelSpa spa, Draft draft, SpaMapComponent manager)
        {
            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float barH = tinyH + 4f;
            float ctrlH = HotelUi.Line;
            float rowH = smallH + barH + 18f;
            ShopUiVisualUtility.DrawTableRowBackground(new Rect(0f, y, width, rowH), index, false);
            ShopUiVisualUtility.DrawBorder(new Rect(0f, y, width, rowH), new Color(1f, 1f, 1f, 0.06f));

            float iconSize = Mathf.Min(36f, rowH - 12f);
            Widgets.ThingIcon(new Rect(8f, y + (rowH - iconSize) * 0.5f, iconSize, iconSize), spa);

            string locateLabel = HotelText.T("RSH.Ui.Locate");
            float locateW = Mathf.Max(54f, Text.CalcSize(locateLabel).x + 16f);
            float textX = 8f + iconSize + 8f;
            float textW = Mathf.Max(80f, width - textX - locateW - 16f);

            int used = manager.OccupiedCount(spa);
            int waiting = manager.WaitingCount(spa);
            int price = spa.Stone ? draft.stone : draft.wood;
            string statusText = HotelText.T(spa.Operational ? "RSH.Ui.Open" : "RSH.Ui.Unavailable");
            float badgeW = Mathf.Min(textW * 0.34f, Mathf.Max(64f, Text.CalcSize(statusText).x + 18f));
            ShopUiVisualUtility.DrawCellLabel(new Rect(textX, y + 6f, Mathf.Max(1f, textW - badgeW - 6f), smallH),
                $"{spa.LabelCap} · {price}{HotelText.T("RSH.Ui.SilverPerVisitShort")}", Color.white);
            HotelUi.Badge(new Rect(textX + textW - badgeW, y + 6f, badgeW, smallH), statusText,
                spa.Operational ? HotelUi.PositiveBadgeFill : HotelUi.WarningBadgeFill);

            float barW = Mathf.Min(170f, textW * 0.45f);
            float fill = spa.Capacity <= 0 ? 0f : used / (float)spa.Capacity;
            HotelUi.ProgressBar(new Rect(textX, y + smallH + 10f, barW, barH), fill,
                $"{used}/{spa.Capacity} · {HotelText.T("RSH.Text.Waiting")} {waiting}",
                spa.Operational ? ShopUiVisualUtility.Accent : ShopUiVisualUtility.WarningText);

            var bathers = manager.ActiveSessions.Where(s => s.spa == spa && s.customer != null)
                .Select(s => s.customer.LabelShortCap.ToString()).Take(4).ToList();
            string bathersText = bathers.Count > 0 ? string.Join(", ", bathers) : HotelText.T("RSH.Text.Vacant");
            ShopUiVisualUtility.DrawCellLabel(new Rect(textX + barW + 8f, y + smallH + 10f, Mathf.Max(1f, textW - barW - 8f), barH),
                bathersText, ShopUiVisualUtility.MutedText, GameFont.Tiny);

            if (HotelUi.Button(new Rect(width - locateW - 8f, y + (rowH - ctrlH) * 0.5f, locateW, ctrlH), locateLabel, enabled: spa.Spawned))
                CameraJumper.TryJumpAndSelect(spa);

            y += rowH + 4f;
        }

        //衣柜明细只读，不将全部存衣条目堆在温泉首页。
        private static void OpenLockers(ShopManagerUiContext context)
        {
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Text.ApparelLockers"), width =>
            {
                float y = 0f;
                int row = 0;
                HotelUi.TableHeader(width, ref y, new[] { HotelText.T("RSH.Ui.Facility"), HotelText.T("RSH.Text.OccupantApparelCount") });
                foreach (var locker in context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelLocker>()
                    .Where(l => context.Shop.ContainsCell(l.Position)).OrderBy(l => l.thingIDNumber))
                    HotelUi.TableRow(width, ref y, row++, new[]
                    {
                        locker.LabelCap + " " + locker.Position,
                        (locker.Occupant?.LabelShortCap.ToString() ?? HotelText.T("RSH.Text.Vacant")) + " · " + locker.ClothingCount
                    }, locker);
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingApparelLockersInThisZone"));
                return y;
            }));
        }

        //保存只修改新温泉会话的配置，并同步刷新商店来客快照。
        public override void OnSave(ShopUiContext context)
        {
            if (context is not ShopManagerUiContext shopContext) return;
            var draft = context.GetPageState<Draft>(Key);
            if (draft == null) return;
            var manager = HotelMapComponent.For(shopContext.Shop.Map);
            var settings = manager.SettingsFor(shopContext.Shop);
            settings.spaEnabled = draft.enabled;
            settings.woodSpaPrice = draft.wood;
            settings.stoneSpaPrice = draft.stone;
            manager.NotifyShopChanged(shopContext.Shop);
        }

        //已开始的会话不追溯改价。
        public override string GetSaveTip(ShopUiContext context) => HotelText.T("RSH.Text.SaveAdmissionSettingsAndPricesForNewSessions");
    }
}
