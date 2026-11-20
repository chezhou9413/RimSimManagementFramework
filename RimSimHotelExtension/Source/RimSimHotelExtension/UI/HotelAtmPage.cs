using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimThingComp;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //取钱机页直接呈现收益统计、内联费率与限额配置以及每台机器的供电和存银状态。
    public sealed class HotelAtmPage : ShopManagerPageWorker
    {
        private const string Key = "RSH.AtmDraft";

        private sealed class Draft : PageScroll
        {
            public int fee, limit, sort;
            public string feeBuf, limitBuf;
        }

        //断电时仍可进入设置。
        public override bool CanShow(ShopUiContext context)
        {
            var shop = (context as ShopManagerUiContext)?.Shop;
            return base.CanShow(context) && shop?.Map != null && shop.Map.listerBuildings.allBuildingsColonist
                .OfType<Building_HotelAtm>().Any(atm => shop.ContainsCell(atm.Position));
        }

        //首次进入时复制本店取款参数。
        private static Draft State(ShopManagerUiContext context) => context.GetOrCreatePageState(Key, () =>
        {
            var settings = HotelMapComponent.For(context.Shop.Map).SettingsFor(context.Shop);
            return new Draft
            {
                fee = settings.atmFee,
                limit = settings.atmDailyLimit,
                feeBuf = settings.atmFee.ToString(),
                limitBuf = settings.atmDailyLimit.ToString()
            };
        });

        //绘制顶部指标、内联费率编辑卡与取钱机设施卡片行。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            var draft = State(context);
            var manager = HotelMapComponent.For(context.Shop.Map);
            var machines = context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelAtm>()
                .Where(a => context.Shop.ContainsCell(a.Position)).ToList();
            var allRecords = manager.MoneyRecords.Where(r => r.shopZoneId == context.Shop.ID && r.kind == "withdrawal").ToList();
            var sorts = new[] { HotelText.T("RSH.Ui.SortName"), HotelText.T("RSH.Ui.UnavailableFirst") };

            HotelUi.Scroll(rect, draft, width =>
            {
                float y = 0f;
                HotelUi.Metrics(width, ref y,
                    new[] { HotelText.T("RSH.Text.AvailableTotalATMs"), HotelText.T("RSH.Text.RecordedPrincipal"),
                        HotelText.T("RSH.Text.RecordedFeeRevenue"), HotelText.T("RSH.Ui.WithdrawalCount") },
                    $"{machines.Count(a => a.Available)} / {machines.Count}",
                    allRecords.Sum(r => r.amount).ToString(),
                    allRecords.Sum(r => r.fee).ToString(),
                    allRecords.Count.ToString());

                DrawControlCard(width, ref y, draft, sorts, context, manager);

                int row = 0;
                foreach (var atm in machines.OrderBy(a => draft.sort == 1 && a.Available ? 1 : 0).ThenBy(a => a.thingIDNumber))
                {
                    if (!HotelUi.Matches(context.SearchText, atm.LabelCap + " " + atm.Position)) continue;
                    DrawAtmRow(width, ref y, row++, atm);
                }
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingATMsInThisZone"));
                draft.height = y + 8f;
            });
        }

        //绘制内联手续费、每日限额输入与取款历史/排序按钮。
        private static void DrawControlCard(float width, ref float y, Draft draft, string[] sorts,
            ShopManagerUiContext context, HotelMapComponent manager)
        {
            float pad = 10f;
            float innerW = Mathf.Max(1f, width - pad * 2f);
            float ctrlH = HotelUi.Line;
            bool stackTwo = innerW < 500f;
            float cardH = pad + (stackTwo ? (ctrlH + 6f) * 2f : ctrlH + 8f) + ctrlH + pad;
            float startY = y;
            ShopUiVisualUtility.DrawSection(new Rect(0f, startY, width, cardH));

            float cy = startY + pad;
            string silverUnit = HotelText.T("RSH.Ui.SilverShort");
            if (stackTwo)
            {
                HotelUi.InlineNumber(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Ui.AtmFeeShort"), ref draft.fee, ref draft.feeBuf, 10000, silverUnit);
                cy += ctrlH + 6f;
                HotelUi.InlineNumber(new Rect(pad, cy, innerW, ctrlH), HotelText.T("RSH.Ui.AtmLimitShort"), ref draft.limit, ref draft.limitBuf, 10000, silverUnit);
                cy += ctrlH + 6f;
            }
            else
            {
                float halfW = (innerW - 12f) * 0.5f;
                HotelUi.InlineNumber(new Rect(pad, cy, halfW, ctrlH), HotelText.T("RSH.Ui.AtmFeeShort"), ref draft.fee, ref draft.feeBuf, 10000, silverUnit);
                HotelUi.InlineNumber(new Rect(pad + halfW + 12f, cy, halfW, ctrlH), HotelText.T("RSH.Ui.AtmLimitShort"), ref draft.limit, ref draft.limitBuf, 10000, silverUnit);
                cy += ctrlH + 8f;
            }

            string historyLabel = HotelText.T("RSH.Ui.WithdrawalHistory");
            string sortLabel = sorts[draft.sort];
            float histW = Mathf.Min(innerW * 0.48f, Mathf.Max(110f, Text.CalcSize(historyLabel).x + 24f));
            float sortW = Mathf.Min(innerW * 0.48f, Mathf.Max(110f, Text.CalcSize(sortLabel).x + 24f));
            if (HotelUi.Button(new Rect(pad, cy, histW, ctrlH), historyLabel))
                OpenHistory(context, manager);
            if (HotelUi.Button(new Rect(width - pad - sortW, cy, sortW, ctrlH), sortLabel))
                HotelUi.SortMenu(sorts, draft.sort, value => { draft.sort = value; draft.position = Vector2.zero; });

            y = startY + cardH + 10f;
        }

        //绘制单台取钱机卡片行，展示图标、通电状态与机内手续费存银。
        private static void DrawAtmRow(float width, ref float y, int index, Building_HotelAtm atm)
        {
            float smallH = ShopUiVisualUtility.LineHeight(GameFont.Small);
            float tinyH = ShopUiVisualUtility.LineHeight(GameFont.Tiny);
            float ctrlH = HotelUi.Line;
            float rowH = smallH + tinyH + 16f;
            ShopUiVisualUtility.DrawTableRowBackground(new Rect(0f, y, width, rowH), index, false);
            ShopUiVisualUtility.DrawBorder(new Rect(0f, y, width, rowH), new Color(1f, 1f, 1f, 0.06f));

            float iconSize = Mathf.Min(34f, rowH - 12f);
            Widgets.ThingIcon(new Rect(8f, y + (rowH - iconSize) * 0.5f, iconSize, iconSize), atm);

            string locateLabel = HotelText.T("RSH.Ui.Locate");
            float locateW = Mathf.Max(54f, Text.CalcSize(locateLabel).x + 16f);
            float textX = 8f + iconSize + 8f;
            float textW = Mathf.Max(80f, width - textX - locateW - 16f);

            string statusText = HotelText.T(atm.Available ? "RSH.Text.Operational2" : "RSH.Text.UnpoweredSwitchedOff");
            float badgeW = Mathf.Min(textW * 0.36f, Mathf.Max(72f, Text.CalcSize(statusText).x + 18f));
            ShopUiVisualUtility.DrawCellLabel(new Rect(textX, y + 6f, Mathf.Max(1f, textW - badgeW - 6f), smallH),
                atm.LabelCap + " " + atm.Position, Color.white);
            HotelUi.Badge(new Rect(textX + textW - badgeW, y + 6f, badgeW, smallH), statusText,
                atm.Available ? HotelUi.PositiveBadgeFill : HotelUi.WarningBadgeFill);

            int stored = atm.GetComp<ThingComp_CashStorage>()?.StoredSilver ?? 0;
            ShopUiVisualUtility.DrawCellLabel(new Rect(textX, y + smallH + 9f, textW, tinyH),
                HotelText.T("RSH.Ui.AtmStoredSilver", stored.Named("SILVER")), ShopUiVisualUtility.MutedText, GameFont.Tiny);

            if (HotelUi.Button(new Rect(width - locateW - 8f, y + (rowH - ctrlH) * 0.5f, locateW, ctrlH), locateLabel, enabled: atm.Spawned))
                CameraJumper.TryJumpAndSelect(atm);

            y += rowH + 4f;
        }

        //取款历史独立分页显示，沿用主窗口搜索的人名条件。
        private static void OpenHistory(ShopManagerUiContext context, HotelMapComponent manager)
        {
            int page = 0;
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Text.WithdrawalsPrincipalFeeSilver"), width =>
            {
                var allRecords = manager.MoneyRecords.Where(r => r.shopZoneId == context.Shop.ID && r.kind == "withdrawal").ToList();
                var records = allRecords.Where(r => HotelUi.Matches(context.SearchText, r.pawn?.LabelShortCap)).Reverse().ToList();
                int pages = Mathf.Max(1, (records.Count + 9) / 10);
                page = Mathf.Clamp(page, 0, pages - 1);
                float y = 0f;
                HotelUi.Summary(width, ref y, HotelText.T("RSH.Ui.WithdrawalSummary", allRecords.Sum(r => r.amount).Named("PRINCIPAL"),
                    allRecords.Sum(r => r.fee).Named("FEES")));
                HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.PrincipalMovesFromTravelAccountsToWalletsOnly"));
                HotelUi.Label(width, ref y, HotelText.T("RSH.Ui.RecordPage", (page + 1).Named("PAGE"), pages.Named("PAGES"), records.Count.Named("COUNT")), true);
                if (pages > 1) HotelUi.Actions(width, ref y, HotelText.T("RSH.Text.PreviousPage"), () => page = Mathf.Max(0, page - 1),
                    HotelText.T("RSH.Text.NextPage"), () => page = Mathf.Min(pages - 1, page + 1));
                int row = 0;
                foreach (var record in records.Skip(page * 10).Take(10))
                    HotelUi.MoneyRow(width, ref y, record.pawn?.LabelShortCap.ToString() ?? HotelText.T("RSH.Text.DepartedGuest"),
                        $"{record.amount} / {record.fee}", HotelText.T("RSH.Ui.RecordDay", (record.tick / 60000 + 1).Named("DAY")), row++);
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingWithdrawalRecords"));
                return y;
            }));
        }

        //统一保存时不触碰已使用的当日额度。
        public override void OnSave(ShopUiContext context)
        {
            if (context is not ShopManagerUiContext shopContext) return;
            var draft = context.GetPageState<Draft>(Key);
            if (draft == null) return;
            var settings = HotelMapComponent.For(shopContext.Shop.Map).SettingsFor(shopContext.Shop);
            settings.atmFee = draft.fee;
            settings.atmDailyLimit = draft.limit;
        }

        //手续费生效仍以主窗口保存为准。
        public override string GetSaveTip(ShopUiContext context) => HotelText.T("RSH.Text.SaveFeesAndDailyLimitsAmountsAlreadyWithdrawn");
    }
}
