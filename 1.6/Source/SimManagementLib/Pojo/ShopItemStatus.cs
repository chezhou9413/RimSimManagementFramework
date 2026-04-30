using SimManagementLib.SimThingComp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.Pojo
{
    public class ShopItemStatus
    {
        public ThingDef Def;
        // 包含你在 UI 里配置的 enabled, count(目标量), price(单价) 等数据
        public GoodsItemData Config;
        // 该商店区域内该商品的总库存
        public int CurrentStock;
    }
}
