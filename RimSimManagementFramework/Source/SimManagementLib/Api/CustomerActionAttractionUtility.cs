using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Api
{
    //类职责：汇总顾客动作提供的商店接待能力，供营业校验和顾客生成快照统一调用。
    public static class CustomerActionAttractionUtility
    {
        //判断商店是否存在可用动作提供者，职责是让餐厅柜台等外部设施能满足商店经营设施校验。
        public static bool HasAvailableProvider(Zone_Shop shop)
        {
            if (shop?.Map == null) return false;
            List<CustomerActionDef> actions = DefDatabase<CustomerActionDef>.AllDefsListForReading;
            for (int i = 0; i < actions.Count; i++)
            {
                CustomerActionDef action = actions[i];
                if (action?.Worker?.IsAttractionAvailable(shop) == true)
                    return true;
            }
            return false;
        }

        //收集已配置动作的经营阻塞原因，职责是在主动刷新时同步更新扩展快照并保留可读诊断。
        public static string GetBlockReasons(Zone_Shop shop, bool refresh = false)
        {
            var reasons = new List<string>();
            foreach (CustomerActionDef action in DefDatabase<CustomerActionDef>.AllDefsListForReading)
            {
                string reason = action?.Worker?.GetAttractionBlockReason(shop, refresh);
                if (!reason.NullOrEmpty()) reasons.Add(action.LabelCap + "：" + reason);
            }
            return string.Join("；", reasons);
        }

        //判断商店动作是否能吸引指定顾客类型，职责是把外部动作接入框架的顾客类型匹配。
        public static bool MatchesCustomer(Zone_Shop shop, RuntimeCustomerKind customerKind)
        {
            if (shop?.Map == null || customerKind == null) return false;
            List<CustomerActionDef> actions = DefDatabase<CustomerActionDef>.AllDefsListForReading;
            for (int i = 0; i < actions.Count; i++)
            {
                CustomerActionDef action = actions[i];
                if (action?.Worker?.CanAttractCustomer(shop, customerKind) == true)
                    return true;
            }
            return false;
        }
    }
}
