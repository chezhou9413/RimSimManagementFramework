using RimWorld;
using SimManagementLib.SimAI.CustomerVisit;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //类职责：为结账阶段顾客读取商店收银台快照和地图级票据，生成稳定排队 Job。
    public class JobGiver_CustomerCheckout : ThinkNode_JobGiver
    {
        //根据账单、购后队列和收银台状态决定下一项结账工作。
        protected override Job TryGiveJob(Pawn pawn)
        {
            Lord lord = pawn.Map.lordManager.LordOf(pawn);
            LordJob_CustomerVisit lordJob = lord?.LordJob as LordJob_CustomerVisit;
            if (lordJob == null) return null;
            if (pawn.Map.GetComponent<CustomerArrivalManager>()?.TryConsumeBehaviorBudget(pawn) != true) return null;
            CustomerVisitSession session = lordJob.GetOrCreateSession(pawn);
            bool isPostCheckout = session?.stage == CustomerVisitStage.PostCheckout;
            if (session == null || (!isPostCheckout && !session.AllowsJobGiver(CustomerVisitStage.Checkout)))
                return null;

            int pawnId = pawn.thingIDNumber;
            float owed = lordJob.GetAmountOwedForCheckout(pawnId);
            if (owed <= 0f)
            {
                if (lordJob.TryTakeNextPostCheckoutJob(pawnId, out Job postJob))
                {
                    return postJob;
                }

                if (lordJob.NeedsPostCheckoutCompletion(pawnId))
                {
                    lordJob.MarkPostCheckoutCompleted(pawnId);
                    session.NotifyPostCheckoutCompleted(lordJob, pawn, "购后行为完成");
                    lordJob.CheckAllCheckoutsDone();
                }

                return null;
            }

            Zone_Shop targetShop = lordJob.GetCurrentShop(pawn);
            CustomerArrivalManager manager = pawn.Map.GetComponent<CustomerArrivalManager>();
            Building_CashRegister register = FindBestRegister(pawn, targetShop, manager);
            if (register == null)
            {
                SimDebugLogger.Journey("RSMF.Checkout", $"没有找到可用收银台，取消本次结账 owed={owed}", pawn, targetShop, -1);
                lordJob.FailCheckoutAndLeave(pawn, SimTranslation.T("RSMF.Checkout.NoReachableRegister"));
                return null;
            }
            if (manager?.TryConsumeReachabilityBudget() != true)
                return null;
            if (!CustomerSafetyUtility.CanCustomerReach(pawn, register, PathEndMode.Touch, Danger.Deadly))
            {
                lordJob.FailCheckoutAndLeave(pawn, SimTranslation.T("RSMF.Checkout.NoReachableRegister"));
                return null;
            }

            IntVec3 serviceCell = CheckoutQueueCellUtility.FindServiceCell(register, pawn);
            if (!CheckoutQueueCellUtility.IsServiceCellStructurallyUsable(pawn.Map, serviceCell, pawn))
            {
                lordJob.FailCheckoutAndLeave(pawn, "收银台服务格不可用");
                return null;
            }

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_PayAtRegister"), register);
            job.SetTarget(TargetIndex.B, serviceCell);
            job.SetTarget(TargetIndex.C, serviceCell);
            //只有完整结账 Job 已构造成功后才切换阶段，避免预算轮空时被看门狗误判为结账失败。
            if (!isPostCheckout)
                session.NotifyCheckoutStarted(lordJob, pawn);
            return job;
        }

        //选择当前最合适的收银台，综合统计已排队与正在前往的顾客数并只对最终候选做一次可达性判断。
        private static Building_CashRegister FindBestRegister(Pawn pawn, Zone_Shop targetShop, CustomerArrivalManager manager)
        {
            if (pawn?.Map == null || targetShop == null) return null;
            Building_CashRegister best = null;
            int bestLoad = int.MaxValue;
            int bestDistance = int.MaxValue;
            IReadOnlyList<Building_CashRegister> registers = ShopDataUtility.GetCashRegisterSnapshotInZone(targetShop);
            for (int i = 0; i < registers.Count; i++)
            {
                Building_CashRegister register = registers[i];
                if (register == null || register.Destroyed || !register.Spawned) continue;
                int load = manager?.RuntimeIndex.CountRegisterLoad(register, pawn.thingIDNumber) ?? 0;
                int distance = (register.Position - pawn.Position).LengthHorizontalSquared;
                if (IsBetterRegister(register, load, distance, best, bestLoad, bestDistance))
                {
                    best = register;
                    bestLoad = load;
                    bestDistance = distance;
                }
            }
            return best;
        }

        //判断候选收银台是否优于当前最佳收银台，优先有人值守、负载更轻和距离更近。
        private static bool IsBetterRegister(Building_CashRegister candidate, int load, int distance, Building_CashRegister best, int bestLoad, int bestDistance)
        {
            if (best == null) return true;
            if (candidate.IsManned != best.IsManned) return candidate.IsManned;
            if (load != bestLoad) return load < bestLoad;
            return distance < bestDistance;
        }

    }
}
