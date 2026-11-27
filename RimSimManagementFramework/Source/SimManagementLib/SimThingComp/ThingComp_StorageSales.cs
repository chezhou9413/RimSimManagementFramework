using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingComp
{
    //在宿主仓储上保存经营设置，所有库存读写仍通过宿主完成。
    public sealed partial class ThingComp_StorageSales : ThingComp, IShopStorage
    {
        private bool salesEnabled;
        private Dictionary<ThingDef, GoodsItemData> rules = new();
        private int stockVersion;
        private int lastStockSignature;
        private bool lastActive;

        internal CompProperties_StorageSales SalesProps => (CompProperties_StorageSales)props;
        public Building Host => (Building)parent;
        public bool SalesEnabled => salesEnabled;
        public bool AllowsCustomerSelfPurchase => salesEnabled && parent.Spawned && parent.Faction == Faction.OfPlayer
            && parent.Map.zoneManager.ZoneAt(parent.Position) is Zone_Shop && HasInventory;
        public int StoredCountVersion => stockVersion;
        public LocalTargetInfo InventoryInteractionTarget => parent.def.hasInteractionCell ? (LocalTargetInfo)parent.InteractionCell : parent;
        public PathEndMode InventoryInteractionEndMode => parent.def.hasInteractionCell ? PathEndMode.OnCell : PathEndMode.Touch;
        public string RestockSourceIssue => "";
        internal IEnumerable<ThingDef> RestockDefs => rules.Where(p => p.Value.enabled && p.Value.count > 0 && SalesProps.AllowsListing(p.Key)).Select(p => p.Key);
        internal IEnumerable<ThingDef> ConfiguredDefs => rules.Keys.Where(SalesProps.AllowsListing);
        internal IEnumerable<ThingDef> ManagedDefs => StockIndex.Keys.Concat(rules.Keys).Distinct().Where(SalesProps.AllowsListing);
        public IEnumerable<ThingDef> ActiveDefs => AllowsCustomerSelfPurchase ? ManagedDefs.Where(OnSale) : Enumerable.Empty<ThingDef>();

        //开启整柜销售后，未单独设置的品种自动上架。
        internal GoodsItemData Rule(ThingDef def) => rules.TryGetValue(def, out var data) ? data : null;
        internal bool OnSale(ThingDef def) => SalesProps.AllowsListing(def) && (Rule(def)?.enabled ?? true);

        //目标量只触发店员补货，不限制宿主正常入库。
        public int GetTargetCount(ThingDef def) => AllowsCustomerSelfPurchase && OnSale(def) && AllowsDef(def) ? Rule(def)?.count ?? 0 : 0;
        public int GetRestockThreshold(ThingDef def) => Rule(def)?.EffectiveRestockThreshold ?? 0;

        //以当前实际物品的市价或手动品种价计算单件报价。
        public float Price(Thing thing)
        {
            float configured = Rule(thing.def)?.price ?? 0f;
            return ShopPricingUtility.ApplyCashierPrice(ShopDataUtility.FindShopZone(parent.Map, parent.Position),
                configured > 0f ? configured : thing.MarketValue, 0f);
        }

        //保存这座建筑的品种设置，同步清理已移除的规则并让营业与补货读取新值。
        internal void ApplyRules(Dictionary<ThingDef, GoodsItemData> values)
        {
            List<ThingDef> removable = rules.Keys.Where(SalesProps.AllowsListing).ToList();
            for (int i = 0; i < removable.Count; i++) rules.Remove(removable[i]);
            foreach (var entry in values)
                if (SalesProps.AllowsListing(entry.Key)) rules[entry.Key] = entry.Value.Copy();
            NotifyStockChanged();
        }

        //只修改批准的品种，保留其他商品的设置。
        internal void ApplyRule(ThingDef def, GoodsItemData value)
        {
            rules[def] = value;
            NotifyStockChanged();
        }

        //切换整柜销售，启售时主动修补被包围或部分覆盖的商店区划并刷新在途状态。
        internal void ToggleSales()
        {
            salesEnabled = !salesEnabled;
            ShopDataUtility.NotifyBuildingChanged(Host);
            NotifyStockChanged();
        }

        //销售开关和品种设置跟随建筑存档。
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref salesEnabled, "rsmfStorageSalesEnabled", false);
            Scribe_Collections.Look(ref rules, "rsmfStorageSalesRules", LookMode.Def, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) rules ??= new();
        }

        //登记宿主以便现有地图队列分摊检查原版搬运产生的变动。
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (parent is Building_SimContainer) return;
            parent.Map.GetComponent<MapComponent_RestockTaskQueue>().RegisterStorage(this, "仓储销售组件生成");
            ShopDataUtility.NotifyBuildingChanged(Host);
        }

        //宿主负责处理实物，组件只注销经营和派工状态。
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            if (parent is Building_SimContainer) return;
            map.GetComponent<MapComponent_RestockTaskQueue>().UnregisterStorage(this, "仓储销售组件移除");
            ShopDataUtility.NotifyBuildingChanged(map, parent.Position);
        }

        //沿用地图队列的分摊巡检发现外部并栈、移货和商店区变化。
        internal void PollHostStock()
        {
            bool active = AllowsCustomerSelfPurchase;
            int signature = 0;
            if (active)
            {
                foreach (Thing t in StockThings)
                    signature = unchecked(signature + HashCode.Combine(t.thingIDNumber, t.stackCount, OnSale(t.def) ? UnreservedCount(t) : -1));
                foreach (var rule in rules)
                    if (rule.Value.count > 0 && AllowsDef(rule.Key)) signature = unchecked(signature + rule.Key.shortHash);
            }
            if (active == lastActive && signature == lastStockSignature) return;
            if (lastActive != active) ShopDataUtility.NotifyBuildingChanged(parent.Map, parent.Position);
            lastActive = active;
            lastStockSignature = signature;
            NotifyStockChanged();
        }

        //立即刷新相关商店库存与补货配置。
        internal void NotifyStockChanged(ThingDef item = null)
        {
            indexDirty = true;
            stockVersion++;
            if (!parent.Spawned) return;
            var queue = parent.Map.GetComponent<MapComponent_RestockTaskQueue>();
            if (item == null) queue.MarkStorageDirty(this, "宿主库存或销售设置变化");
            else queue.MarkDirty(this, item, "宿主商品数量变化");
            ShopDataUtility.NotifyShopContentsChanged(Host);
        }

        //建筑按钮与商店管理共用此窗口，避免同一货柜重复叠加多个窗口。
        public void OpenInventoryManagement()
        {
            if (Find.WindowStack.Windows.OfType<Dialog_StorageSales>().Any(window => window.Matches(this))) return;
            Find.WindowStack.Add(new Dialog_StorageSales(this));
        }

        //仅在开启货柜销售时显示简要营业状态，避免干扰未启售的普通储物架。
        public override string CompInspectStringExtra()
        {
            if (parent.Faction != Faction.OfPlayer || parent is Building_SimContainer || !salesEnabled) return null;
            if (!parent.Spawned || parent.Map.zoneManager.ZoneAt(parent.Position) is not Zone_Shop)
                return SimTranslation.T("RSMF.StorageSales.Inspect.NotInShop");
            if (SalesProps.maxListedCount >= 0)
            {
                int listedCount = ListedStock().Sum(stack => stack.Count);
                return SimTranslation.T("RSMF.StorageSales.Inspect.ActiveWithCap",
                    listedCount.Named("listed"), SalesProps.maxListedCount.Named("capacity"));
            }
            return SimTranslation.T("RSMF.StorageSales.Inspect.Active");
        }

        //为玩家建筑提供框架风格的逐柜启售和管理按钮。
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || parent is Building_SimContainer) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = SimTranslation.T("RSMF.StorageSales.Toggle"),
                defaultDesc = SimTranslation.T("RSMF.StorageSales.ToggleDesc"),
                icon = StorageSalesIcons.SalesToggle,
                isActive = () => salesEnabled,
                toggleAction = ToggleSales
            };
            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.Gizmo.ContainerManagement.Label"),
                defaultDesc = SimTranslation.T("RSMF.StorageSales.ManageDesc"),
                icon = StorageSalesIcons.ManageStorage,
                alsoClickIfOtherInGroupClicked = false,
                action = OpenInventoryManagement
            };
        }
    }
}
