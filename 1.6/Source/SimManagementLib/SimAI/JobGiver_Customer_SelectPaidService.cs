using SimManagementLib.SimService;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 为顾客分配可用的付费服务建筑，负责按预算、可达性、并发和服务 Worker 判断目标服务。
    /// </summary>
    public class JobGiver_Customer_SelectPaidService : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            LordJob_CustomerVisit lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            if (lordJob == null) return null;

            int pawnId = pawn.thingIDNumber;
            if (lordJob.HasReachedConsumptionLimit(pawnId))
            {
                lordJob.MarkPawnReadyForCheckout(pawnId);
                return null;
            }

            Zone_Shop shopZone = lordJob.GetCurrentShop(pawn);
            if (shopZone == null) return null;

            float remainingBudget = lordJob.GetRemainingTripBudget(pawn, shopZone);
            if (remainingBudget <= 0f)
            {
                lordJob.MarkPawnReadyForCheckout(pawnId);
                return null;
            }

            // 服务和商品共享浏览阶段，随机让出一部分机会给商品货柜，避免纯优先级导致顾客只选服务。
            if (ShopDataUtility.GetInStockGoods(shopZone).Count > 0 && Rand.Value > 0.45f)
                return null;

            if (!ShopServiceUtility.TryFindServiceForCustomer(
                    pawn,
                    shopZone,
                    remainingBudget,
                    out Thing provider,
                    out ShopServiceDef serviceDef,
                    out float price))
                return null;

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_SelectPaidService"), provider);
            job.count = Mathf.CeilToInt(price);
            job.reportStringOverride = serviceDef.DisplayLabel;
            return job;
        }
    }
}
