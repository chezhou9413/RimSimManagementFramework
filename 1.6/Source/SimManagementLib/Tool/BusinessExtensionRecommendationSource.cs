using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 管理扩展推荐页数据来源，负责优先使用服务端推荐并在失败时回退本地 Def。
    /// </summary>
    public static class BusinessExtensionRecommendationSource
    {
        private static Task<List<RecommendedModNetworkItemData>> runningTask;
        private static CancellationTokenSource runningCts;
        private static List<IBusinessExtensionRecommendation> serverRows;
        private static bool requestFinished;

        /// <summary>
        /// 返回当前推荐项列表，负责按服务端优先、本地 Def 兜底的顺序选择数据源。
        /// </summary>
        public static List<IBusinessExtensionRecommendation> GetRows()
        {
            EnsureRequestStarted();
            PollRequest();
            if (!serverRows.NullOrEmpty())
                return serverRows.ToList();

            return DefDatabase<BusinessExtensionRecommendationDef>.AllDefsListForReading
                .OrderBy(def => def.Order)
                .ThenBy(def => def.StableId)
                .Cast<IBusinessExtensionRecommendation>()
                .ToList();
        }

        /// <summary>
        /// 启动服务端推荐请求，负责每局只尝试一次避免失败后频繁重试。
        /// </summary>
        private static void EnsureRequestStarted()
        {
            if (requestFinished || runningTask != null)
                return;

            runningCts = new CancellationTokenSource();
            runningTask = RecommendedModNetworkApiClient.GetRecommendedModsAsync(runningCts.Token);
        }

        /// <summary>
        /// 轮询服务端推荐请求，负责在主线程切换到远端列表或回退本地列表。
        /// </summary>
        private static void PollRequest()
        {
            if (runningTask == null || !runningTask.IsCompleted)
                return;

            Task<List<RecommendedModNetworkItemData>> task = runningTask;
            runningTask = null;
            runningCts?.Dispose();
            runningCts = null;
            requestFinished = true;

            if (task.IsFaulted || task.IsCanceled || task.Result.NullOrEmpty())
                return;

            serverRows = task.Result
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.StableId))
                .OrderBy(item => item.Order)
                .ThenBy(item => item.StableId)
                .Cast<IBusinessExtensionRecommendation>()
                .ToList();
        }
    }
}
