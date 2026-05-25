namespace SimManagementLib.Api
{
    /// <summary>
    /// 表示公开 API 的无返回值执行结果，负责用稳定结构传递成功状态和失败原因。
    /// </summary>
    public class SimApiResult
    {
        public bool success;
        public string failReason = "";

        /// <summary>
        /// 创建成功结果。
        /// </summary>
        public static SimApiResult Success()
        {
            return new SimApiResult { success = true, failReason = "" };
        }

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        public static SimApiResult Fail(string reason)
        {
            return new SimApiResult { success = false, failReason = reason ?? "" };
        }
    }

    /// <summary>
    /// 表示公开 API 的带返回值执行结果，负责避免外部模组依赖异常或空引用判断常规失败。
    /// </summary>
    public class SimApiResult<T> : SimApiResult
    {
        public T value;

        /// <summary>
        /// 创建带返回值的成功结果。
        /// </summary>
        public static SimApiResult<T> Success(T value)
        {
            return new SimApiResult<T> { success = true, value = value, failReason = "" };
        }

        /// <summary>
        /// 创建带返回值的失败结果。
        /// </summary>
        public new static SimApiResult<T> Fail(string reason)
        {
            return new SimApiResult<T> { success = false, value = default(T), failReason = reason ?? "" };
        }
    }
}
