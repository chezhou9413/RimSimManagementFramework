namespace SimManagementLib.SimAI.CustomerVisit
{
    /// <summary>
    /// 保存顾客 Session 单次 Tick 的推进结果，负责让 LordJob 只根据统一结果发送状态机 Memo。
    /// </summary>
    public struct CustomerVisitTickResult
    {
        public bool requestCheckoutMemo;
        public bool requestCheckoutCompletedMemo;
        public bool requestNextShopMemo;
        public bool removeFromLord;
        public string reason;

        /// <summary>
        /// 创建请求进入结账阶段的结果。
        /// </summary>
        public static CustomerVisitTickResult Checkout(string reason)
        {
            return new CustomerVisitTickResult
            {
                requestCheckoutMemo = true,
                reason = reason ?? ""
            };
        }

        /// <summary>
        /// 创建请求结束访问离店的结果。
        /// </summary>
        public static CustomerVisitTickResult Leave(string reason)
        {
            return new CustomerVisitTickResult
            {
                requestCheckoutCompletedMemo = true,
                reason = reason ?? ""
            };
        }
    }
}
