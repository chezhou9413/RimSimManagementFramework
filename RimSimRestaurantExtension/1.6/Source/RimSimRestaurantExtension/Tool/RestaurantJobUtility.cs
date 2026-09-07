using Verse.AI;
using Verse;

namespace RimSimRestaurantExtension.Tool
{
    //提供餐厅 Job 的公共字段读写方法，负责避免订单编号占用原版 count 语义。
    public static class RestaurantJobUtility
    {
        //把餐厅订单编号写入 Job 标签，负责让 count 保留给搬运和进食数量。
        public static void SetOrderId(Job job, int orderId)
        {
            if (job == null) return;
            job.ritualTag = orderId.ToString();
        }

        //从 Job 标签读取餐厅订单编号，负责拒绝把搬运或进食数量误识别成订单编号。
        public static int GetOrderId(Job job)
        {
            if (job == null) return 0;
            if (!job.ritualTag.NullOrEmpty() && int.TryParse(job.ritualTag, out int taggedId))
                return taggedId;
            return 0;
        }
    }
}
