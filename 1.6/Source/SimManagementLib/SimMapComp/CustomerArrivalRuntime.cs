using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimMapComp
{
    //类职责：维护商店吸引力快照和刷新游标，把商店与顾客类型匹配计算分摊到固定预算。
    internal sealed class CustomerArrivalRuntime
    {
        private const int CatalogCheckIntervalTicks = 120;
        private readonly CustomerArrivalManager owner;
        private readonly Map map;
        private readonly Dictionary<int, CustomerArrivalShopContext> contexts = new Dictionary<int, CustomerArrivalShopContext>();
        private readonly Queue<int> dirtyQueue = new Queue<int>();
        private readonly HashSet<int> dirtyIds = new HashSet<int>();
        private readonly List<int> cycleShopIds = new List<int>();
        private int cycleCursor;
        private int lastCatalogSignature;
        private int nextCatalogCheckTick;

        public bool CycleCompleted => cycleCursor >= cycleShopIds.Count;
        public int DirtyCount => dirtyIds.Count;
        public int CycleProcessed => cycleCursor;
        public int CycleTotal => cycleShopIds.Count;

        //创建商店刷新运行态，职责是注册已有区划并把首次重算放入预算队列。
        public CustomerArrivalRuntime(CustomerArrivalManager owner, Map map)
        {
            this.owner = owner;
            this.map = map;
            RegisterCurrentShops();
            lastCatalogSignature = CalculateCatalogSignature();
        }

        //推进两份脏商店快照，职责是限制同 tick 的设施与库存扫描数量。
        public void Tick()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now >= nextCatalogCheckTick)
            {
                nextCatalogCheckTick = now + CatalogCheckIntervalTicks;
                int signature = CalculateCatalogSignature();
                if (signature != lastCatalogSignature)
                {
                    lastCatalogSignature = signature;
                    MarkAllDirty();
                }
            }

            int budget = 2;
            while (budget-- > 0 && dirtyQueue.Count > 0)
            {
                int shopId = dirtyQueue.Dequeue();
                dirtyIds.Remove(shopId);
                Rebuild(shopId);
            }
        }

        //开始一次刷新候选周期，职责是只复制已注册商店 ID 并冻结本轮顺序。
        public void BeginCycle()
        {
            cycleShopIds.Clear();
            cycleCursor = 0;
            foreach (KeyValuePair<int, CustomerArrivalShopContext> pair in contexts)
            {
                CustomerArrivalShopContext context = pair.Value;
                if (context?.Shop != null && context.Shop.Map == map)
                    cycleShopIds.Add(pair.Key);
            }
        }

        //返回本轮下一个可用快照，职责是跳过关店、脏数据和已删除区划。
        public bool TryTakeNextContext(out CustomerArrivalShopContext context)
        {
            context = null;
            while (cycleCursor < cycleShopIds.Count)
            {
                int shopId = cycleShopIds[cycleCursor++];
                if (dirtyIds.Contains(shopId)) continue;
                if (!contexts.TryGetValue(shopId, out CustomerArrivalShopContext candidate)) continue;
                if (candidate?.Shop == null || candidate.Shop.Map != map) continue;
                if (!candidate.Shop.IsOpenNow()) continue;
                candidate.IsOpen = true;
                context = candidate;
                return true;
            }
            return false;
        }

        //结束刷新周期，职责是释放本轮候选列表。
        public void EndCycle()
        {
            cycleShopIds.Clear();
            cycleCursor = 0;
        }

        //返回当前开放商店快照，职责是供玩家强制刷新复用缓存。
        public List<CustomerArrivalShopContext> GetOpenContexts(CustomerRuntimeIndex index)
        {
            List<CustomerArrivalShopContext> result = new List<CustomerArrivalShopContext>();
            foreach (CustomerArrivalShopContext context in contexts.Values)
            {
                if (context?.Shop == null || context.Shop.Map != map || !context.Shop.IsOpenNow()) continue;
                context.IsOpen = true;
                context.CurrentCustomers = index?.CountActiveForShop(context.Shop.ID) ?? 0;
                result.Add(context);
            }
            return result;
        }

        //同步刷新全部商店，职责是仅为玩家主动强制刷新绕过调度等待。
        public void RefreshAllSynchronously()
        {
            RegisterCurrentShops();
            List<int> ids = new List<int>(contexts.Keys);
            for (int i = 0; i < ids.Count; i++)
                Rebuild(ids[i]);
            dirtyQueue.Clear();
            dirtyIds.Clear();
        }

        //标记商店快照失效，职责是用集合合并同 tick 的重复通知。
        public void MarkDirty(Zone_Shop shop)
        {
            if (shop == null || shop.Map != map) return;
            if (!contexts.ContainsKey(shop.ID))
                contexts[shop.ID] = null;
            if (dirtyIds.Add(shop.ID))
                dirtyQueue.Enqueue(shop.ID);
        }

        //注销商店快照，职责是按 ID 删除缓存、脏标记和后续刷新候选。
        public void Unregister(int shopId)
        {
            contexts.Remove(shopId);
            dirtyIds.Remove(shopId);
        }

        //返回缓存入口格，职责是让旅行阶段执行 O(1) 目标查询。
        public bool TryGetEntryCell(int shopId, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (!contexts.TryGetValue(shopId, out CustomerArrivalShopContext context) || context == null)
                return false;
            cell = context.EntryCell;
            return cell.IsValid;
        }

        //注册地图当前商店区划，职责是仅在初始化和玩家强制刷新时扫描区划列表。
        private void RegisterCurrentShops()
        {
            List<Zone> zones = map?.zoneManager?.AllZones;
            if (zones == null) return;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Shop shop)
                    MarkDirty(shop);
            }
        }

        //重建单个商店快照，职责是完成一次原子替换并移除已删除区划。
        private void Rebuild(int shopId)
        {
            Zone_Shop shop = FindShop(shopId);
            if (shop == null)
            {
                contexts.Remove(shopId);
                return;
            }

            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            contexts[shopId] = owner.BuildShopContext(shop, analytics, owner.RuntimeIndex.CountActiveForShop(shopId));
        }

        //按 ID 查找已存在商店，职责是仅在脏快照重建时扫描区划而非刷新热路径。
        private Zone_Shop FindShop(int shopId)
        {
            List<Zone> zones = map?.zoneManager?.AllZones;
            if (zones == null) return null;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Shop shop && shop.ID == shopId)
                    return shop;
            }
            return null;
        }

        //标记全部已注册商店失效，职责是处理顾客目录整体变化。
        internal void MarkAllDirty()
        {
            foreach (CustomerArrivalShopContext context in contexts.Values)
            {
                if (context?.Shop != null)
                    MarkDirty(context.Shop);
            }
        }

        //计算顾客目录签名，职责是用轻量字段识别自定义类型增删和目标变化。
        private static int CalculateCatalogSignature()
        {
            unchecked
            {
                int hash = 17;
                IReadOnlyCollection<RuntimeCustomerKind> kinds = CustomerCatalog.Kinds;
                if (kinds == null) return hash;
                foreach (RuntimeCustomerKind kind in kinds)
                {
                    hash = hash * 31 + (kind?.kindId?.GetHashCode() ?? 0);
                    hash = hash * 31 + (kind?.targetGoodsCategoryIds?.Count ?? 0);
                    hash = hash * 31 + (kind?.targetServiceCategoryIds?.Count ?? 0);
                    if (kind?.targetGoodsCategoryIds != null)
                    {
                        for (int i = 0; i < kind.targetGoodsCategoryIds.Count; i++)
                            hash = hash * 31 + (kind.targetGoodsCategoryIds[i]?.GetHashCode() ?? 0);
                    }
                    if (kind?.targetServiceCategoryIds != null)
                    {
                        for (int i = 0; i < kind.targetServiceCategoryIds.Count; i++)
                            hash = hash * 31 + (kind.targetServiceCategoryIds[i]?.GetHashCode() ?? 0);
                    }
                }
                return hash;
            }
        }
    }
}
