using System.Linq;
using RimSimHotelExtension.Buildings;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //取钱机首页保留设施状态，费用与历史分别打开查看。
    public sealed class HotelAtmPage : ShopManagerPageWorker
    {
        private const string Key = "RSH.AtmDraft";
        //费用、限额及排序属于当前窗口草稿。
        private sealed class Draft : PageScroll
        {
            public int fee, limit, sort;
        }

        //断电时仍可进入设置。
        public override bool CanShow(ShopUiContext context)
        {
            var shop = (context as ShopManagerUiContext)?.Shop;
            return base.CanShow(context) && shop?.Map != null && shop.Map.listerBuildings.allBuildingsColonist
                .OfType<Building_HotelAtm>().Any(atm => shop.ContainsCell(atm.Position));
        }

        //首次进入时复制本店取款参数。
        private static Draft State(ShopManagerUiContext context) => context.GetOrCreatePageState(Key, () => {
            var settings = HotelMapComponent.For(context.Shop.Map).SettingsFor(context.Shop);
            return new Draft { fee = settings.atmFee, limit = settings.atmDailyLimit };
        });

        //设施列表使用统一列宽，操作不挤占名称和状态。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            var draft = State(context);
            var manager = HotelMapComponent.For(context.Shop.Map);
            var machines = context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelAtm>()
                .Where(a => context.Shop.ContainsCell(a.Position)).ToList();
            var sorts = new[] { HotelText.T("RSH.Ui.SortName"), HotelText.T("RSH.Ui.UnavailableFirst") };
            HotelUi.Scroll(rect, draft, width => {
                float y = 0;
                HotelUi.Summary(width, ref y, HotelText.T("RSH.Ui.AtmSummary", machines.Count(a => a.Available).Named("READY"),
                    machines.Count.Named("TOTAL"), draft.fee.Named("FEE"), draft.limit.Named("LIMIT")));
                HotelUi.Actions(width, ref y, HotelText.T("RSH.Text.WithdrawalSettings"), () => OpenSettings(draft),
                    HotelText.T("RSH.Ui.WithdrawalHistory"), () => OpenHistory(context, manager));
                float sortWidth = Mathf.Min(width, Text.CalcSize(sorts[draft.sort]).x + 28f);
                if (HotelUi.Button(new Rect(0, y, sortWidth, HotelUi.Line), sorts[draft.sort]))
                    HotelUi.SortMenu(sorts, draft.sort, value => { draft.sort = value; draft.position = Vector2.zero; });
                y += HotelUi.Line + 8f;
                HotelUi.TableHeader(width, ref y, new[] { HotelText.T("RSH.Ui.Facility"), HotelText.T("RSH.Ui.Status") });
                int row = 0;
                foreach (var atm in machines.OrderBy(a => draft.sort == 1 && a.Available ? 1 : 0).ThenBy(a => a.thingIDNumber))
                {
                    if (!HotelUi.Matches(context.SearchText, atm.LabelCap + " " + atm.Position)) continue;
                    HotelUi.TableRow(width, ref y, row++, new[] { atm.LabelCap + " " + atm.Position,
                        HotelText.T(atm.Available ? "RSH.Text.Operational2" : "RSH.Text.UnpoweredSwitchedOff") },
                        atm, () => OpenSettings(draft), HotelText.T("RSH.Ui.Edit"));
                }
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingATMsInThisZone"));
                draft.height = y + 8f;
            });
        }

        //确认仅回写费用和限额草稿，不修改顾客账户。
        private static void OpenSettings(Draft draft)
        {
            int fee = draft.fee, limit = draft.limit;
            string feeBuffer = fee.ToString(), limitBuffer = limit.ToString();
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Text.WithdrawalSettings"), width => {
                float y = 0;
                HotelUi.Number(width, ref y, HotelText.T("RSH.Text.FeePerWithdrawalSilver"), ref fee, ref feeBuffer, 10000);
                HotelUi.Number(width, ref y, HotelText.T("RSH.Text.DailyLimitPerGuestSilver"), ref limit, ref limitBuffer, 10000);
                HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.PrincipalMovesFromTravelAccountsToWalletsOnly"));
                return y;
            }, () => { draft.fee = fee; draft.limit = limit; }));
        }

        //取款历史独立分页显示，沿用主窗口搜索的人名条件。
        private static void OpenHistory(ShopManagerUiContext context, HotelMapComponent manager)
        {
            int page = 0;
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Text.WithdrawalsPrincipalFeeSilver"), width => {
                var allRecords = manager.MoneyRecords.Where(r => r.shopZoneId == context.Shop.ID && r.kind == "withdrawal").ToList();
                var records = allRecords.Where(r => HotelUi.Matches(context.SearchText, r.pawn?.LabelShortCap)).Reverse().ToList();
                int pages = Mathf.Max(1, (records.Count + 9) / 10);
                page = Mathf.Clamp(page, 0, pages - 1);
                float y = 0;
                HotelUi.Summary(width, ref y, HotelText.T("RSH.Ui.WithdrawalSummary", allRecords.Sum(r => r.amount).Named("PRINCIPAL"),
                    allRecords.Sum(r => r.fee).Named("FEES")));
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
            settings.atmFee = draft.fee; settings.atmDailyLimit = draft.limit;
        }

        //手续费生效仍以主窗口保存为准。
        public override string GetSaveTip(ShopUiContext context) => HotelText.T("RSH.Text.SaveFeesAndDailyLimitsAmountsAlreadyWithdrawn");
    }
}
