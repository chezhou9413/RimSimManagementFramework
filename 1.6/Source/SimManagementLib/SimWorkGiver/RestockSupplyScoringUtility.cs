using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimWorkGiver
{
    //补货货源评分工具，职责是保留顺序距离评分能力供低频调试或旧调用使用。
    internal static class RestockSupplyScoringUtility
    {
        //对供货候选快照计算距离分数，职责是按近到远返回主线程验证顺序。
        public static List<ScoredSupplyCandidate> ScoreSupplyCandidates(List<SupplyCandidateSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count <= 0)
                return new List<ScoredSupplyCandidate>();

            List<ScoredSupplyCandidate> scoredCandidates = ScoreSupplyCandidatesSequential(snapshots);
            scoredCandidates.Sort((a, b) => a.Score.CompareTo(b.Score));
            return scoredCandidates;
        }

        //顺序计算候选距离分数，职责是避免高频工作扫描进入线程池造成额外开销。
        private static List<ScoredSupplyCandidate> ScoreSupplyCandidatesSequential(List<SupplyCandidateSnapshot> snapshots)
        {
            List<ScoredSupplyCandidate> result = new List<ScoredSupplyCandidate>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
                result.Add(snapshots[i].ToScoredCandidate());
            return result;
        }

        //供货候选快照，职责是保存距离评分需要的稳定数据。
        internal sealed class SupplyCandidateSnapshot
        {
            private readonly Thing thing;
            private readonly IntVec3 thingPosition;
            private readonly IntVec3 pawnPosition;
            private readonly bool assignedPawn;

            //创建供货候选快照，职责是记录距离评分需要的数据。
            public SupplyCandidateSnapshot(Thing thing, IntVec3 thingPosition, IntVec3 pawnPosition, bool assignedPawn)
            {
                this.thing = thing;
                this.thingPosition = thingPosition;
                this.pawnPosition = pawnPosition;
                this.assignedPawn = assignedPawn;
            }

            //转换为带分数候选，职责是计算距离权重。
            public ScoredSupplyCandidate ToScoredCandidate()
            {
                float score = (thingPosition - pawnPosition).LengthHorizontalSquared;
                if (assignedPawn)
                    score *= 0.35f;
                return new ScoredSupplyCandidate(thing, score);
            }
        }

        //供货候选评分结果，职责是保存候选 Thing 和距离分数。
        internal sealed class ScoredSupplyCandidate
        {
            public readonly Thing Thing;
            public readonly float Score;

            //创建已评分供货候选。
            public ScoredSupplyCandidate(Thing thing, float score)
            {
                Thing = thing;
                Score = score;
            }
        }
    }
}
