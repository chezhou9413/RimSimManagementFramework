using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Tool;
using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Buildings
{
    //保存餐厅专用库存，职责是隔离零售、限制后厨补货并承载冷藏和商品规则。
    public sealed class Building_RestaurantStorage : Building_SimContainer
    {
        public List<RestaurantStockRule> saleRules = new List<RestaurantStockRule>();
        public RestaurantStorageExtension Settings => def.GetModExtension<RestaurantStorageExtension>();
        public bool IsRefrigerator => Settings.refrigerated;
        public bool IsCooling => IsRefrigerator && GetComp<CompPowerTrader>()?.PowerOn == true;
        public Zone_Shop Shop => SimShopServiceApi.FindShop(Map, Position);
        public string CatalogId => "RestaurantStorage/" + def.defName;
        public ThingComp_GoodsData Goods => GetComp<ThingComp_GoodsData>();
        public override bool AllowsCustomerSelfPurchase => false;
        public override IEnumerable<ThingDef> ActiveDefs => GoodsCatalog.GetItems(CatalogId).Select(i => i.thingDef);
        public override LocalTargetInfo InventoryInteractionTarget => Settings.wallMounted ? (LocalTargetInfo)Position : this;
        public override PathEndMode InventoryInteractionEndMode => Settings.wallMounted ? PathEndMode.OnCell : PathEndMode.Touch;
        public override string RestockSourceIssue => Shop == null ? "货柜未放在餐厅商店区域内"
            : RestaurantStockUtility.Pantries(Shop).Any() ? "" : "本店未绑定有效的后厨储存区";

        //判断物品是否属于建筑配置并被玩家允许。
        public override bool AllowsInventoryItem(ThingDef item)
        {
            return Settings.products.Any(p => p.Allows(item)) && Goods.FindItemData(item)?.enabled == true;
        }

        //限制补货来源，职责是只允许从本店绑定的原版储存区搬运。
        public override bool AllowsRestockSource(Thing source)
        {
            return source?.Spawned == true && source.Map == Map && Shop != null
                && RestaurantStockUtility.Pantries(Shop).Any(z => z.ContainsCell(source.Position));
        }

        //查找商品默认规则，职责是按建筑 XML 配置顺序解析重叠商品范围。
        public RestaurantProductDef Product(ThingDef item) => Settings.products.FirstOrDefault(p => p.Allows(item));

        //查找或创建未上架规则，职责是避免未配置商品自动销售。
        public RestaurantStockRule Rule(ThingDef item)
        {
            var rule = saleRules.FirstOrDefault(r => r.item == item);
            if (rule == null)
            {
                var product = Product(item);
                rule = new RestaurantStockRule { item = item, mode = product.deliveryMode, portions = product.defaultCount };
                saleRules.Add(rule);
            }
            return rule;
        }

        //登记货柜目录，职责是在补货协调器读取配置前建立商品分类。
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            RegisterCatalog();
            Goods.ActiveGoodsDefName = CatalogId;
            base.SpawnSetup(map, respawningAfterLoad);
        }

        //注册建筑允许的商品目录，职责是复用框架筛选和逐项补货目标。
        public void RegisterCatalog()
        {
            GoodsCatalog.RegisterCategory(CatalogId, LabelCap, DefDatabase<ThingDef>.AllDefsListForReading
                .Where(t => t.category == ThingCategory.Item && t.EverHaulable && Settings.products.Any(p => p.Allows(t))), replace: true);
        }

        //保存额外销售规则，职责是不改变物品自身的品质、成分或腐坏进度。
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref saleRules, "restaurantSaleRules", LookMode.Deep);
        }

        //创建独立草稿窗口，职责是集中管理库存和上架规则。
        protected override Window CreateManagementWindow() => new UI.Dialog_RestaurantStorage(this);

        //显示用途、供电和后厨配置阻塞原因。
        protected override string BuildContainerInspectString(string baseStr)
        {
            return base.BuildContainerInspectString(baseStr) + "\n"
                + (IsRefrigerator ? IsCooling ? "厨房冷藏库存：通电保鲜" : "厨房冷藏库存：断电，按环境温度腐坏" : "餐厅配送库存")
                + (RestockSourceIssue.NullOrEmpty() ? "" : "\n" + RestockSourceIssue);
        }
    }
}
