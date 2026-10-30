using System.Linq;
using RimSimHotelExtension.GameComp;
using RimSimHotelExtension.Spa;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimHotelExtension.UI
{
    //温泉首页展示池位，票价和衣柜明细通过按钮打开。
    public sealed class HotelSpaPage : ShopManagerPageWorker
    {
        private const string Key = "RSH.SpaDraft";
        //页面排序和经营草稿在切换服务时保留。
        private sealed class Draft : PageScroll
        {
            public bool enabled;
            public int wood, stone, sort;
        }

        //设施存在即可配置，断电和暂停接待不隐藏入口。
        public override bool CanShow(ShopUiContext context)
        {
            var shop = (context as ShopManagerUiContext)?.Shop;
            return base.CanShow(context) && shop?.Map != null && shop.Map.listerBuildings.allBuildingsColonist
                .OfType<Building_HotelSpa>().Any(spa => shop.ContainsCell(spa.Position));
        }

        //首次打开时复制本页价格和接待状态。
        private static Draft State(ShopManagerUiContext context) => context.GetOrCreatePageState(Key, () => {
            var settings = HotelMapComponent.For(context.Shop.Map).SettingsFor(context.Shop);
            return new Draft { enabled = settings.spaEnabled, wood = settings.woodSpaPrice, stone = settings.stoneSpaPrice };
        });

        //同屏比较各池状态、占用和排队，衣柜列表另开窗口。
        public override void DrawShopPage(Rect rect, ShopManagerUiContext context)
        {
            using var scope = new HotelUi();
            var draft = State(context);
            var spas = context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelSpa>()
                .Where(b => context.Shop.ContainsCell(b.Position)).ToList();
            var lockers = context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelLocker>()
                .Where(b => context.Shop.ContainsCell(b.Position)).ToList();
            var manager = SpaMapComponent.For(context.Shop.Map);
            var sorts = new[] { HotelText.T("RSH.Ui.SortName"), HotelText.T("RSH.Ui.UnavailableFirst"), HotelText.T("RSH.Ui.BusiestFirst") };
            HotelUi.Scroll(rect, draft, width => {
                float y = 0;
                HotelUi.Summary(width, ref y, HotelText.T("RSH.Ui.SpaSummary", spas.Sum(s => manager.OccupiedCount(s)).Named("USED"),
                    spas.Sum(s => s.Capacity).Named("TOTAL"), spas.Sum(s => manager.WaitingCount(s)).Named("QUEUE"),
                    lockers.Count(l => l.Available).Named("FREE"), lockers.Count.Named("LOCKERS")));
                HotelUi.Actions(width, ref y, HotelText.T("RSH.Ui.SpaSettings"), () => OpenSettings(draft),
                    HotelText.T("RSH.Text.ApparelLockers"), () => OpenLockers(context));
                float sortWidth = Mathf.Min(width, Text.CalcSize(sorts[draft.sort]).x + 28f);
                if (HotelUi.Button(new Rect(0, y, sortWidth, HotelUi.Line), sorts[draft.sort]))
                    HotelUi.SortMenu(sorts, draft.sort, value => { draft.sort = value; draft.position = Vector2.zero; });
                y += HotelUi.Line + 8f;
                HotelUi.TableHeader(width, ref y, new[] { HotelText.T("RSH.Ui.Facility"), HotelText.T("RSH.Ui.Status"),
                    HotelText.T("RSH.Ui.SlotsQueue"), HotelText.T("RSH.Ui.Price") });
                int row = 0;
                foreach (var spa in spas.OrderBy(s => draft.sort == 1 ? (s.Operational ? 1 : 0)
                    : draft.sort == 2 ? -manager.OccupiedCount(s) : 0).ThenBy(s => s.LabelCap.ToString()).ThenBy(s => s.thingIDNumber))
                {
                    if (!HotelUi.Matches(context.SearchText, spa.LabelCap + " " + spa.Position)) continue;
                    HotelUi.TableRow(width, ref y, row++, new[] { spa.LabelCap + " " + spa.Position,
                        HotelText.T(spa.Operational ? "RSH.Ui.Open" : "RSH.Ui.Unavailable"),
                        $"{manager.OccupiedCount(spa)}/{spa.Capacity} · {manager.WaitingCount(spa)}",
                        (spa.Stone ? draft.stone : draft.wood).ToString() }, spa, () => OpenSettings(draft), HotelText.T("RSH.Ui.Edit"));
                }
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingHotSpringsInThisZone"));
                draft.height = y + 8f;
            });
        }

        //票价编辑保留独立副本，确认才更新父页草稿。
        private static void OpenSettings(Draft draft)
        {
            bool enabled = draft.enabled;
            int wood = draft.wood, stone = draft.stone;
            string woodBuffer = wood.ToString(), stoneBuffer = stone.ToString();
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Ui.SpaSettings"), width => {
                float y = 0;
                HotelUi.Check(width, ref y, HotelText.T("RSH.Text.AcceptSpaAdmissions"), ref enabled);
                HotelUi.Number(width, ref y, HotelText.T("RSH.Text.WoodenSpaSilverAdmission"), ref wood, ref woodBuffer, 10000);
                HotelUi.Number(width, ref y, HotelText.T("RSH.Text.StoneSpaSilverAdmission"), ref stone, ref stoneBuffer, 10000);
                HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.PaymentFollowsReservationOfABathingSlotAnd"));
                return y;
            }, () => { draft.enabled = enabled; draft.wood = wood; draft.stone = stone; }));
        }

        //衣柜明细只读，不将全部存衣条目堆在温泉首页。
        private static void OpenLockers(ShopManagerUiContext context)
        {
            Find.WindowStack.Add(new Dialog_HotelDetails(HotelText.T("RSH.Text.ApparelLockers"), width => {
                float y = 0;
                int row = 0;
                HotelUi.TableHeader(width, ref y, new[] { HotelText.T("RSH.Ui.Facility"), HotelText.T("RSH.Text.OccupantApparelCount") });
                foreach (var locker in context.Shop.Map.listerBuildings.allBuildingsColonist.OfType<Building_HotelLocker>()
                    .Where(l => context.Shop.ContainsCell(l.Position)).OrderBy(l => l.thingIDNumber))
                    HotelUi.TableRow(width, ref y, row++, new[] { locker.LabelCap + " " + locker.Position,
                        (locker.Occupant?.LabelShortCap.ToString() ?? HotelText.T("RSH.Text.Vacant")) + " · " + locker.ClothingCount }, locker);
                if (row == 0) HotelUi.Notice(width, ref y, HotelText.T("RSH.Text.NoMatchingApparelLockersInThisZone"));
                return y;
            }));
        }

        //保存只修改新温泉会话的配置。
        public override void OnSave(ShopUiContext context)
        {
            if (context is not ShopManagerUiContext shopContext) return;
            var draft = context.GetPageState<Draft>(Key);
            if (draft == null) return;
            var settings = HotelMapComponent.For(shopContext.Shop.Map).SettingsFor(shopContext.Shop);
            settings.spaEnabled = draft.enabled; settings.woodSpaPrice = draft.wood; settings.stoneSpaPrice = draft.stone;
        }

        //已开始的会话不追溯改价。
        public override string GetSaveTip(ShopUiContext context) => HotelText.T("RSH.Text.SaveAdmissionSettingsAndPricesForNewSessions");
    }
}
