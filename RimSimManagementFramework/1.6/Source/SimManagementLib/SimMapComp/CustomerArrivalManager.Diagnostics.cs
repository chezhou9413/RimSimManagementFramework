using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //汇总手动到客诊断，职责是在不绕过经营限制的情况下刷新扩展状态并解释商店被排除的原因。
    public partial class CustomerArrivalManager
    {
        //同步查询已配置经营动作，职责是让暂停状态下的主动刷新也读取最新设施与员工状态。
        private Dictionary<int, string> RefreshActionAttractionDiagnostics()
        {
            var result = new Dictionary<int, string>();
            foreach (Zone_Shop shop in map.zoneManager.AllZones.OfType<Zone_Shop>())
                result[shop.ID] = CustomerActionAttractionUtility.GetBlockReasons(shop, refresh: true);
            return result;
        }

        //解释全部开放商店未能生成顾客的原因，职责是区分满员、动作不可用、客群不匹配和声望门槛。
        private static string DescribeForcedSpawnRejections(IReadOnlyList<CustomerArrivalShopContext> contexts,
            IReadOnlyList<RuntimeCustomerKind> kinds, Dictionary<int, string> actionReasons)
        {
            var reasons = new List<string>();
            foreach (CustomerArrivalShopContext context in contexts)
            {
                string reason;
                if (context.IsAtCapacity)
                    reason = SimTranslation.T("RSMF.CustomerArrival.BlockCapacity",
                        context.CurrentCustomers.Named("count"), context.Capacity.Named("capacity"));
                else
                {
                    var matching = kinds.Where(k => context.MatchingKindIds.Contains(k.kindId ?? "")).ToList();
                    if (matching.Count == 0)
                    {
                        actionReasons.TryGetValue(context.Shop.ID, out string actionReason);
                        reason = !actionReason.NullOrEmpty() ? actionReason
                            : SimTranslation.T("RSMF.CustomerArrival.BlockMatching");
                    }
                    else
                    {
                        float reputation = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>()?
                            .GetReputation(context.Shop.ID) ?? 0f;
                        float required = matching.Min(k => k.minShopReputation);
                        reason = required > 0f && reputation < required
                            ? SimTranslation.T("RSMF.CustomerArrival.BlockReputation",
                                reputation.ToString("0.##").Named("current"), required.ToString("0.##").Named("required"))
                            : SimTranslation.T("RSMF.CustomerArrival.BlockConditions");
                    }
                }
                reasons.Add(context.Shop.label + "：" + reason);
            }
            return string.Join("；", reasons);
        }
    }
}
