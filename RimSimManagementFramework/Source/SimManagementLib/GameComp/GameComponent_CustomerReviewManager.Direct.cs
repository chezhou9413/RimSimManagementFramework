using SimManagementLib.Api;
using SimManagementLib.Pojo;
using System.Collections.Generic;

namespace SimManagementLib.GameComp
{
    //保存玩法直接产出的评价，历史裁剪后仍记住已提交编号。
    public partial class GameComponent_CustomerReviewManager
    {
        private HashSet<string> directReviewIds = new HashSet<string>();

        //按业务编号幂等记录已完成评价，保留其他店铺的原有记录。
        internal SimApiResult RecordDirectReview(CustomerReviewRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.reviewId))
                return SimApiResult.Fail("直接评价缺少有效编号");
            if (directReviewIds.Contains(record.reviewId)) return SimApiResult.Success();
            if (FindRecord(record.reviewId) != null)
            {
                directReviewIds.Add(record.reviewId);
                return SimApiResult.Success();
            }
            if (record.zoneId < 0 || record.stars < 1 || record.stars > 5
                || string.IsNullOrWhiteSpace(record.reviewText) || record.spentSilver < 0f
                || float.IsNaN(record.spentSilver) || float.IsInfinity(record.spentSilver))
                return SimApiResult.Fail("直接评价的店铺、星级、正文或金额无效");

            record.provider = CustomerReviewProvider.Simulation;
            record.generationStatus = CustomerReviewGenerationStatus.Completed;
            record.isHeavyMode = false;
            records.Add(record);
            directReviewIds.Add(record.reviewId);
            TrimRecords();
            return SimApiResult.Success();
        }
    }
}
