using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    // 管理扩展推荐页数据来源，负责始终从本地 Def 读取推荐项。
    public static class BusinessExtensionRecommendationSource
    {
        // 返回当前推荐项列表，负责按本地 Def 排序后提供给 UI。
        public static List<IBusinessExtensionRecommendation> GetRows()
        {
            return DefDatabase<BusinessExtensionRecommendationDef>.AllDefsListForReading
                .OrderBy(def => def.Order)
                .ThenBy(def => def.StableId)
                .Cast<IBusinessExtensionRecommendation>()
                .ToList();
        }
    }
}
