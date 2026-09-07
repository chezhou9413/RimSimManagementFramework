using SimManagementLib.SimZone;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供多 Pawn 顾客动作会话 Worker，负责让外部玩法编排顾客、员工和目标建筑之间的协作服务。
    /// </summary>
    public class CustomerActionSessionWorker : CustomerActionWorker
    {
        /// <summary>
        /// 返回动作订单需要的员工数量，默认需要一名员工。
        /// </summary>
        public virtual int GetRequiredStaffCount(CustomerActionContext context)
        {
            return 1;
        }

        /// <summary>
        /// 判断指定员工是否可加入会话订单，默认要求员工有效且订单未结束。
        /// </summary>
        public virtual bool CanStaffJoin(CustomerActionContext context, Pawn staff, out string reason)
        {
            reason = "";
            if (staff == null || staff.Destroyed || staff.Dead)
            {
                reason = "员工无效";
                return false;
            }
            if (context?.order == null || !context.order.IsActiveState)
            {
                reason = "动作订单不可加入";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 创建员工参与会话的 Job，默认不提供具体工作。
        /// </summary>
        public virtual Job MakeStaffSessionJob(CustomerActionContext context, Pawn staff)
        {
            return null;
        }

        /// <summary>
        /// 在员工加入会话时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void NotifyStaffJoined(CustomerActionContext context, Pawn staff)
        {
        }

        /// <summary>
        /// 在员工离开会话时接收通知，默认不执行额外逻辑。
        /// </summary>
        public virtual void NotifyStaffLeft(CustomerActionContext context, Pawn staff, string reason)
        {
        }

        /// <summary>
        /// 在会话完成时接收通知，默认使用顾客动作订单完成逻辑。
        /// </summary>
        public virtual void NotifySessionCompleted(CustomerActionContext context)
        {
            NotifyOrderCompleted(context);
        }

        /// <summary>
        /// 查找会话目标建筑，默认使用订单记录的目标。
        /// </summary>
        public virtual Thing FindSessionTarget(CustomerActionContext context, Map map, Zone_Shop shop)
        {
            return SimShopCustomerApi.FindActionOrderTarget(map, context?.order);
        }
    }
}
