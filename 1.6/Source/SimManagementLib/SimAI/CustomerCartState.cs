using SimManagementLib.Pojo;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 保存顾客购物车、消费次数、满意度和浏览等待计时，负责把消费阶段的状态从 LordJob 中拆出。
    /// </summary>
    internal class CustomerCartState
    {
        internal Dictionary<int, float> cartValues = new Dictionary<int, float>();
        internal Dictionary<int, float> satisfactionMap = new Dictionary<int, float>();
        internal Dictionary<int, List<CustomerCartItem>> cartItems = new Dictionary<int, List<CustomerCartItem>>();
        internal Dictionary<int, int> consumptionActionCounts = new Dictionary<int, int>();
        internal Dictionary<int, int> effectiveBudgetCaps = new Dictionary<int, int>();
        internal Dictionary<int, int> browseWaitStartTick = new Dictionary<int, int>();

        private List<int> tmpCartItemKeys;
        private List<List<CustomerCartItem>> tmpCartItemValues;
        private List<int> tmpEffectiveBudgetCapKeys;
        private List<int> tmpEffectiveBudgetCapValues;

        /// <summary>
        /// 读写购物车相关存档数据，并在读档后补齐集合实例。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref cartValues, "cartValues", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref satisfactionMap, "satisfactionMap", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref cartItems, "cartItems", LookMode.Value, LookMode.Deep, ref tmpCartItemKeys, ref tmpCartItemValues);
            Scribe_Collections.Look(ref consumptionActionCounts, "consumptionActionCounts", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref effectiveBudgetCaps, "effectiveBudgetCaps", LookMode.Value, LookMode.Value, ref tmpEffectiveBudgetCapKeys, ref tmpEffectiveBudgetCapValues);
            Scribe_Collections.Look(ref browseWaitStartTick, "browseWaitStartTick", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureCollections();
        }

        /// <summary>
        /// 添加指定商品到顾客购物车，相同 ThingDef 会合并数量。
        /// </summary>
        public void AddCartItem(int pawnId, ThingDef def, int count)
        {
            if (pawnId <= 0 || def == null || count <= 0) return;

            if (!cartItems.TryGetValue(pawnId, out List<CustomerCartItem> list))
            {
                list = new List<CustomerCartItem>();
                cartItems[pawnId] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CustomerCartItem item = list[i];
                if (item == null || item.def != def) continue;
                item.count += count;
                return;
            }

            list.Add(new CustomerCartItem { def = def, count = count });
        }

        /// <summary>
        /// 把套餐条目批量加入顾客购物车。
        /// </summary>
        public void AddCartItemsFromCombo(int pawnId, List<ComboItem> comboItems)
        {
            if (comboItems == null) return;
            for (int i = 0; i < comboItems.Count; i++)
            {
                ComboItem comboItem = comboItems[i];
                if (comboItem == null || comboItem.def == null || comboItem.count <= 0) continue;
                AddCartItem(pawnId, comboItem.def, comboItem.count);
            }
        }

        /// <summary>
        /// 返回指定顾客的购物车条目。
        /// </summary>
        public List<CustomerCartItem> GetCartItems(int pawnId)
        {
            return cartItems.TryGetValue(pawnId, out List<CustomerCartItem> list) ? list : null;
        }

        /// <summary>
        /// 清除指定顾客的购物车、预算缓存、消费次数和浏览等待计时。
        /// </summary>
        public void ClearCustomerCart(int pawnId)
        {
            cartItems.Remove(pawnId);
            cartValues[pawnId] = 0f;
            consumptionActionCounts.Remove(pawnId);
            effectiveBudgetCaps.Remove(pawnId);
            browseWaitStartTick.Remove(pawnId);
        }

        /// <summary>
        /// 记录一次消费动作，并返回是否达到本次访问的消费次数上限。
        /// </summary>
        public bool RegisterConsumptionActionAndShouldCheckout(int pawnId, int maxConsumptionActions)
        {
            if (pawnId <= 0) return true;
            int count = consumptionActionCounts.TryGetValue(pawnId, out int old) ? old + 1 : 1;
            consumptionActionCounts[pawnId] = count;
            return count >= maxConsumptionActions;
        }

        /// <summary>
        /// 判断顾客是否已经达到本次访问的消费次数上限。
        /// </summary>
        public bool HasReachedConsumptionLimit(int pawnId, int maxConsumptionActions)
        {
            return pawnId > 0
                && consumptionActionCounts.TryGetValue(pawnId, out int count)
                && count >= maxConsumptionActions;
        }

        /// <summary>
        /// 获取或初始化顾客浏览等待开始 Tick。
        /// </summary>
        public int GetOrInitBrowseWaitStartTick(int pawnId, int nowTick)
        {
            if (pawnId <= 0) return nowTick;
            if (!browseWaitStartTick.TryGetValue(pawnId, out int start) || start <= 0 || start > nowTick)
            {
                browseWaitStartTick[pawnId] = nowTick;
                return nowTick;
            }

            return start;
        }

        /// <summary>
        /// 清除顾客浏览等待计时。
        /// </summary>
        public void ClearBrowseWaitStartTick(int pawnId)
        {
            if (pawnId <= 0) return;
            browseWaitStartTick.Remove(pawnId);
        }

        /// <summary>
        /// 确保存档恢复后所有集合均可安全访问。
        /// </summary>
        private void EnsureCollections()
        {
            if (cartValues == null) cartValues = new Dictionary<int, float>();
            if (satisfactionMap == null) satisfactionMap = new Dictionary<int, float>();
            if (cartItems == null) cartItems = new Dictionary<int, List<CustomerCartItem>>();
            if (consumptionActionCounts == null) consumptionActionCounts = new Dictionary<int, int>();
            if (effectiveBudgetCaps == null) effectiveBudgetCaps = new Dictionary<int, int>();
            if (browseWaitStartTick == null) browseWaitStartTick = new Dictionary<int, int>();
        }
    }
}
