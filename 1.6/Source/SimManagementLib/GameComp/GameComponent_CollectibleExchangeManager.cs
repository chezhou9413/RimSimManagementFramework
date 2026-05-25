using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
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

        // 下次刷新 Tick 字典，key 使用 exchangeDefName，避免把 Def 配置写入存档。
        private Dictionary<string, int> nextRefreshTicks = new Dictionary<string, int>();

        private int nextRefreshCheckTick;
        private const int RefreshCheckIntervalTicks = 250;

        private List<string> tmpPurchasedKeys;
        private List<int> tmpPurchasedValues;
        private List<string> tmpRefreshKeys;
        private List<int> tmpRefreshValues;

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
            Scribe_Collections.Look(ref purchasedCounts, "collectibleExchangePurchasedCounts", LookMode.Value, LookMode.Value, ref tmpPurchasedKeys, ref tmpPurchasedValues);
            Scribe_Collections.Look(ref nextRefreshTicks, "collectibleExchangeNextRefreshTicks", LookMode.Value, LookMode.Value, ref tmpRefreshKeys, ref tmpRefreshValues);
            Scribe_Values.Look(ref nextRefreshCheckTick, "collectibleExchangeNextRefreshCheckTick", 0);
            if (purchasedCounts == null)
                purchasedCounts = new Dictionary<string, int>();
            if (nextRefreshTicks == null)
                nextRefreshTicks = new Dictionary<string, int>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureRefreshSchedules(false);
        }

        /// <summary>
        /// 周期检查所有启用刷新周期的兑换商店，负责按 tick 推进商品库存刷新。
        /// </summary>
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now < nextRefreshCheckTick)
                return;

            nextRefreshCheckTick = now + RefreshCheckIntervalTicks;
            EnsureRefreshSchedules(true);
            foreach (CollectibleExchangeListDef exchangeDef in GetRefreshableExchangeDefs())
            {
                string defName = exchangeDef.defName;
                if (defName.NullOrEmpty())
                    continue;

                if (!nextRefreshTicks.TryGetValue(defName, out int nextTick))
                {
                    nextRefreshTicks[defName] = now + exchangeDef.refreshIntervalTicks;
                    continue;
                }

                if (now < nextTick)
                    continue;

                RefreshExchange(exchangeDef);
                nextRefreshTicks[defName] = now + exchangeDef.refreshIntervalTicks;
            }
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
        /// 读取指定兑换商店距离下次刷新还剩多少 tick；未启用刷新时返回 -1。
        /// </summary>
        public int GetTicksUntilNextRefresh(CollectibleExchangeListDef exchangeDef)
        {
            if (exchangeDef == null || exchangeDef.refreshIntervalTicks <= 0 || exchangeDef.defName.NullOrEmpty())
                return -1;

            EnsureRefreshSchedule(exchangeDef, false);
            if (!nextRefreshTicks.TryGetValue(exchangeDef.defName, out int nextTick))
                return -1;

            int now = Find.TickManager?.TicksGame ?? 0;
            return System.Math.Max(0, nextTick - now);
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
        /// 初始化或维护刷新日程，负责让旧存档在首次加载后从当前 tick 往后等待一个完整周期。
        /// </summary>
        private void EnsureRefreshSchedules(bool removeDisabled)
        {
            foreach (CollectibleExchangeListDef exchangeDef in DefDatabase<CollectibleExchangeListDef>.AllDefsListForReading)
                EnsureRefreshSchedule(exchangeDef, removeDisabled);

            if (!removeDisabled || nextRefreshTicks.Count <= 0)
                return;

            HashSet<string> enabled = new HashSet<string>(GetRefreshableExchangeDefs().Select(def => def.defName));
            List<string> disabledKeys = nextRefreshTicks.Keys.Where(key => !enabled.Contains(key)).ToList();
            for (int i = 0; i < disabledKeys.Count; i++)
                nextRefreshTicks.Remove(disabledKeys[i]);
        }

        /// <summary>
        /// 为单个兑换商店初始化刷新时间，职责是避免新旧存档在第一次 tick 时立刻刷新。
        /// </summary>
        private void EnsureRefreshSchedule(CollectibleExchangeListDef exchangeDef, bool removeDisabled)
        {
            if (exchangeDef == null || exchangeDef.defName.NullOrEmpty())
                return;

            if (exchangeDef.refreshIntervalTicks <= 0)
            {
                if (removeDisabled)
                    nextRefreshTicks.Remove(exchangeDef.defName);
                return;
            }

            if (nextRefreshTicks.ContainsKey(exchangeDef.defName))
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            nextRefreshTicks[exchangeDef.defName] = now + exchangeDef.refreshIntervalTicks;
        }

        /// <summary>
        /// 执行单个兑换商店的商品刷新，负责只重置通过概率判定的可刷新商品购买次数。
        /// </summary>
        private void RefreshExchange(CollectibleExchangeListDef exchangeDef)
        {
            if (exchangeDef?.items == null)
                return;

            for (int i = 0; i < exchangeDef.items.Count; i++)
            {
                CollectibleExchangeItemEntry item = exchangeDef.items[i];
                if (item == null || !item.canRefresh)
                    continue;
                if (!Rand.Chance(item.RefreshChance))
                    continue;

                string key = BuildKey(exchangeDef, item);
                if (!key.NullOrEmpty())
                    purchasedCounts.Remove(key);
            }
        }

        /// <summary>
        /// 枚举配置了刷新周期的兑换商店 Def。
        /// </summary>
        private static IEnumerable<CollectibleExchangeListDef> GetRefreshableExchangeDefs()
        {
            return DefDatabase<CollectibleExchangeListDef>.AllDefsListForReading
                .Where(def => def != null && !def.defName.NullOrEmpty() && def.refreshIntervalTicks > 0);
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
