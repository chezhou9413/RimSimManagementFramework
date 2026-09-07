using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    public static class ComboNameGenerator
    {
        // 单品套餐模板键，{0} 为主商品。
        private static readonly string[] SingleItemTemplateKeys = new string[]
        {
            "RSMF.ComboName.Single.01",
            "RSMF.ComboName.Single.02",
            "RSMF.ComboName.Single.03",
            "RSMF.ComboName.Single.04",
            "RSMF.ComboName.Single.05",
            "RSMF.ComboName.Single.06",
            "RSMF.ComboName.Single.07",
            "RSMF.ComboName.Single.08",
            "RSMF.ComboName.Single.09",
            "RSMF.ComboName.Single.10",
            "RSMF.ComboName.Single.11",
            "RSMF.ComboName.Single.12",
            "RSMF.ComboName.Single.13",
            "RSMF.ComboName.Single.14",
            "RSMF.ComboName.Single.15",
            "RSMF.ComboName.Single.16",
            "RSMF.ComboName.Single.17",
            "RSMF.ComboName.Single.18",
            "RSMF.ComboName.Single.19",
            "RSMF.ComboName.Single.20",
        };

        // 双品套餐模板键，{0} 为主商品，{1} 为副商品。
        private static readonly string[] DoubleItemTemplateKeys = new string[]
        {
            "RSMF.ComboName.Double.01",
            "RSMF.ComboName.Double.02",
            "RSMF.ComboName.Double.03",
            "RSMF.ComboName.Double.04",
            "RSMF.ComboName.Double.05",
            "RSMF.ComboName.Double.06",
            "RSMF.ComboName.Double.07",
            "RSMF.ComboName.Double.08",
            "RSMF.ComboName.Double.09",
            "RSMF.ComboName.Double.10",
            "RSMF.ComboName.Double.11",
            "RSMF.ComboName.Double.12",
            "RSMF.ComboName.Double.13",
            "RSMF.ComboName.Double.14",
            "RSMF.ComboName.Double.15",
            "RSMF.ComboName.Double.16",
            "RSMF.ComboName.Double.17",
            "RSMF.ComboName.Double.18",
            "RSMF.ComboName.Double.19",
        };

        // 三品套餐模板键。
        private static readonly string[] TripleItemTemplateKeys = new string[]
        {
            "RSMF.ComboName.Triple.01",
            "RSMF.ComboName.Triple.02",
            "RSMF.ComboName.Triple.03",
            "RSMF.ComboName.Triple.04",
            "RSMF.ComboName.Triple.05",
            "RSMF.ComboName.Triple.06",
            "RSMF.ComboName.Triple.07",
            "RSMF.ComboName.Triple.08",
            "RSMF.ComboName.Triple.09",
            "RSMF.ComboName.Triple.10",
            "RSMF.ComboName.Triple.11",
            "RSMF.ComboName.Triple.12",
            "RSMF.ComboName.Triple.13",
            "RSMF.ComboName.Triple.14",
            "RSMF.ComboName.Triple.15",
        };

        // 四品套餐模板键。
        private static readonly string[] QuadItemTemplateKeys = new string[]
        {
            "RSMF.ComboName.Quad.01",
            "RSMF.ComboName.Quad.02",
            "RSMF.ComboName.Quad.03",
            "RSMF.ComboName.Quad.04",
            "RSMF.ComboName.Quad.05",
            "RSMF.ComboName.Quad.06",
            "RSMF.ComboName.Quad.07",
            "RSMF.ComboName.Quad.08",
            "RSMF.ComboName.Quad.09",
            "RSMF.ComboName.Quad.10",
            "RSMF.ComboName.Quad.11",
            "RSMF.ComboName.Quad.12",
            "RSMF.ComboName.Quad.13",
            "RSMF.ComboName.Quad.14",
            "RSMF.ComboName.Quad.15",
        };

        // 五品及以上套餐模板键，仅用主商品。
        private static readonly string[] MassItemTemplateKeys = new string[]
        {
            "RSMF.ComboName.Mass.01",
            "RSMF.ComboName.Mass.02",
            "RSMF.ComboName.Mass.03",
            "RSMF.ComboName.Mass.04",
            "RSMF.ComboName.Mass.05",
            "RSMF.ComboName.Mass.06",
            "RSMF.ComboName.Mass.07",
            "RSMF.ComboName.Mass.08",
        };

        public static string GenerateName(ComboData combo)
        {
            if (combo.items.NullOrEmpty()) return SimTranslation.T("RSMF.ComboName.Empty");

            var sortedItems = combo.items.OrderByDescending(x => x.count).ToList();
            string main = sortedItems.Count > 0 ? sortedItems[0].def.label : "";
            string sub1 = sortedItems.Count > 1 ? sortedItems[1].def.label : "";
            string sub2 = sortedItems.Count > 2 ? sortedItems[2].def.label : "";
            string sub3 = sortedItems.Count > 3 ? sortedItems[3].def.label : "";

            switch (sortedItems.Count)
            {
                case 1:
                    return FormatTemplate(SingleItemTemplateKeys.RandomElement(), main);
                case 2:
                    return FormatTemplate(DoubleItemTemplateKeys.RandomElement(), main, sub1);
                case 3:
                    return FormatTemplate(TripleItemTemplateKeys.RandomElement(), main, sub1, sub2);
                case 4:
                    return FormatTemplate(QuadItemTemplateKeys.RandomElement(), main, sub1, sub2, sub3);
                default:
                    return FormatTemplate(MassItemTemplateKeys.RandomElement(), main);
            }
        }

        /// <summary>
        /// 按当前语言读取套餐名模板并填入商品名。
        /// </summary>
        private static string FormatTemplate(string key, params string[] values)
        {
            return string.Format(SimTranslation.T(key), values);
        }
    }
}
