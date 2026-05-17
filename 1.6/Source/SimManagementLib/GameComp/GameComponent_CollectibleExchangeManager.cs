using SimManagementLib.SimDef;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.GameComp
{
    /// <summary>
    /// 保存收藏品兑换运行时购买次数，职责是只持久化 exchangeDefName 和 itemId 对应的购买计数。
    /// </summary>
    public class GameComponent_CollectibleExchangeManager : GameComponent
    {
        // 购买次数字典，key 使用 exchangeDefName::itemId，避免把 Def 配置写入存档。
        private Dictionary<string, int> purchasedCounts = new Dictionary<string, int>();

        // Scribe 字典序列化使用的临时键列表。
        private List<string> tmpKeys;

        // Scribe 字典序列化使用的临时值列表。
        private List<int> tmpValues;

        /// <summary>
        /// 构造收藏品兑换管理组件，负责让 RimWorld 在新局和读档时创建组件实例。
        /// </summary>
        public GameComponent_CollectibleExchangeManager(Game game)
        {
        }

        /// <summary>
        /// 读写购买次数存档，负责保证读档后字典总是可用。
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref purchasedCounts, "collectibleExchangePurchasedCounts", LookMode.Value, LookMode.Value, ref tmpKeys, ref tmpValues);
            if (purchasedCounts == null)
                purchasedCounts = new Dictionary<string, int>();
        }

        /// <summary>
        /// 读取指定商品已购买次数，负责给 UI 和购买校验提供当前存档状态。
        /// </summary>
        public int GetPurchasedCount(CollectibleExchangeListDef exchangeDef, CollectibleExchangeItemEntry item)
        {
            string key = BuildKey(exchangeDef, item);
            if (key.NullOrEmpty())
                return 0;

            return purchasedCounts.TryGetValue(key, out int count) ? count : 0;
        }

        /// <summary>
        /// 读取指定商品剩余购买次数，负责根据 Def 限购次数和存档购买次数计算剩余量。
        /// </summary>
        public int GetRemainingCount(CollectibleExchangeListDef exchangeDef, CollectibleExchangeItemEntry item)
        {
            if (item == null || item.maxPurchases <= 0)
                return 0;

            return System.Math.Max(0, item.maxPurchases - GetPurchasedCount(exchangeDef, item));
        }

        /// <summary>
        /// 统计一个兑换商店的购买进度，负责给首页进度条提供总限购、已购买和剩余次数。
        /// </summary>
        public CollectibleExchangeProgress GetProgress(CollectibleExchangeListDef exchangeDef)
        {
            CollectibleExchangeProgress progress = new CollectibleExchangeProgress();
            if (exchangeDef?.items == null)
                return progress;

            for (int i = 0; i < exchangeDef.items.Count; i++)
            {
                CollectibleExchangeItemEntry item = exchangeDef.items[i];
                if (item == null || item.maxPurchases <= 0)
                    continue;

                int purchased = System.Math.Min(item.maxPurchases, GetPurchasedCount(exchangeDef, item));
                progress.Total += item.maxPurchases;
                progress.Purchased += purchased;
            }

            progress.Remaining = System.Math.Max(0, progress.Total - progress.Purchased);
            return progress;
        }

        /// <summary>
        /// 记录一次成功购买，负责只增加购买次数而不持久化任何 Def 配置字段。
        /// </summary>
        public void RecordPurchase(CollectibleExchangeListDef exchangeDef, CollectibleExchangeItemEntry item)
        {
            string key = BuildKey(exchangeDef, item);
            if (key.NullOrEmpty())
                return;

            purchasedCounts.TryGetValue(key, out int count);
            purchasedCounts[key] = count + 1;
        }

        /// <summary>
        /// 生成存档键，负责把兑换商店 Def 和商品稳定 ID 合并为唯一键。
        /// </summary>
        private static string BuildKey(CollectibleExchangeListDef exchangeDef, CollectibleExchangeItemEntry item)
        {
            if (exchangeDef == null || item == null || exchangeDef.defName.NullOrEmpty() || item.StableId.NullOrEmpty())
                return "";

            return exchangeDef.defName + "::" + item.StableId;
        }
    }

    /// <summary>
    /// 表示收藏品兑换商店购买进度，职责是承载首页进度条需要的汇总数据。
    /// </summary>
    public struct CollectibleExchangeProgress
    {
        // 所有商品限购次数总和。
        public int Total;

        // 已成功购买次数总和。
        public int Purchased;

        // 当前剩余可购买次数总和。
        public int Remaining;

        /// <summary>
        /// 读取进度百分比，负责处理没有可购买商品时的安全兜底。
        /// </summary>
        public float Percent
        {
            get
            {
                return Total > 0 ? (float)Purchased / Total : 0f;
            }
        }
    }
}
