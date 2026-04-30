using RimWorld;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimDef
{
    public enum PostPurchaseTargetMode
    {
        None,
        ShopCenterCell,
        RandomStandableCellInShop,
        NearestChair,
        NearestTable,
        DiningSpot
    }

    public class PurchaseOutcomeDef : Def
    {
        // 绑定触发后逻辑的 worker 类型。
        public Type workerClass;
        public float triggerChance = 1.0f;

        // 触发条件：按具体物品或 Goods 分类匹配；都为空时表示任意购买都可触发。
        public List<ThingDef> triggerThingDefs = new List<ThingDef>();
        public List<GoodsDef> triggerGoodsDefs = new List<GoodsDef>();
        public List<string> triggerGoodsCategoryIds = new List<string>();

        // true: 每次结账只触发一次；false: 每个匹配商品都可触发。
        public bool triggerOncePerCheckout = true;

        // 通用 worker 使用的配置参数。
        public JobDef configuredJobDef;
        public PostPurchaseTargetMode targetMode = PostPurchaseTargetMode.None;
        public IntRange jobDurationTicks = new IntRange(300, 900);
        public int maxJobsToQueue = 1;

        [Unsaved] private PurchaseOutcomeWorker workerInt;
        public PurchaseOutcomeWorker Worker
        {
            get
            {
                if (workerInt == null && workerClass != null)
                {
                    try
                    {
                        workerInt = (PurchaseOutcomeWorker)Activator.CreateInstance(workerClass);
                        workerInt.def = this;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SimShop] PurchaseOutcomeDef {defName} 初始化 worker 失败: {ex}");
                    }
                }

                return workerInt;
            }
        }

        public bool MatchesThing(ThingDef purchasedDef)
        {
            if (purchasedDef == null)
                return false;

            bool hasThingRules = triggerThingDefs != null && triggerThingDefs.Count > 0;
            bool hasGoodsRules = !GetTriggerGoodsCategoryIds().NullOrEmpty();

            // 未配置条件时，允许任意购买命中。
            if (!hasThingRules && !hasGoodsRules)
                return true;

            if (hasThingRules && triggerThingDefs.Contains(purchasedDef))
                return true;

            List<string> triggerCategoryIds = GetTriggerGoodsCategoryIds();
            if (triggerCategoryIds.Count > 0)
            {
                for (int i = 0; i < triggerCategoryIds.Count; i++)
                {
                    if (GoodsCatalog.Contains(triggerCategoryIds[i], purchasedDef))
                        return true;
                }
            }

            return false;
        }

        public List<string> GetTriggerGoodsCategoryIds()
        {
            List<string> ids = new List<string>();
            if (!triggerGoodsCategoryIds.NullOrEmpty())
                ids.AddRange(triggerGoodsCategoryIds.Where(id => !string.IsNullOrEmpty(id)));
            if (!triggerGoodsDefs.NullOrEmpty())
                ids.AddRange(triggerGoodsDefs.Where(g => g != null).Select(g => g.defName));
            return ids.Distinct().ToList();
        }
    }

    // 购后行为逻辑抽象基类。
    public abstract class PurchaseOutcomeWorker
    {
        public PurchaseOutcomeDef def;

        public virtual bool CanTrigger(Pawn customer, ThingDef purchasedDef, int purchasedCount, Zone_Shop shopZone)
        {
            return customer != null
                && purchasedDef != null
                && purchasedCount > 0
                && def != null
                && Rand.Chance(Mathf.Clamp01(def.triggerChance));
        }

        // 返回要执行的后续 Jobs，按枚举顺序入队。
        public abstract IEnumerable<Job> TryMakeJobs(Pawn customer, ThingDef purchasedDef, int purchasedCount, Zone_Shop shopZone);
    }
}
