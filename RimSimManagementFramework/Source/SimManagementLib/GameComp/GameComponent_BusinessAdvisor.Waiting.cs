using SimManagementLib.BusinessAdvisor;
using Verse;

namespace SimManagementLib.GameComp
{
    //等待原版工作落地时由本地观察区域，不用连续网络请求消耗 token。
    public sealed partial class GameComponent_BusinessAdvisor
    {
        private int workWaitStarted;
        private int nextWorkWaitCheck;

        //每次取出等待查询时建立新的观察周期。
        private void StartWorkWait()
        {
            workWaitStarted = Find.TickManager.TicksGame;
            nextWorkWaitCheck = workWaitStarted;
        }

        //工作清空后继续任务；长时间未完成则把实际剩余工作交回顾问诊断。
        private bool PollWorkWait(out string result)
        {
            result = "";
            int tick = Find.TickManager.TicksGame;
            if (tick < nextWorkWaitCheck) return false;
            nextWorkWaitCheck = tick + 120;
            int remaining = AdvisorQueries.WorkRemaining(activeQuery, out string detail);
            int elapsed = tick - workWaitStarted;
            if (remaining > 0 && elapsed < 5000)
            {
                status = (activeQuery.shopId.HasValue ? "等待店铺补货入柜" : "等待殖民者清理/施工") + " · 剩余 " + remaining + " 项 · 已观察 " + elapsed + "/5000 游戏刻；暂停时等待也暂停，可停止任务。";
                return false;
            }
            result = "waitFinished=" + (remaining == 0) + " elapsedTicks=" + elapsed + "\n" + detail
                + (remaining == 0 ? "\n本次观察目标已满足；仅适用于上述 mode，任务结束前仍需查询对应的实际结果。"
                    : "\n观察周期结束但工作未完成；请检查货源、材料、人员工作、可达性或剩余工期，不要宣称完工。");
            return true;
        }
    }
}
