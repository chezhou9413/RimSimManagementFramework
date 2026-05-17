using RimWorld;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
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

        private enum MenuType { Overview, BusinessHours, ManageGoods, ManageServices, ComboEdit }

        private MenuType curMenu = MenuType.Overview;
        private Zone_Shop shopZone;
        private ComboData curCombo;
        private List<ComboData> zoneCombos;
        private Vector2 sideScroll;
        private Vector2 listScroll;
        private string searchQuery = "";
        private Dictionary<string, GoodsItemData> draftItemData = new Dictionary<string, GoodsItemData>();
        private Dictionary<int, List<ServiceSlotData>> draftServiceData = new Dictionary<int, List<ServiceSlotData>>();
        private List<ThingDef> availableGoodsDefs = new List<ThingDef>();
        private List<Thing> serviceProviders = new List<Thing>();
        private HashSet<string> manageableGoodsCategoryIds = new HashSet<string>();
        private Dictionary<string, string> countBuffers = new Dictionary<string, string>();
        private Dictionary<string, string> priceBuffers = new Dictionary<string, string>();
        private ShopScheduleData draftSchedule;
        private string comboPriceBuf = "";
        private bool priceJustCalculated;

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

            HashSet<Building_SimContainer> storages = ShopDataUtility.GetStoragesInZone(shopZone);
            HashSet<string> addedDefNames = new HashSet<string>();

            foreach (Building_SimContainer storage in storages)
            {
                ThingComp_GoodsData comp = storage.GetComp<ThingComp_GoodsData>();
                if (comp == null) continue;
                RegisterManageableGoodsCategories(comp);

                foreach (KeyValuePair<string, GoodsItemData> kvp in comp.CloneItemData())
                {
                    if (!draftItemData.ContainsKey(kvp.Key))
                        draftItemData[kvp.Key] = kvp.Value;
                }
            }

            foreach (string categoryId in manageableGoodsCategoryIds)
            {
                IReadOnlyList<RuntimeGoodsItem> items = GoodsCatalog.GetItems(categoryId);
                for (int i = 0; i < items.Count; i++)
                {
                    ThingDef def = items[i]?.thingDef;
                    if (def != null && addedDefNames.Add(def.defName))
                        availableGoodsDefs.Add(def);
                }
            }

            availableGoodsDefs = availableGoodsDefs
                .OrderBy(def => def.label)
                .ToList();

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
                        priceOverride = s.priceOverride,
                        maxSimultaneousUsers = s.maxSimultaneousUsers
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// 记录商店级货品上架面板可管理的分类，负责让空货柜和受限货柜也能通过快速设置写入配置。
        /// </summary>
        private void RegisterManageableGoodsCategories(ThingComp_GoodsData comp)
        {
            if (comp == null) return;

            if (!string.IsNullOrEmpty(comp.ActiveGoodsDefName) && comp.AllowsGoodsCategory(comp.ActiveGoodsDefName))
            {
                manageableGoodsCategoryIds.Add(comp.ActiveGoodsDefName);
                return;
            }

            List<string> allowedCategoryIds = comp.GetAllowedGoodsCategoryIds();
            if (!allowedCategoryIds.NullOrEmpty())
            {
                for (int i = 0; i < allowedCategoryIds.Count; i++)
                    manageableGoodsCategoryIds.Add(allowedCategoryIds[i]);
                return;
            }

            foreach (RuntimeGoodsCategory category in GoodsCatalog.Categories ?? Enumerable.Empty<RuntimeGoodsCategory>())
            {
                if (category != null && !string.IsNullOrEmpty(category.categoryId))
                    manageableGoodsCategoryIds.Add(category.categoryId);
            }
        }

        private GoodsItemData GetDraftItem(ThingDef td)
        {
            if (!draftItemData.TryGetValue(td.defName, out GoodsItemData draft))
                draftItemData[td.defName] = draft = new GoodsItemData();
            return draft;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (shopZone == null || !shopZone.Map.zoneManager.AllZones.Contains(shopZone))
            {
                Close();
                return;
            }

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

            if (curMenu == MenuType.Overview) DrawOverviewPanel(panelRect);
            else if (curMenu == MenuType.BusinessHours) DrawBusinessHoursPanel(panelRect);
            else if (curMenu == MenuType.ManageGoods) DrawManagePanel(panelRect);
            else if (curMenu == MenuType.ManageServices) DrawServicesPanel(panelRect);
            else if (curMenu == MenuType.ComboEdit) DrawComboPanel(panelRect);

            GUI.EndGroup();

            priceJustCalculated = false;
            DrawBottomBar(bottomRect);
        }
    }
}
