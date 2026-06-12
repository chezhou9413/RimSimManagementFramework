using SimManagementLib.Pojo;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 添加指定商品到顾客购物车。
        /// </summary>
        public void AddCartItem(int pawnId, ThingDef def, int count)
        {
            cartState.AddCartItem(pawnId, def, count);
        }

        /// <summary>
        /// 把套餐商品加入顾客购物车。
        /// </summary>
        public void AddCartItemsFromCombo(int pawnId, List<ComboItem> comboItems)
        {
            cartState.AddCartItemsFromCombo(pawnId, comboItems);
        }

        /// <summary>
        /// 返回指定顾客的购物车条目。
        /// </summary>
        public List<CustomerCartItem> GetCartItems(int pawnId)
        {
            return cartState.GetCartItems(pawnId);
        }

        // 记录已经交付到顾客身上的购买物，负责在紧急离店时丢弃商品。
        public void RecordDeliveredItems(int pawnId, List<CustomerCartItem> items)
        {
            cartState.RecordDeliveredItems(pawnId, items);
        }

        // 返回指定顾客已交付购买物记录。
        public List<CustomerCartItem> GetDeliveredItems(int pawnId)
        {
            return cartState.GetDeliveredItems(pawnId);
        }

        // 清除指定顾客已交付购买物记录。
        public void ClearDeliveredItems(int pawnId)
        {
            cartState.ClearDeliveredItems(pawnId);
        }

        /// <summary>
        /// 查找指定编号的活跃顾客，负责让业务代码不直接遍历 Lord 成员列表。
        /// </summary>
        public Pawn FindOwnedPawnById(int pawnId)
        {
            if (pawnId <= 0 || lord?.ownedPawns == null) return null;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn != null && pawn.thingIDNumber == pawnId)
                    return pawn;
            }
            return null;
        }

        /// <summary>
        /// 确保顾客账单记录存在，负责替代外部代码直接写入购物车金额字典。
        /// </summary>
        public void EnsureCustomerBill(int pawnId)
        {
            if (pawnId <= 0) return;
            if (!cartValues.ContainsKey(pawnId))
                cartValues[pawnId] = 0f;
        }

        /// <summary>
        /// 返回顾客当前购物车金额，负责给调试和 UI 提供只读账单入口。
        /// </summary>
        public float GetCartValue(int pawnId)
        {
            return pawnId > 0 && cartValues.TryGetValue(pawnId, out float value) ? value : 0f;
        }

        /// <summary>
        /// 判断顾客是否存在任何账单金额，负责统一零账单判断。
        /// </summary>
        public bool HasAnyBill(int pawnId)
        {
            return GetAmountOwedForCheckout(pawnId) > 0f;
        }

        /// <summary>
        /// 向顾客当前账单追加金额，负责集中维护外部动作和扩展费用入口。
        /// </summary>
        public bool AddCustomerBill(int pawnId, float amount)
        {
            if (pawnId <= 0 || amount <= 0f) return false;
            EnsureCustomerBill(pawnId);
            cartValues[pawnId] += amount;
            return true;
        }

        /// <summary>
        /// 清除顾客的购物车、预算缓存、消费次数、结账顺序和浏览等待计时。
        /// </summary>
        public void ClearCustomerCart(int pawnId)
        {
            cartState.ClearCustomerCart(pawnId);
            checkoutState.ClearCheckoutOrder(pawnId);
            ClearPriceRejectionReason(pawnId);
        }

        /// <summary>
        /// 记录一次商品或服务消费动作，并在达到上限时要求顾客去结账。
        /// </summary>
        public bool RegisterConsumptionActionAndShouldCheckout(int pawnId)
        {
            Pawn pawn = FindOwnedPawnById(pawnId);
            if (pawn != null)
                return RegisterConsumptionActionForCurrentShop(pawn);
            return cartState.RegisterConsumptionActionAndShouldCheckout(pawnId, GetShoppingBehavior().maxConsumptionActionsPerShop);
        }

        /// <summary>
        /// 判断顾客是否已经达到本次访问允许的最大消费动作数量。
        /// </summary>
        public bool HasReachedConsumptionLimit(int pawnId)
        {
            Pawn pawn = FindOwnedPawnById(pawnId);
            if (pawn != null)
                return HasReachedCurrentShopConsumptionLimit(pawn);
            return cartState.HasReachedConsumptionLimit(pawnId, GetShoppingBehavior().maxConsumptionActionsPerShop);
        }

        /// <summary>
        /// 设置指定顾客的运行时配置。
        /// </summary>
        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            pawnSettingsState.SetPawnSettings(pawnId, settings);
        }

        /// <summary>
        /// 获取指定顾客的运行时配置。
        /// </summary>
        public CustomerRuntimeSettings GetPawnSettings(int pawnId)
        {
            return pawnSettingsState.GetPawnSettings(pawnId);
        }

        /// <summary>
        /// 返回顾客原始预算，优先使用按顾客写入的运行时预算。
        /// </summary>
        public int GetBudgetForPawn(int pawnId)
        {
            return CustomerBudgetPolicy.GetBudgetForPawn(this, pawnId);
        }

        /// <summary>
        /// 返回顾客本次愿意消费的预算上限，负责让低品质商店难以吃满顾客原始预算。
        /// </summary>
        public int GetEffectiveBudgetForPawn(Pawn pawn, Zone_Shop shopZone)
        {
            return CustomerBudgetPolicy.GetEffectiveBudgetForPawn(this, pawn, shopZone);
        }

        /// <summary>
        /// 返回顾客排队耐心，优先使用个体设置，其次使用顾客类型配置。
        /// </summary>
        public int GetQueuePatienceForPawn(int pawnId)
        {
            return CustomerBudgetPolicy.GetQueuePatienceForPawn(this, pawnId);
        }

        /// <summary>
        /// 返回顾客对指定物品的偏好倍率。
        /// </summary>
        public float GetPreferenceMultiplier(int pawnId, ThingDef def)
        {
            return CustomerPreferencePolicy.GetPreferenceMultiplier(this, pawnId, def);
        }

        /// <summary>
        /// 返回指定顾客最终价格敏感度，负责让未配置顾客和旧存档顾客使用默认参数。
        /// </summary>
        public CustomerPriceSensitivityProps GetPriceSensitivity(int pawnId)
        {
            CustomerRuntimeSettings settings = GetPawnSettings(pawnId);
            settings?.EnsureDefaults();
            return CustomerPriceSensitivityProps.Resolve(settings?.priceSensitivity);
        }

        /// <summary>
        /// 记录顾客最近一次因高溢价拒买的原因，负责提供评价和调试上下文。
        /// </summary>
        public void RecordPriceRejection(int pawnId, string reason)
        {
            if (pawnId <= 0 || string.IsNullOrWhiteSpace(reason))
                return;
            priceRejectionReasons[pawnId] = reason;
        }

        /// <summary>
        /// 返回顾客最近一次因高溢价拒买的原因。
        /// </summary>
        public string GetPriceRejectionReason(int pawnId)
        {
            return pawnId > 0 && priceRejectionReasons.TryGetValue(pawnId, out string reason) ? reason : "";
        }

        /// <summary>
        /// 清除顾客高溢价拒买原因，负责在成功购物或清理购物车时移除过期负面信号。
        /// </summary>
        public void ClearPriceRejectionReason(int pawnId)
        {
            if (pawnId <= 0)
                return;
            priceRejectionReasons.Remove(pawnId);
        }

        /// <summary>
        /// 获取或初始化顾客浏览等待开始 Tick。
        /// </summary>
        public int GetOrInitBrowseWaitStartTick(int pawnId, int nowTick)
        {
            return cartState.GetOrInitBrowseWaitStartTick(pawnId, nowTick);
        }

        /// <summary>
        /// 清除顾客浏览等待计时。
        /// </summary>
        public void ClearBrowseWaitStartTick(int pawnId)
        {
            cartState.ClearBrowseWaitStartTick(pawnId);
        }
    }
}
