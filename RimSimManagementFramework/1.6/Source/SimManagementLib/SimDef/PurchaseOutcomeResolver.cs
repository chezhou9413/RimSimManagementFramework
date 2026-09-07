using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimZone;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimDef
{
    public static class PurchaseOutcomeResolver
    {
        public static bool TryQueuePostPurchaseJobs(
            Pawn customer,
            LordJob_CustomerVisit lordJob,
            int pawnId,
            Zone_Shop shopZone,
            List<CustomerCartItem> purchasedItems)
        {
            if (customer == null || lordJob == null || purchasedItems.NullOrEmpty())
                return false;

            IReadOnlyList<RuntimePurchaseOutcomeRule> allRules = Tool.PurchaseOutcomeCatalog.Rules;
            if (allRules == null || allRules.Count <= 0)
                return false;

            List<Job> queuedJobs = new List<Job>();

            for (int defIndex = 0; defIndex < allRules.Count; defIndex++)
            {
                RuntimePurchaseOutcomeRule outcomeRule = allRules[defIndex];
                if (outcomeRule == null)
                    continue;

                PurchaseOutcomeWorker worker = outcomeRule.worker;
                if (worker == null)
                    continue;

                int maxJobs = Mathf.Max(1, outcomeRule.maxJobsToQueue);
                int queuedByThisDef = 0;

                if (outcomeRule.triggerOncePerCheckout)
                {
                    CustomerCartItem matched = purchasedItems.FirstOrDefault(i => i != null && i.def != null && i.count > 0 && outcomeRule.MatchesThing(i.def));
                    if (matched != null && worker.CanTrigger(customer, matched.def, matched.count, shopZone))
                        QueueWorkerJobs(worker, customer, matched.def, matched.count, shopZone, queuedJobs, ref queuedByThisDef, maxJobs);
                }
                else
                {
                    for (int i = 0; i < purchasedItems.Count; i++)
                    {
                        if (queuedByThisDef >= maxJobs)
                            break;

                        CustomerCartItem item = purchasedItems[i];
                        if (item == null || item.def == null || item.count <= 0)
                            continue;
                        if (!outcomeRule.MatchesThing(item.def))
                            continue;
                        if (!worker.CanTrigger(customer, item.def, item.count, shopZone))
                            continue;

                        QueueWorkerJobs(worker, customer, item.def, item.count, shopZone, queuedJobs, ref queuedByThisDef, maxJobs);
                    }
                }
            }

            if (queuedJobs.Count <= 0)
                return false;

            lordJob.QueuePostCheckoutJobs(pawnId, queuedJobs);
            return true;
        }

        private static void QueueWorkerJobs(
            PurchaseOutcomeWorker worker,
            Pawn customer,
            ThingDef purchasedDef,
            int purchasedCount,
            Zone_Shop shopZone,
            List<Job> queuedJobs,
            ref int queuedByThisDef,
            int maxJobs)
        {
            IEnumerable<Job> jobs = worker.TryMakeJobs(customer, purchasedDef, purchasedCount, shopZone);
            if (jobs == null)
                return;

            foreach (Job job in jobs)
            {
                if (job == null)
                    continue;

                queuedJobs.Add(job);
                queuedByThisDef++;
                if (queuedByThisDef >= maxJobs)
                    break;
            }
        }
    }
}
