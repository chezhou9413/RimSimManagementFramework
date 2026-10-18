using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    //管理扩展推荐页数据来源，负责始终从本地 Def 读取推荐项。
    public static class BusinessExtensionRecommendationSource
    {
        //返回全部推荐项，职责是保留框架调用入口并按本地 Def 稳定排序。
        public static List<IBusinessExtensionRecommendation> GetRows()
        {
            return GetRows(null);
        }

        //返回分类推荐项，职责是按官方标记筛选并保留各分类内的展示顺序。
        public static List<IBusinessExtensionRecommendation> GetRows(bool? official)
        {
            return DefDatabase<BusinessExtensionRecommendationDef>.AllDefsListForReading
                .Where(def => !official.HasValue || def.official == official.Value)
                .OrderBy(def => def.Order)
                .ThenBy(def => def.StableId)
                .Cast<IBusinessExtensionRecommendation>()
                .ToList();
        }
    }
}
