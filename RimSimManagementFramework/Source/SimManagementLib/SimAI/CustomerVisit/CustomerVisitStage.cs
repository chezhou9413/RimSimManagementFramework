namespace SimManagementLib.SimAI.CustomerVisit
{
    /// <summary>
    /// 表示顾客访问流程的唯一业务阶段，负责让 JobGiver、JobDriver、Inspect 和 Debug 使用同一套状态。
    /// </summary>
    public enum CustomerVisitStage
    {
        Arriving,
        Browsing,
        SelectingService,
        RunningExternalAction,
        WaitingCheckout,
        Checkout,
        PostCheckout,
        Leaving,
        Ended
    }
}
