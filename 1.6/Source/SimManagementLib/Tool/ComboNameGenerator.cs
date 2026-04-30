using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    public static class ComboNameGenerator
    {
        // 单品套餐模板 {0}主商品
        private static readonly string[] SingleItemTemplates = new string[]
        {
            "特惠{0}装",
            "量贩{0}",
            "至尊{0}礼盒",
            "{0}大满足",
            "超值{0}组合",
            "精选{0}套装",
            "{0}囤货装",
            "家庭装{0}",
            "{0}尝鲜包",
            "限量{0}礼盒",
            "经典{0}套餐",
            "{0}畅享装",
            "豪华{0}礼包",
            "{0}特供版",
            "甄选{0}大礼",
            "{0}超级装",
            "节庆{0}礼盒",
            "旗舰{0}套组",
            "{0}狂欢装",
            "匠心{0}礼装",
        };

        // 双品套餐模板 {0}主商品，{1}副商品
        private static readonly string[] DoubleItemTemplates = new string[]
        {
            "{0}与{1}全家桶",
            "经典{0}搭配套餐",
            "{0}狂欢大礼包",
            "豪华{0}伴侣",
            "{0}与{1}的完美邂逅",
            "{0}加{1}超值组合",
            "{0}领衔·{1}助阵礼包",
            "黄金搭档：{0}与{1}",
            "{0}×{1}限定套餐",
            "{0}搭{1}，实惠加倍",
            "王炸组合·{0}配{1}",
            "{0}带{1}同行礼盒",
            "绝配之选：{0}＋{1}",
            "双料实惠·{0}遇{1}",
            "{0}挚友{1}超值包",
            "精品双拼·{0}配{1}",
            "强强联手·{0}与{1}",
            "{0}与{1}限时福袋",
            "无敌搭档·{0}携{1}",
        };

        // 三品套餐模板 {0}主商品，{1}副商品，{2}第三商品
        private static readonly string[] TripleItemTemplates = new string[]
        {
            "{0}、{1}与{2}三重奏",
            "三合一超值礼包·{0}领衔",
            "{0}、{1}、{2}全齐套装",
            "黄金三角·{0}配{1}配{2}",
            "{0}为主·{1}与{2}随行礼盒",
            "三拼臻选·{0}系列大礼",
            "{0}×{1}×{2}豪华组合",
            "铁三角套餐·{0}领衔出击",
            "{0}、{1}、{2}一网打尽",
            "三重惊喜·{0}携{1}带{2}",
            "超值三件套·{0}打头阵",
            "{0}+{1}+{2}省心套装",
            "三强联盟·{0}与{1}及{2}",
            "{0}的左膀右臂：{1}与{2}",
            "全明星三拼·{0}坐镇",
        };

        // 四品套餐模板 {0}主商品，{1}副商品，{2}第三，{3}第四
        private static readonly string[] QuadItemTemplates = new string[]
        {
            "{0}领衔四重奏大礼包",
            "四合一豪华套餐·{0}打头阵",
            "{0}、{1}、{2}与{3}终极组合",
            "王者四件套·{0}坐镇",
            "{0}×{1}×{2}×{3}超级礼盒",
            "四方会聚·{0}领衔出击",
            "全家福套装·{0}系列",
            "{0}领头羊·四品尊享礼包",
            "无敌四拼·{0}与{1}等豪华组合",
            "四季礼盒·{0}为首臻选套装",
            "超级四件套·{0}配{1}等全家桶",
            "{0}统领四宝豪华礼包",
            "旗舰四合套·{0}坐庄",
            "四强联手·{0}领衔限定礼盒",
            "至尊四拼·{0}与三大伙伴",
        };

        // 五品及以上套餐模板（仅用主商品）
        private static readonly string[] MassItemTemplates = new string[]
        {
            "{0}领衔超级大礼包",
            "万物皆有·{0}打头阵",
            "{0}统领全明星套餐",
            "终极囤货礼盒·{0}领衔",
            "{0}坐镇·应有尽有套装",
            "豪华全家桶·{0}系列",
            "{0}领衔无敌组合大礼",
            "史诗级套装·{0}打头阵",
        };

        public static string GenerateName(ComboData combo)
        {
            if (combo.items.NullOrEmpty()) return "空套餐";

            var sortedItems = combo.items.OrderByDescending(x => x.count).ToList();
            string main = sortedItems.Count > 0 ? sortedItems[0].def.label : "";
            string sub1 = sortedItems.Count > 1 ? sortedItems[1].def.label : "";
            string sub2 = sortedItems.Count > 2 ? sortedItems[2].def.label : "";
            string sub3 = sortedItems.Count > 3 ? sortedItems[3].def.label : "";

            switch (sortedItems.Count)
            {
                case 1:
                    return string.Format(SingleItemTemplates.RandomElement(), main);
                case 2:
                    return string.Format(DoubleItemTemplates.RandomElement(), main, sub1);
                case 3:
                    return string.Format(TripleItemTemplates.RandomElement(), main, sub1, sub2);
                case 4:
                    return string.Format(QuadItemTemplates.RandomElement(), main, sub1, sub2, sub3);
                default:
                    return string.Format(MassItemTemplates.RandomElement(), main);
            }
        }
    }
}