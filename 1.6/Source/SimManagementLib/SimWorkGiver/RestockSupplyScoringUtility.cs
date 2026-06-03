using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.SimWorkGiver
{
    /// <summary>
    /// 提供补货货源候选的快照评分能力，负责把大量距离计算从主线程路径验证中拆分出来。
    /// </summary>
    internal static class RestockSupplyScoringUtility
    {
        private const int ParallelSupplyScoreThreshold = 128;

        /// <summary>
        /// 对供货候选快照计算距离分数，负责按近到远返回主线程验证顺序。
        /// </summary>
        public static List<ScoredSupplyCandidate> ScoreSupplyCandidates(List<SupplyCandidateSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count <= 0)
                return new List<ScoredSupplyCandidate>();

            List<ScoredSupplyCandidate> scoredCandidates = snapshots.Count >= ParallelSupplyScoreThreshold
                ? ScoreSupplyCandidatesParallel(snapshots)
                : ScoreSupplyCandidatesSequential(snapshots);

            scoredCandidates.Sort((a, b) => a.Score.CompareTo(b.Score));
            return scoredCandidates;
        }

        /// <summary>
        /// 顺序计算少量候选距离分数，负责避免小列表进入线程池造成额外开销。
        /// </summary>
        private static List<ScoredSupplyCandidate> ScoreSupplyCandidatesSequential(List<SupplyCandidateSnapshot> snapshots)
        {
            List<ScoredSupplyCandidate> result = new List<ScoredSupplyCandidate>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
                result.Add(snapshots[i].ToScoredCandidate());
            return result;
        }

        /// <summary>
        /// 并行计算大量候选距离分数，负责只处理主线程准备好的纯快照数据。
        /// </summary>
        private static List<ScoredSupplyCandidate> ScoreSupplyCandidatesParallel(List<SupplyCandidateSnapshot> snapshots)
        {
            ConcurrentBag<ScoredSupplyCandidate> results = new ConcurrentBag<ScoredSupplyCandidate>();
            SupplyCandidateSnapshot[] snapshotArray = snapshots.ToArray();
            Parallel.ForEach(snapshotArray, snapshot =>
            {
                results.Add(snapshot.ToScoredCandidate());
            });
            return new List<ScoredSupplyCandidate>(results);
        }

        /// <summary>
        /// 保存供货候选的主线程快照，负责让并行评分不直接读取 RimWorld 可变集合。
        /// </summary>
        internal sealed class SupplyCandidateSnapshot
        {
            private readonly Thing thing;
            private readonly IntVec3 thingPosition;
            private readonly IntVec3 pawnPosition;
            private readonly bool assignedPawn;

            /// <summary>
            /// 创建供货候选快照，负责记录距离评分需要的稳定数据。
            /// </summary>
            public SupplyCandidateSnapshot(Thing thing, IntVec3 thingPosition, IntVec3 pawnPosition, bool assignedPawn)
            {
                this.thing = thing;
                this.thingPosition = thingPosition;
                this.pawnPosition = pawnPosition;
                this.assignedPawn = assignedPawn;
            }

            /// <summary>
            /// 转换为带分数候选，负责在纯快照数据上计算距离权重。
            /// </summary>
            public ScoredSupplyCandidate ToScoredCandidate()
            {
                float score = (thingPosition - pawnPosition).LengthHorizontalSquared;
                if (assignedPawn)
                    score *= 0.35f;
                return new ScoredSupplyCandidate(thing, score);
            }
        }

        /// <summary>
        /// 保存供货候选和距离分数，负责让主线程按近到远执行预约和寻路验证。
        /// </summary>
        internal sealed class ScoredSupplyCandidate
        {
            public readonly Thing Thing;
            public readonly float Score;

            /// <summary>
            /// 创建已评分供货候选。
            /// </summary>
            public ScoredSupplyCandidate(Thing thing, float score)
            {
                Thing = thing;
                Score = score;
            }
        }
    }
}
