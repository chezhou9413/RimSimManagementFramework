namespace SimManagementLib.SimAI.CustomerVisit
{
    //结构职责：保存顾客 Session 单次巡检的结账、恢复、离店或强制退出请求。
    public struct CustomerVisitTickResult
    {
        public bool requestCheckoutMemo;
        public bool requestCheckoutCompletedMemo;
        public bool requestNextShopMemo;
        public bool removeFromLord;
        public bool requestRecovery;
        public bool forceExitNow;
        public string reason;
        public bool HasRequest => requestCheckoutMemo || requestCheckoutCompletedMemo || requestNextShopMemo || removeFromLord || requestRecovery || forceExitNow;
        //创建请求进入结账阶段的结果。
        public static CustomerVisitTickResult Checkout(string reason)
        {
            return new CustomerVisitTickResult
            {
                requestCheckoutMemo = true,
                reason = reason ?? ""
            };
        }
        //创建请求结束访问离店的结果。
        public static CustomerVisitTickResult Leave(string reason)
        {
            return new CustomerVisitTickResult
            {
                requestCheckoutCompletedMemo = true,
                reason = reason ?? ""
            };
        }

        //创建重新下发职责的恢复结果。
        public static CustomerVisitTickResult Recover(string reason)
        {
            return new CustomerVisitTickResult
            {
                requestRecovery = true,
                reason = reason ?? ""
            };
        }

        //请求结束当前访问并交给独立离图职责，职责是保留顾客实物直到其走出地图。
        public static CustomerVisitTickResult ForceExit(string reason)
        {
            return new CustomerVisitTickResult
            {
                forceExitNow = true,
                reason = reason ?? ""
            };
        }
    }
}
