using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        /// <summary>
        /// 标记顾客准备结账，并在全体活跃顾客准备完毕时推进群体状态机。
        /// </summary>
        public void MarkPawnReadyForCheckout(int pawnId)
        {
            checkoutState.MarkPawnReadyForCheckout(pawnId);

            if (AreAllActivePawnsReadyForCheckout())
                lord?.ReceiveMemo("Customer_ReadyToCheckout");
        }

        /// <summary>
        /// 获取或分配顾客的固定结账顺序。
        /// </summary>
        public int EnsureCheckoutOrder(int pawnId)
        {
            return checkoutState.EnsureCheckoutOrder(pawnId);
        }

        /// <summary>
        /// 返回顾客已分配的结账顺序。
        /// </summary>
        public int GetCheckoutOrder(int pawnId)
        {
            return checkoutState.GetCheckoutOrder(pawnId);
        }

        /// <summary>
        /// 加入付款后需要执行的 Job 队列。
        /// </summary>
        public void QueuePostCheckoutJobs(int pawnId, IEnumerable<Job> jobs)
        {
            checkoutState.QueuePostCheckoutJobs(pawnId, jobs);
        }

        /// <summary>
        /// 取出顾客下一项购后 Job。
        /// </summary>
        public bool TryTakeNextPostCheckoutJob(int pawnId, out Job job)
        {
            return checkoutState.TryTakeNextPostCheckoutJob(pawnId, out job);
        }

        /// <summary>
        /// 判断顾客是否仍需要完成购后阶段。
        /// </summary>
        public bool NeedsPostCheckoutCompletion(int pawnId)
        {
            return checkoutState.NeedsPostCheckoutCompletion(pawnId);
        }

        /// <summary>
        /// 返回指定顾客当前购后 Job 队列的简短说明，负责给顾客评价快照提供售后行为上下文。
        /// </summary>
        public string DescribePostCheckoutJobs(int pawnId)
        {
            return checkoutState.DescribePostCheckoutJobs(pawnId);
        }

        /// <summary>
        /// 标记顾客购后阶段完成，并清除服务订单。
        /// </summary>
        public void MarkPostCheckoutCompleted(int pawnId)
        {
            checkoutState.MarkPostCheckoutCompleted(pawnId);
            ClearCustomerServiceOrders(pawnId);
        }

        /// <summary>
        /// 判断所有仍在地图上的顾客是否都已准备结账。
        /// </summary>
        private bool AreAllActivePawnsReadyForCheckout()
        {
            if (lord?.ownedPawns == null || lord.ownedPawns.Count == 0) return true;

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                if (!checkoutState.IsPawnReadyForCheckout(pawn.thingIDNumber))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 检查所有活跃顾客是否都完成结账和购后行为，完成时推进群体状态机离店。
        /// </summary>
        public void CheckAllCheckoutsDone()
        {
            bool allDone = true;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;

                int pawnId = pawn.thingIDNumber;
                if (cartValues.TryGetValue(pawnId, out float value) && value > 0f)
                {
                    allDone = false;
                    break;
                }

                // 顾客必须消费完所有购后服务 Job 后才算完成本次访问。
                if (checkoutState.NeedsPostCheckoutCompletion(pawnId))
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                lord.ReceiveMemo("Customer_CheckoutCompleted");
            }
        }
    }
}
