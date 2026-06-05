namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// MCP 原子工具的统一返回结构
    /// 没有特殊情况不允许修改
    /// </summary>
    public struct YIUIMCPResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool success { get; set; }

        /// <summary>
        /// 给 AI 看的信息 (包含成功数据或失败原因)
        /// </summary>
        public string message { get; set; }

        public static YIUIMCPResult Success(string message = "success")
        {
            return new YIUIMCPResult
            {
                success = true,
                message = message
            };
        }

        public static YIUIMCPResult SuccessLog(string message = "success")
        {
            YIUIMCPLog.Log(message);
            return new YIUIMCPResult
            {
                success = true,
                message = message
            };
        }

        public static YIUIMCPResult Failure(string message)
        {
            return new YIUIMCPResult
            {
                success = false,
                message = message
            };
        }

        public static YIUIMCPResult FailureLog(string message)
        {
            YIUIMCPLog.LogError(message);
            return new YIUIMCPResult
            {
                success = false,
                message = message
            };
        }
    }
}