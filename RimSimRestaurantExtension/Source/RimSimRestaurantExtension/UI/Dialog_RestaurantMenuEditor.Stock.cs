using System.Globalization;
using System.Linq;
using RimSimRestaurantExtension.Buildings;
using RimSimRestaurantExtension.Inventory;
using RimSimRestaurantExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.UI
{
    //在添加菜品窗口中配置现货，保存时直接使用货柜原有库存和销售规则。
    internal sealed partial class Dialog_RestaurantMenuEditor
    {
        private bool stockMode;
        private Building_RestaurantStorage stockCabinet;
        private ThingDef stockItem;
        private string stockPrice = "";
        private string stockPortions = "1";
        private string stockTarget = "1";
        private bool stockOnSale = true;
        private RestaurantDeliveryMode stockDelivery;

        //让新菜品明确选择厨房制作或销售现货，切换不提交任何设置。
        private void DrawSupplyMode(Rect rect)
        {
            float width = (rect.width - 8f) / 2f;
            if (ShopUiVisualUtility.DrawTabButton(new Rect(rect.x, rect.y, width, rect.height),
                SimTranslation.T("RSR.UI.KitchenSource"), !stockMode, RestaurantUiStyle.MutedText))
            {
                stockMode = false;
                error = "";
                scroll = Vector2.zero;
            }
            if (ShopUiVisualUtility.DrawTabButton(new Rect(rect.x + width + 8f, rect.y, width, rect.height),
                SimTranslation.T("RSR.UI.SellStock"), stockMode, RestaurantUiStyle.MutedText))
            {
                stockMode = true;
                error = "";
                scroll = Vector2.zero;
                var cabinets = RestaurantStockUtility.Cabinets(shop).Where(c => !c.IsRefrigerator).ToList();
                if (stockCabinet == null && cabinets.Count == 1) stockCabinet = cabinets[0];
            }
        }

        //绘制现货的来源、售价、补货目标和交付方式，现货无需设置厨房配方。
        private void DrawStockForm(float width, float row, string note, float noteHeight)
        {
            float y = 0f;
            ShopUiVisualUtility.DrawCellLabel(new Rect(0f, y, 110f, row - 8f), SimTranslation.T("RSR.UI.StockCabinet"));
            Rect cabinetField = new Rect(118f, y, width - 118f, row - 8f);
            string cabinetLabel = stockCabinet == null ? SimTranslation.T("RSR.UI.SelectStockCabinet") : CabinetLabel(stockCabinet);
            TooltipHandler.TipRegion(cabinetField, cabinetLabel);
            if (RestaurantUiStyle.DrawSecondaryButton(cabinetField, cabinetLabel.Truncate(cabinetField.width - 12f))) SelectStockCabinet();
            y += row;
            ShopUiVisualUtility.DrawCellLabel(new Rect(0f, y, 110f, row - 8f), SimTranslation.T("RSR.UI.StockProduct"));
            RestaurantUiStyle.DrawThingIconOrMissing(new Rect(118f, y, row - 8f, row - 8f), stockItem);
            ShopUiVisualUtility.DrawCellLabel(new Rect(164f, y, width - 268f, row - 8f),
                stockItem?.LabelCap.ToString() ?? SimTranslation.T("RSR.UI.NotSelected"));
            if (RestaurantUiStyle.DrawSecondaryButton(new Rect(width - 96f, y, 96f, row - 8f),
                SimTranslation.T("RSR.UI.SelectProduct"), stockCabinet != null)) OpenStockFoods();
            y += row;
            stockPrice = Field(width, y, row, SimTranslation.T("RSR.UI.BasePrice"), stockPrice); y += row;
            stockPortions = Field(width, y, row, SimTranslation.T("RSR.UI.StockOrderPortions"), stockPortions); y += row;
            stockTarget = Field(width, y, row, SimTranslation.T("RSR.UI.StockTarget"), stockTarget); y += row;
            ShopUiVisualUtility.DrawCellLabel(new Rect(0f, y, 110f, row - 8f), SimTranslation.T("RSR.UI.Delivery"));
            if (RestaurantUiStyle.DrawSecondaryButton(new Rect(118f, y, width - 118f, row - 8f),
                SimTranslation.T(stockDelivery == RestaurantDeliveryMode.Consume ? "RSR.UI.Consume" : "RSR.UI.TakeAway")))
                stockDelivery = stockDelivery == RestaurantDeliveryMode.Consume ? RestaurantDeliveryMode.TakeAway : RestaurantDeliveryMode.Consume;
            y += row;
            RestaurantUiStyle.DrawCheckbox(new Rect(0f, y, width, row - 8f), SimTranslation.T("RSR.UI.OnSale"), ref stockOnSale); y += row;
            string stockInfo = stockCabinet == null ? SimTranslation.T("RSR.UI.NoStockCabinetHint")
                : stockItem == null ? SimTranslation.T("RSR.UI.SelectStockFoodHint")
                : SimTranslation.T("RSR.UI.StockMenuInventory", stockCabinet.CountStored(stockItem).Named("stored"),
                    stockCabinet.CountReserved(stockItem).Named("reserved"), stockCabinet.CountPending(stockItem).Named("pending"));
            ShopUiVisualUtility.DrawCellLabel(new Rect(0f, y, width, row - 8f), stockInfo, RestaurantUiStyle.MutedText); y += row;
            Widgets.Label(new Rect(0f, y, width, noteHeight), note);
        }

        //只列出本店的销售货柜，并在换柜后重新读取该柜对商品的配置。
        private void SelectStockCabinet()
        {
            var cabinets = RestaurantStockUtility.Cabinets(shop).Where(c => !c.IsRefrigerator).ToList();
            if (cabinets.Count == 0) { error = SimTranslation.T("RSR.UI.NoStockCabinetHint"); return; }
            Find.WindowStack.Add(new FloatMenu(cabinets.Select(cabinet => new FloatMenuOption(CabinetLabel(cabinet), () =>
            {
                stockCabinet = cabinet;
                if (stockItem != null && CanSellStock(stockItem)) SelectStockFood(stockItem);
                else { stockItem = null; stockPrice = ""; }
                error = "";
            })).ToList()));
        }

        //使用食品浏览器选择现货，允许没有厨房配方的成品和酒水。
        private void OpenStockFoods()
        {
            var foods = DefDatabase<ThingDef>.AllDefsListForReading.Where(CanSellStock).OrderBy(t => t.label).ToList();
            Find.WindowStack.Add(new Dialog_SelectRestaurantFood(SelectStockFood, foods, selectingStock: true));
        }

        //现货与热注册采用相同资格，普通商品可直接登记并通过服务员交付。
        private bool CanSellStock(ThingDef item) => CustomGoodsDatabase.IsValidCandidateThing(item) && item.EverHaulable
            && stockCabinet.Goods.AllowsThingDef(item);

        //建立所选商品的局部草稿，不提前改动货柜目录和销售开关。
        private void SelectStockFood(ThingDef food)
        {
            stockItem = food;
            var data = stockCabinet.Goods.FindItemData(food);
            var rule = stockCabinet.saleRules.FirstOrDefault(r => r.item == food);
            var product = stockCabinet.Product(food);
            float defaultPrice = product?.defaultPrice > 0f ? product.defaultPrice : Mathf.Max(1f, food.BaseMarketValue * 1.5f);
            stockPrice = (data?.price > 0f ? data.price : defaultPrice).ToString(CultureInfo.InvariantCulture);
            int portions = rule?.portions ?? product?.defaultCount ?? 1;
            stockPortions = portions.ToString();
            stockTarget = (data?.enabled == true ? data.count : Mathf.Max(data?.count ?? 0, portions)).ToString();
            stockDelivery = food.ingestible == null ? RestaurantDeliveryMode.TakeAway
                : rule?.mode ?? product?.deliveryMode ?? RestaurantDeliveryMode.Consume;
            stockOnSale = true;
            error = "";
        }

        //先完整校验，再提交该商品的库存和销售规则，顾客菜单沿用货柜现货链路。
        private bool SaveStockMenu()
        {
            if (stockCabinet?.Spawned != true || stockCabinet.IsRefrigerator || stockCabinet.Shop != shop)
            { error = SimTranslation.T("RSR.UI.NoStockCabinetHint"); return false; }
            if (stockItem == null || !CanSellStock(stockItem))
            { error = SimTranslation.T("RSR.UI.SelectStockFoodHint"); return false; }
            if (!float.TryParse(stockPrice, NumberStyles.Float, CultureInfo.InvariantCulture, out float unitPrice)
                || float.IsNaN(unitPrice) || float.IsInfinity(unitPrice) || unitPrice < 1f || unitPrice > 100000f)
            { error = SimTranslation.T("RSR.UI.InvalidPrice"); return false; }
            if (!int.TryParse(stockPortions, out int portions) || portions < 1 || portions > stockItem.stackLimit)
            { error = SimTranslation.T("RSR.UI.InvalidPortions"); return false; }
            if (!int.TryParse(stockTarget, out int target) || target < 0 || target > stockCabinet.MaxTotalCapacity)
            { error = SimTranslation.T("RSR.UI.InvalidStockTarget", stockCabinet.MaxTotalCapacity.Named("capacity")); return false; }
            if (stockOnSale && stockDelivery == RestaurantDeliveryMode.Consume && stockItem.ingestible == null)
            { error = SimTranslation.T("RSR.UI.CannotConsume", stockItem.LabelCap.ToString().Named("item")); return false; }
            var inventory = stockCabinet.Goods.CloneItemData();
            inventory.TryGetValue(stockItem.defName, out var previous);
            inventory[stockItem.defName] = new GoodsItemData { enabled = true, count = target, price = unitPrice,
                restockThreshold = previous == null ? -1 : Mathf.Min(previous.restockThreshold, target) };
            if (inventory.Values.Where(data => data.enabled).Sum(data => data.count) > stockCabinet.MaxTotalCapacity)
            { error = SimTranslation.T("RSR.UI.TotalStockOverCapacity"); return false; }

            if (stockCabinet.Product(stockItem) == null) stockCabinet.registeredProducts.Add(stockItem);
            stockCabinet.RegisterCatalog();
            var sale = stockCabinet.Rule(stockItem);
            sale.onSale = stockOnSale;
            sale.portions = portions;
            sale.mode = stockDelivery;
            stockCabinet.Goods.ApplySettings(stockCabinet.CatalogId, inventory);
            RestaurantStorageConfiguration.Apply(stockCabinet);
            return true;
        }

        //显示坐标以区分同名货柜。
        private static string CabinetLabel(Building_RestaurantStorage cabinet) => cabinet.LabelCap + " (" + cabinet.Position.x + ", " + cabinet.Position.z + ")";
    }
}
