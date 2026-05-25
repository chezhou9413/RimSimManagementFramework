using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 保存顾客结账队列、准备结账标记和购后 Job 队列，负责结账阶段的运行状态。
    /// </summary>
    public class CustomerCheckoutState
    {
        public Dictionary<int, int> checkoutOrder = new Dictionary<int, int>();
        public int nextCheckoutOrder = 1;
        public List<int> readyForCheckout = new List<int>();

        // 购后 Job 队列只保留在运行时，避免把依赖地图实时对象的 Job 写入存档。
        [Unsaved] private readonly Dictionary<int, List<Job>> postCheckoutJobs = new Dictionary<int, List<Job>>();
        [Unsaved] private readonly HashSet<int> postCheckoutRequired = new HashSet<int>();

        /// <summary>
        /// 读写结账队列存档数据，并在读档后补齐集合实例。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref checkoutOrder, "checkoutOrder", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref nextCheckoutOrder, "nextCheckoutOrder", 1);
            Scribe_Collections.Look(ref readyForCheckout, "readyForCheckout", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (checkoutOrder == null) checkoutOrder = new Dictionary<int, int>();
                if (readyForCheckout == null) readyForCheckout = new List<int>();
                if (nextCheckoutOrder <= 0) nextCheckoutOrder = 1;
            }
        }

        /// <summary>
        /// 标记顾客已经准备结账。
        /// </summary>
        public void MarkPawnReadyForCheckout(int pawnId)
        {
            if (pawnId <= 0) return;
            if (!readyForCheckout.Contains(pawnId))
                readyForCheckout.Add(pawnId);
        }

        /// <summary>
        /// 判断顾客是否已经准备结账。
        /// </summary>
        public bool IsPawnReadyForCheckout(int pawnId)
        {
            return pawnId > 0 && readyForCheckout.Contains(pawnId);
        }

        /// <summary>
        /// 获取或分配顾客的固定结账顺序。
        /// </summary>
        public int EnsureCheckoutOrder(int pawnId)
        {
            if (checkoutOrder.TryGetValue(pawnId, out int order)) return order;
            int next = nextCheckoutOrder++;
            checkoutOrder[pawnId] = next;
            return next;
        }

        /// <summary>
        /// 返回顾客已分配的结账顺序。
        /// </summary>
        public int GetCheckoutOrder(int pawnId)
        {
            return checkoutOrder.TryGetValue(pawnId, out int order) ? order : int.MaxValue;
        }

        /// <summary>
        /// 清除顾客的结账顺序。
        /// </summary>
        public void ClearCheckoutOrder(int pawnId)
        {
            if (pawnId <= 0) return;
            checkoutOrder.Remove(pawnId);
        }

        /// <summary>
        /// 加入付款后需要执行的 Job 队列。
        /// </summary>
        public void QueuePostCheckoutJobs(int pawnId, IEnumerable<Job> jobs)
        {
            if (pawnId <= 0 || jobs == null) return;

            List<Job> list = jobs.Where(j => j != null).ToList();
            if (list.NullOrEmpty()) return;

            if (!postCheckoutJobs.TryGetValue(pawnId, out List<Job> existing))
            {
                existing = new List<Job>();
                postCheckoutJobs[pawnId] = existing;
            }

            existing.AddRange(list);
            postCheckoutRequired.Add(pawnId);
        }

        /// <summary>
        /// 取出顾客下一项购后 Job。
        /// </summary>
        public bool TryTakeNextPostCheckoutJob(int pawnId, out Job job)
        {
            job = null;
            if (pawnId <= 0) return false;
            if (!postCheckoutJobs.TryGetValue(pawnId, out List<Job> list) || list.NullOrEmpty()) return false;

            job = list[0];
            list.RemoveAt(0);
            if (list.Count <= 0)
                postCheckoutJobs.Remove(pawnId);

            return job != null;
        }

        /// <summary>
        /// 判断顾客是否仍需要完成购后阶段。
        /// </summary>
        public bool NeedsPostCheckoutCompletion(int pawnId)
        {
            return pawnId > 0 && postCheckoutRequired.Contains(pawnId);
        }

        /// <summary>
        /// 清除顾客购后阶段的运行状态。
        /// </summary>
        public void MarkPostCheckoutCompleted(int pawnId)
        {
            if (pawnId <= 0) return;
            postCheckoutRequired.Remove(pawnId);
            postCheckoutJobs.Remove(pawnId);
        }

        /// <summary>
        /// 返回指定顾客当前购后 Job 队列的简短说明。
        /// </summary>
        public string DescribePostCheckoutJobs(int pawnId)
        {
            if (pawnId <= 0 || !postCheckoutJobs.TryGetValue(pawnId, out List<Job> list) || list.NullOrEmpty())
                return "无付款后行为。";

            List<string> parts = new List<string>();
            for (int i = 0; i < list.Count && parts.Count < 5; i++)
            {
                Job job = list[i];
                if (job?.def == null) continue;
                string label = !string.IsNullOrEmpty(job.def.label) ? job.def.label : job.def.defName;
                string targets = DescribeJobTargets(job);
                parts.Add(string.IsNullOrEmpty(targets) ? label : label + "(" + targets + ")");
            }

            return parts.Count > 0 ? string.Join("；", parts) : "无可描述的付款后行为。";
        }

        /// <summary>
        /// 构造 Job 目标摘要，负责避免把完整 Job 对象传给后台点评线程。
        /// </summary>
        private static string DescribeJobTargets(Job job)
        {
            List<string> parts = new List<string>();
            AppendTarget(parts, "A", job.targetA);
            AppendTarget(parts, "B", job.targetB);
            return parts.Count > 0 ? string.Join("，", parts) : "";
        }

        /// <summary>
        /// 添加单个 Job 目标摘要。
        /// </summary>
        private static void AppendTarget(List<string> parts, string label, LocalTargetInfo target)
        {
            if (!target.IsValid) return;
            if (target.HasThing && target.Thing != null)
                parts.Add(label + ":" + target.Thing.LabelShortCap);
            else if (target.Cell.IsValid)
                parts.Add(label + ":" + target.Cell.ToString());
        }
    }
}
