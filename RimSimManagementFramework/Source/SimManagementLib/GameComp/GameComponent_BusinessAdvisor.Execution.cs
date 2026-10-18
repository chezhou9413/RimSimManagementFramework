using System;
using SimManagementLib.BusinessAdvisor;

namespace SimManagementLib.GameComp
{
    //手动审批或玩家开启的全自动审批启动执行，模型不能改变审批设置。
    public sealed partial class GameComponent_BusinessAdvisor
    {
        private int executeChildIndex;
        private int executeSatisfiedCount;
        private bool executingAutomatically;

        //批准前重新核对整份方案，过期方案必须重新生成和批准。
        public void ApprovePlan(int revision)
        {
            if (!HasPendingPlan || Busy || revision != planRevision) return;
            BeginPlan(false);
        }

        //两种审批方式共用状态核验，批准具体参数后才开始推进。
        private void BeginPlan(bool automatic)
        {
            try
            {
                if (!SimManagementLibMod.Settings.advisorEnabled || !SimManagementLibMod.Settings.llmEnabled)
                    throw new InvalidOperationException("经营顾问已关闭。");
                foreach (AdvisorPreparedStep step in pending) Revalidate(step);
                if (automatic && !SimManagementLibMod.Settings.advisorAutoApprove) return;
                AddActivity(automatic ? "全自动审批通过" : "玩家已批准", PlanPreview);
                if (automatic) AddMessage("system", "全自动审批通过，开始执行 " + pending.Count + " 项操作。可点击停止任务。", PlanPreview);
                executeIndex = 0;
                executeChildIndex = 0;
                executeSatisfiedCount = 0;
                executingAutomatically = automatic;
                nextExecutionTick = Environment.TickCount + 300;
                cancelled = false;
                status = "准备执行 1/" + pending.Count + "：" + pending[0].preview;
            }
            catch (Exception ex) { Recover(ex, "审批前核验失败，本批尚未执行。方案：\n" + PlanPreview); }
        }

        //拒绝不触发任何修改，将拒绝作为下一轮上下文的一部分。
        public void RejectPlan(int revision)
        {
            if (!HasPendingPlan || revision != planRevision) return;
            pending = null;
            status = "玩家已拒绝方案，未执行任何步骤。";
            AddMessage("system", status);
            wire.Add(new AdvisorEntry { role = "user", text = status });
        }

        //批量指令每帧最多八个目标且不超过八毫秒，既显示进度也避免集中铺设卡顿。
        private void AdvanceExecution()
        {
            if (unchecked(Environment.TickCount - nextExecutionTick) < 0) return;
            try
            {
                AdvisorPreparedStep approved = pending[executeIndex];
                if (approved.children != null)
                {
                    int started = Environment.TickCount, count = 0;
                    while (executeChildIndex < approved.children.Count && count < 8 && unchecked(Environment.TickCount - started) < 8)
                    {
                        AdvisorPreparedStep current = Revalidate(approved.children[executeChildIndex]);
                        current.apply();
                        if (current.alreadySatisfied) executeSatisfiedCount++;
                        executeChildIndex++; count++;
                        ResetRecovery();
                    }
                    status = (executingAutomatically ? "全自动 · " : "") + "正在执行 " + (executeIndex + 1) + "/" + pending.Count
                        + " · 目标 " + executeChildIndex + "/" + approved.children.Count + " · " + Short(approved.preview.Replace("\n", " · "), 180);
                    if (executeChildIndex < approved.children.Count) return;
                }
                else
                {
                    AdvisorPreparedStep current = Revalidate(approved);
                    current.apply();
                    if (current.alreadySatisfied) executeSatisfiedCount++;
                    ResetRecovery();
                }
                string result = "处理完成 " + (executeIndex + 1) + "/" + pending.Count + "：" + approved.preview
                    + "\n执行时已满足并跳过 " + executeSatisfiedCount + " 个目标；区域标记和蓝图不代表施工完工。";
                AddActivity("操作完成", result);
                wire.Add(new AdvisorEntry { role = "user", text = "已批准步骤的实际执行结果：" + result });
                executeIndex++;
                executeChildIndex = 0;
                executeSatisfiedCount = 0;
                if (executeIndex == pending.Count)
                {
                    string complete = "已下达 " + pending.Count + " 项操作；施工与工作是否完成请以地图实际状态为准。";
                    pending = null; executeIndex = -1;
                    status = complete; AddMessage("system", complete);
                    try { RequestNext(); }
                    catch (Exception ex) { Fail(ex); }
                }
                else
                {
                    status = "正在调整 " + (executeIndex + 1) + "/" + pending.Count + "：" + pending[executeIndex].preview;
                    nextExecutionTick = Environment.TickCount + 300;
                }
            }
            catch (Exception ex)
            {
                int completed = Math.Max(0, executeIndex);
                AdvisorPreparedStep step = pending[completed];
                AdvisorPreparedStep failed = step.children != null && executeChildIndex < step.children.Count ? step.children[executeChildIndex] : step;
                Recover(ex, "第 " + (completed + 1) + " 项失败；此前 " + completed + " 项、当前项前 " + executeChildIndex + " 个目标已执行，全部保留。"
                    + "失败目标也可能部分生效，请重新查询确认。"
                    + "\n当前方案：" + step.preview + "\n失败参数：" + AdvisorJson.Write(failed.action)
                    + "\n当前项已完成目标：" + (step.children == null ? "无" : string.Join("；", step.children.GetRange(0, executeChildIndex).ConvertAll(s => s.resource))));
            }
        }

        //重新校验同一组固定参数，不接受模型在批准后替换操作。
        private static AdvisorPreparedStep Revalidate(AdvisorPreparedStep approved)
        {
            //只核对批准过的固定目标，不因重新扫描矩形而扩大范围或丢弃已满足的目标。
            if (approved.children != null)
            {
                foreach (AdvisorPreparedStep child in approved.children) Revalidate(child);
                return approved;
            }
            AdvisorPreparedStep current = AdvisorActions.Prepare(approved.action);
            if (current.before != approved.before || current.preview != approved.preview)
                throw new InvalidOperationException("与操作相关的状态已变化，请重新生成方案并批准：" + approved.preview
                    + "\n批准时：" + approved.before + "\n现在：" + current.before);
            return current;
        }

        //取消请求、待审批计划或剩余步骤，不撤销已经成功的修改。
        public void Cancel()
        {
            if (!Busy && !HasPendingPlan) return;
            string progress = executeIndex < 0 ? "" : "已完成 " + executeIndex + " 项；当前第 " + (executeIndex + 1)
                + " 项已执行 " + executeChildIndex + " 个目标。\n" + pending[executeIndex].preview;
            if (progress.Length > 0) AddActivity("执行已停止", progress);
            cancelled = true;
            errorDetail = "";
            cancellation?.Cancel();
            retryPending = false;
            pending = null; executeIndex = -1; queries.Clear(); activeQuery = null; queryResults.Clear();
            status = "已取消；已完成的操作保留，剩余步骤不会执行。";
            AddMessage("system", status, progress);
            wire.Add(new AdvisorEntry { role = "user", text = status + "\n" + progress });
        }

        //清空聊天、查询历史及模型上下文，同时使旧审批窗口失效。
        public void ClearConversation()
        {
            Cancel();
            messages.Clear(); activity.Clear(); wire.Clear(); profile = ""; usage = ""; errorDetail = ""; lastGodMode = null; lastAutoApproval = null;
            ResetRecovery();
            planRevision++;
            status = "对话已清空，下一次请求建立新上下文。";
        }
    }
}
