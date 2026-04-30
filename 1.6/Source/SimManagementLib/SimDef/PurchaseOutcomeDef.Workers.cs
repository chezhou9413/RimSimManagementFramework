using RimWorld;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 通用 worker：执行 XML 中配置的 JobDef。
    /// </summary>
    public class PurchaseOutcomeWorker_RunConfiguredJob : PurchaseOutcomeWorker
    {
        public override IEnumerable<Job> TryMakeJobs(Pawn customer, ThingDef purchasedDef, int purchasedCount, Zone_Shop shopZone)
        {
            if (customer == null || customer.Map == null || def == null || def.configuredJobDef == null)
                yield break;

            Job job = JobMaker.MakeJob(def.configuredJobDef);

            PurchaseOutcomeTargetResolver.TryResolveTargets(
                customer,
                shopZone,
                def.targetMode,
                out LocalTargetInfo targetA,
                out LocalTargetInfo targetB);

            if (targetA.IsValid)
                job.SetTarget(TargetIndex.A, targetA);
            if (targetB.IsValid)
                job.SetTarget(TargetIndex.B, targetB);

            int duration = def.jobDurationTicks.RandomInRange;
            if (duration > 0)
                job.expiryInterval = duration;

            yield return job;
        }
    }

    /// <summary>
    /// 内置堂食 worker：优先用 Ingest 在店内消费本次购买的食物。
    /// </summary>
    public class PurchaseOutcomeWorker_DineInShop : PurchaseOutcomeWorker
    {
        public override IEnumerable<Job> TryMakeJobs(Pawn customer, ThingDef purchasedDef, int purchasedCount, Zone_Shop shopZone)
        {
            if (customer == null || customer.Map == null || def == null)
                yield break;

            JobDef dineJobDef = def.configuredJobDef ?? JobDefOf.Ingest;
            if (dineJobDef == null)
                yield break;

            PurchaseOutcomeTargetResolver.TryFindDiningTargets(
                customer,
                shopZone,
                out LocalTargetInfo seatTarget,
                out LocalTargetInfo tableTarget);

            if (dineJobDef == JobDefOf.Ingest)
            {
                ThingDef foodDef = ResolveMealThingDef(customer, purchasedDef);
                if (foodDef == null)
                    yield break;

                Thing foodOnPawn = CreateFoodOnPawn(customer, foodDef);
                if (foodOnPawn == null)
                    yield break;

                Job ingestJob = JobMaker.MakeJob(JobDefOf.Ingest, foodOnPawn);
                ingestJob.count = 1;
                if (tableTarget.IsValid)
                    ingestJob.SetTarget(TargetIndex.B, tableTarget);

                yield return ingestJob;
                yield break;
            }

            // 如果开发者改成了别的 JobDef，就退回通用的目标赋值模式。
            Job fallbackJob = JobMaker.MakeJob(dineJobDef);
            if (seatTarget.IsValid)
                fallbackJob.SetTarget(TargetIndex.A, seatTarget);
            if (tableTarget.IsValid)
                fallbackJob.SetTarget(TargetIndex.B, tableTarget);

            int duration = def.jobDurationTicks.RandomInRange;
            if (duration <= 0)
                duration = 600;

            fallbackJob.expiryInterval = duration;
            yield return fallbackJob;
        }

        private static ThingDef ResolveMealThingDef(Pawn customer, ThingDef purchasedDef)
        {
            if (IsIngestibleFor(customer, purchasedDef))
                return purchasedDef;

            // 这里只允许消费本次买到的食物，不自动替换成其它餐品。
            return null;
        }

        private static Thing CreateFoodOnPawn(Pawn customer, ThingDef foodDef)
        {
            if (customer == null || foodDef == null)
                return null;

            Thing food = ThingMaker.MakeThing(foodDef, foodDef.MadeFromStuff ? GenStuff.DefaultStuffFor(foodDef) : null);
            if (food == null)
                return null;

            food.stackCount = 1;

            ThingOwner inventory = customer.inventory?.innerContainer;
            if (inventory != null && inventory.TryAdd(food))
            {
                // 入背包后可能和同类物品堆叠，所以优先返回实际留在库存里的实例。
                if (!food.Destroyed)
                    return food;

                Thing merged = inventory.FirstOrDefault(t => t != null && !t.Destroyed && t.def == foodDef && t.stackCount > 0);
                if (merged != null)
                    return merged;
            }

            // 背包放不进去时再尝试手持，避免向地图额外刷出物品。
            if (customer.carryTracker != null)
            {
                try
                {
                    if (customer.carryTracker.TryStartCarry(food))
                        return customer.carryTracker.CarriedThing;
                }
                catch
                {
                }
            }

            if (!food.Destroyed)
                food.Destroy(DestroyMode.Vanish);

            return null;
        }

        private static bool IsIngestibleFor(Pawn customer, ThingDef def)
        {
            if (customer == null || def == null || def.ingestible == null)
                return false;

            return customer.RaceProps != null && customer.RaceProps.CanEverEat(def);
        }
    }

    /// <summary>
    /// 内置成瘾品 worker：先在店内停留使用，再执行原版 Ingest 以应用物品效果。
    /// </summary>
    public class PurchaseOutcomeWorker_UseDrugInShop : PurchaseOutcomeWorker
    {
        public override IEnumerable<Job> TryMakeJobs(Pawn customer, ThingDef purchasedDef, int purchasedCount, Zone_Shop shopZone)
        {
            if (customer == null || customer.Map == null || def == null)
                yield break;

            if (!IsIngestibleFor(customer, purchasedDef))
                yield break;

            Thing drugOnPawn = CreateConsumableOnPawn(customer, purchasedDef);
            if (drugOnPawn == null)
                yield break;

            PurchaseOutcomeTargetResolver.TryResolveTargets(
                customer,
                shopZone,
                def.targetMode == PostPurchaseTargetMode.None ? PostPurchaseTargetMode.RandomStandableCellInShop : def.targetMode,
                out LocalTargetInfo targetA,
                out LocalTargetInfo targetB);

            JobDef useJobDef = def.configuredJobDef ?? DefDatabase<JobDef>.GetNamedSilentFail("Customer_UseDrugInShop");
            if (useJobDef != null)
            {
                Job useJob = JobMaker.MakeJob(useJobDef);
                if (targetA.IsValid)
                    useJob.SetTarget(TargetIndex.A, targetA);
                if (targetB.IsValid)
                    useJob.SetTarget(TargetIndex.B, targetB);

                int duration = def.jobDurationTicks.RandomInRange;
                if (duration <= 0)
                    duration = 600;

                useJob.expiryInterval = duration;
                yield return useJob;
            }

            Job ingestJob = JobMaker.MakeJob(JobDefOf.Ingest, drugOnPawn);
            ingestJob.count = 1;
            if (targetB.IsValid)
                ingestJob.SetTarget(TargetIndex.B, targetB);

            yield return ingestJob;
        }

        private static Thing CreateConsumableOnPawn(Pawn customer, ThingDef thingDef)
        {
            if (customer == null || thingDef == null)
                return null;

            Thing thing = ThingMaker.MakeThing(thingDef, thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef) : null);
            if (thing == null)
                return null;

            thing.stackCount = 1;

            ThingOwner inventory = customer.inventory?.innerContainer;
            if (inventory != null && inventory.TryAdd(thing))
            {
                if (!thing.Destroyed)
                    return thing;

                for (int i = 0; i < inventory.Count; i++)
                {
                    Thing existing = inventory[i];
                    if (existing != null && !existing.Destroyed && existing.def == thingDef && existing.stackCount > 0)
                        return existing;
                }
            }

            if (customer.carryTracker != null)
            {
                try
                {
                    if (customer.carryTracker.TryStartCarry(thing))
                        return customer.carryTracker.CarriedThing;
                }
                catch
                {
                }
            }

            if (!thing.Destroyed)
                thing.Destroy(DestroyMode.Vanish);

            return null;
        }

        private static bool IsIngestibleFor(Pawn customer, ThingDef def)
        {
            if (customer == null || def == null || def.ingestible == null)
                return false;

            return customer.RaceProps != null && customer.RaceProps.CanEverEat(def);
        }
    }
}
