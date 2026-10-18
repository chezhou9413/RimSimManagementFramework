using System;
using SimManagementLib.BusinessAdvisor;
using SimManagementLib.Tool;

namespace SimManagementLib.GameComp
{
    //把可修正的工具错误回传给模型；重试计入玩家轮数额度，可随时停止。
    public sealed partial class GameComponent_BusinessAdvisor
    {
        private bool retryPending;
        private int retryAt;
        private int requestStartedAt;
        private string lastFailure = "";
        private int repeatedFailures;
        private bool? lastAutoApproval;

        //清除成功操作之前的失败计数，不把重复查询当成实际修改进展。
        private void ResetRecovery() { lastFailure = ""; repeatedFailures = 0; }

        //反馈实际错误与已完成范围，让模型重新查询和规划，而不是盲目重复操作。
        private void Recover(Exception ex, string context)
        {
            if (cancelled) return;
            if (ex is LlmRequestException network && !network.Recoverable) { Fail(ex); return; }
            repeatedFailures = lastFailure == ex.Message ? repeatedFailures + 1 : 1;
            lastFailure = ex.Message;
            string detail = ex is LlmRequestException error ? error.Detail : ex.Message;
            pending = null; executeIndex = -1; executeChildIndex = 0;
            queries.Clear(); activeQuery = null; queryResults.Clear(); planRevision++;
            errorDetail = detail;
            status = "遇到问题，顾问将调整方案：" + Short(ex.Message.Replace("\n", " · "), 160);
            AddMessage("system", status, context + "\n\n" + detail);
            AddActivity("错误反馈与自动重试", context + "\n\n" + detail);
            wire.Add(new AdvisorEntry { role = "user", text = "执行器错误反馈：\n" + Short(context, 12000) + "\n原因：" + Short(detail, 6000)
                + "\n同一错误连续次数=" + repeatedFailures + "。重复失败说明原方案没有解决问题，必须更改方案；无法解决时向玩家说明实际阻碍，不要继续原样重试。"
                + "\n继续原任务。准备阶段失败则整批未执行；执行阶段以列出的完成范围为准。不要把失败理解为已建成部分成果。"
                + "按具体坐标/编号查小范围，使用反馈提供的真实占位物、允许分类/岗位和容量，不猜测 ID，也不换个物品盲试。"
                + "同一原因重复时先核对工具或源码证据再提交；查询有分页时，未返回的内容不是空。"
                + "施工和上架指派不等于实际完工/入柜；wait_work 矩形观察施工，mapId+shopId 观察店铺补货。"
                + "无法继续时说明证实的阻碍和未完成内容，不要用 blocked 同时承诺接下来执行。新方案仍遵循当前审批设置。" });
            retryPending = true;
            retryAt = Environment.TickCount + (ex is LlmRequestException ? 5000 * Math.Min(repeatedFailures, 6) : 1500 * Math.Min(repeatedFailures, 10));
        }
    }
}
