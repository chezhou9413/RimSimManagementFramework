using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager : Window
    {
        private const float SidebarW = 190f;
        private const float DivW = 1f;
        private const float RowH = 46f;
        private const float IconSz = 28f;
        private const float CheckSz = 24f;
        private const float FieldW = 68f;
        private const float StockW = 95f;
        private const float SliderW = 115f;
        private const float ColGap = 8f;
        private const float RowPad = 8f;
        private const float HeaderH = 28f;
        private const float BottomH = 52f;
        private const float SearchBarH = 36f;
        private const float ScrW = 16f;

        private static readonly Color CAccent = new Color(0.25f, 0.65f, 0.85f, 1f);
        private static readonly Color CSideBg = new Color(0.10f, 0.11f, 0.12f, 1f);
        private static readonly Color CSideSel = new Color(0.20f, 0.30f, 0.40f, 0.45f);
        private static readonly Color CSideHov = new Color(0.30f, 0.30f, 0.30f, 0.20f);
        private static readonly Color CDivider = new Color(0.28f, 0.28f, 0.28f, 0.5f);
        private static readonly Color CStockOk = new Color(0.35f, 0.80f, 0.45f, 1f);
        private static readonly Color CStockLow = new Color(0.95f, 0.72f, 0.25f, 1f);
        private static readonly Color CStockNo = new Color(0.90f, 0.35f, 0.35f, 1f);
        private static readonly Color CCheckedBg = new Color(0.25f, 0.65f, 0.85f, 0.08f);
        private static readonly Color CHeaderBg = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color CRowAlt = new Color(1f, 1f, 1f, 0.025f);
        private static readonly Color CTextDim = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color CTextMid = new Color(0.78f, 0.78f, 0.78f, 1f);
        private static readonly Color CGold = new Color(0.95f, 0.82f, 0.35f, 1f);

        private const string PageOverview = "Sim_BusinessUi_Shop_Overview";
        private const string PageBusinessHours = "Sim_BusinessUi_Shop_BusinessHours";
        private const string PageManageServices = "Sim_BusinessUi_Shop_Services";
        private const string PageComboEdit = "Sim_BusinessUi_Shop_ComboEdit";

        private readonly List<ShopUiPageDef> uiPages = new List<ShopUiPageDef>();
        private readonly ShopManagerUiContext uiContext = new ShopManagerUiContext();
        private string curPageDefName = PageOverview;
        private Zone_Shop shopZone;
        private ComboData curCombo;
        private List<ComboData> zoneCombos;
        private List<Building_SimContainer> storages = new List<Building_SimContainer>();
        private Vector2 sideScroll;
        private Vector2 listScroll;
        private string searchQuery = "";
        private Dictionary<int, List<ServiceSlotData>> draftServiceData = new Dictionary<int, List<ServiceSlotData>>();
        private List<ThingDef> availableGoodsDefs = new List<ThingDef>();
        private List<Thing> serviceProviders = new List<Thing>();
        private int selectedStorageThingId = -1;
        private ShopScheduleData draftSchedule;
        private string comboPriceBuf = "";
        private bool priceJustCalculated;
        private ComboData cachedCombo;
        private string comboSearchCacheKey = "";
        private int comboStorageCacheSignature = int.MinValue;
        private int comboItemsCacheSignature = int.MinValue;
        private readonly List<ThingDef> comboSellableCache = new List<ThingDef>();
        private readonly Dictionary<ThingDef, ComboItem> comboItemByDefCache = new Dictionary<ThingDef, ComboItem>();
        private readonly Dictionary<string, string> comboItemCountBuffers = new Dictionary<string, string>();
        private readonly Dictionary<ThingDef, float> comboReferencePriceCache = new Dictionary<ThingDef, float>();

        public override Vector2 InitialSize => new Vector2(880f, 660f);

        public Dialog_ShopManager(Zone_Shop zone)
        {
            shopZone = zone;
            doCloseButton = false;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            resizeable = true;
            draggable = true;

            GameComponent_ShopComboManager comboManager = Current.Game.GetComponent<GameComponent_ShopComboManager>();
            zoneCombos = comboManager.GetCombosForZone(zone);
            draftSchedule = zone.GetSchedule().Clone();
            GoodsCatalog.EnsureInitialized();

            storages = ShopDataUtility.GetStoragesInZone(shopZone)
                .Where(storage => storage != null && !storage.Destroyed)
                .OrderBy(storage => storage.StorageDisplayLabel)
                .ThenBy(storage => storage.thingIDNumber)
                .ToList();
            HashSet<string> addedDefNames = new HashSet<string>();

            foreach (Building_SimContainer storage in storages)
            {
                ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                if (comp == null) continue;
                RegisterStorageAvailableGoods(comp, addedDefNames);
            }

            availableGoodsDefs = availableGoodsDefs
                .OrderBy(def => def.label)
                .ToList();
            selectedStorageThingId = storages.FirstOrDefault()?.thingIDNumber ?? -1;

            foreach (Thing provider in ShopServiceUtility.GetServiceProvidersInZone(shopZone))
            {
                ThingComp_ServiceProvider comp = ShopServiceUtility.GetProviderComp(provider);
                if (comp == null) continue;
                comp.EnsureDefaultSlots();
                serviceProviders.Add(provider);
                draftServiceData[provider.thingIDNumber] = comp.serviceSlots
                    .Where(s => s != null)
                    .Select(s => new ServiceSlotData
                    {
                        serviceDefName = s.serviceDefName,
                        enabled = s.enabled,
                        priceOverrideEnabled = s.priceOverrideEnabled,
                        priceOverride = s.priceOverride,
                        maxSimultaneousUsers = s.maxSimultaneousUsers
                    })
                    .ToList();
            }

            uiContext.Window = this;
            uiContext.PageSelector = SwitchPage;
            EnsureUiPages();
        }

        /// <summary>
        /// 为商店总管收集指定货柜可管理的商品定义，负责给套餐页和货柜列表提供稳定的商品全集。
        /// </summary>
        private void RegisterStorageAvailableGoods(ThingComp_GoodsData comp, HashSet<string> addedDefNames)
        {
            if (comp == null || addedDefNames == null) return;

            foreach (string categoryId in GetManageableCategoryIds(comp))
            {
                IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(categoryId);
                for (int i = 0; i < items.Count; i++)
                {
                    ThingDef def = items[i]?.thingDef;
                    if (def != null && addedDefNames.Add(def.defName))
                        availableGoodsDefs.Add(def);
                }
            }
        }

        /// <summary>
        /// 返回指定货柜当前可管理的分类列表，负责兼容已配置分类、受限分类和通用货柜三种情况。
        /// </summary>
        private IEnumerable<string> GetManageableCategoryIds(ThingComp_GoodsData comp)
        {
            if (comp == null)
                yield break;

            HashSet<string> yielded = new HashSet<string>();
            if (!string.IsNullOrEmpty(comp.ActiveGoodsDefName) && comp.AllowsGoodsCategory(comp.ActiveGoodsDefName) && yielded.Add(comp.ActiveGoodsDefName))
                yield return comp.ActiveGoodsDefName;

            List<string> allowedCategoryIds = comp.GetAllowedGoodsCategoryIds();
            if (!allowedCategoryIds.NullOrEmpty())
            {
                for (int i = 0; i < allowedCategoryIds.Count; i++)
                {
                    string categoryId = allowedCategoryIds[i];
                    if (!string.IsNullOrEmpty(categoryId) && yielded.Add(categoryId))
                        yield return categoryId;
                }
                yield break;
            }

            foreach (RuntimeGoodsCategory category in GoodsCatalog.Categories ?? Enumerable.Empty<RuntimeGoodsCategory>())
            {
                if (category != null && !string.IsNullOrEmpty(category.categoryId) && yielded.Add(category.categoryId))
                    yield return category.categoryId;
            }
        }

        /// <summary>
        /// 校正当前选中的货柜编号，负责在货柜被拆除或列表变化后让货柜管理页仍然指向有效目标。
        /// </summary>
        private void EnsureSelectedStorageValid()
        {
            storages = storages
                .Where(storage => storage != null && !storage.Destroyed)
                .OrderBy(storage => storage.StorageDisplayLabel)
                .ThenBy(storage => storage.thingIDNumber)
                .ToList();

            if (storages.Any(storage => storage.thingIDNumber == selectedStorageThingId))
                return;

            selectedStorageThingId = storages.FirstOrDefault()?.thingIDNumber ?? -1;
        }

        /// <summary>
        /// 返回当前在商店总管中选中的货柜，负责让货柜页、套餐页和定位操作复用同一目标。
        /// </summary>
        private Building_SimContainer GetSelectedStorage()
        {
            if (selectedStorageThingId < 0) return null;
            return storages.FirstOrDefault(storage => storage.thingIDNumber == selectedStorageThingId);
        }

        public override void DoWindowContents(Rect inRect)
        {
            PollComboAiNameTask();

            if (shopZone == null || !shopZone.Map.zoneManager.AllZones.Contains(shopZone))
            {
                Close();
                return;
            }

            EnsureSelectedStorageValid();
            EnsureUiPages();
            uiContext.Window = this;
            uiContext.PageSelector = SwitchPage;
            uiContext.CurrentPageDefName = curPageDefName;
            uiContext.WindowRect = inRect;
            uiContext.SearchText = searchQuery;

            Rect contentRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - BottomH);
            Rect sideRect = new Rect(contentRect.x, contentRect.y, SidebarW, contentRect.height);
            Rect dividerRect = new Rect(sideRect.xMax, contentRect.y, DivW, contentRect.height);
            Rect mainRect = new Rect(dividerRect.xMax + 6f, contentRect.y, contentRect.width - SidebarW - DivW - 6f, contentRect.height);
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - BottomH, inRect.width, BottomH);

            DrawSidebar(sideRect);
            Widgets.DrawBoxSolid(dividerRect, CDivider);

            GUI.BeginGroup(mainRect);
            Rect innerRect = mainRect.AtZero();
            DrawSearchBar(new Rect(innerRect.x, innerRect.y, innerRect.width, SearchBarH));
            Rect panelRect = new Rect(innerRect.x, SearchBarH + 4f, innerRect.width, innerRect.height - SearchBarH - 4f);

            DrawCurrentUiPage(panelRect);

            GUI.EndGroup();

            priceJustCalculated = false;
            DrawBottomBar(bottomRect);
        }

        /// <summary>
        /// 确保店铺管理页面缓存有效，负责从 Def/API 拉取当前可见页。
        /// </summary>
        private void EnsureUiPages()
        {
            if (!uiPages.NullOrEmpty() && !SimShopUiApi.ConsumeRefreshRequest())
                return;

            uiPages.Clear();
            uiContext.Window = this;
            uiPages.AddRange(SimShopUiApi.GetPages(ShopUiPageScope.ShopManager, uiContext));
            if (uiPages.Count == 0)
                return;

            if (!uiPages.Any(page => page.defName == curPageDefName))
                curPageDefName = uiPages[0].defName;
            NotifyPageOpened(GetCurrentUiPage());
        }

        /// <summary>
        /// 返回当前选中的 UI 页面 Def。
        /// </summary>
        private ShopUiPageDef GetCurrentUiPage()
        {
            return uiPages.FirstOrDefault(page => page.defName == curPageDefName) ?? uiPages.FirstOrDefault();
        }

        /// <summary>
        /// 绘制当前 UI 页面，负责隔离外部 Worker 异常。
        /// </summary>
        private void DrawCurrentUiPage(Rect rect)
        {
            ShopUiPageDef page = GetCurrentUiPage();
            if (page == null)
            {
                ShopUiLayoutUtility.DrawEmptyState(rect, SimTranslation.TOrFallback("RSMF.ShopUi.Empty.NoPages", "No available pages."));
                return;
            }

            SimShopUiApi.SafeInvoke(page, uiContext, "DrawPage", worker => worker?.DrawPage(rect, uiContext));
            if (uiContext.LastException != null)
                ShopUiLayoutUtility.DrawErrorState(rect, SimTranslation.TOrFallback("RSMF.ShopUi.Error.PageDrawFailed", "Page drawing failed."), uiContext.LastException.Message);
        }

        /// <summary>
        /// 切换到指定页面，负责触发打开生命周期并重置列表滚动。
        /// </summary>
        private void SwitchPage(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return;

            curPageDefName = defName;
            if (defName != PageComboEdit)
                curCombo = null;
            listScroll = Vector2.zero;
            NotifyPageOpened(GetCurrentUiPage());
        }

        /// <summary>
        /// 通知页面已打开，负责触发 Def Worker 的打开生命周期。
        /// </summary>
        private void NotifyPageOpened(ShopUiPageDef page)
        {
            if (page == null) return;
            SimShopUiApi.SafeInvoke(page, uiContext, "OnOpen", worker => worker?.OnOpen(uiContext));
        }

        /// <summary>
        /// 关闭窗口前清理 UI API 上下文和异步请求。
        /// </summary>
        public override void PreClose()
        {
            CancelComboAiNameRequest();
            base.PreClose();
            SimShopUiApi.ClearContext(uiContext);
        }
    }
}
