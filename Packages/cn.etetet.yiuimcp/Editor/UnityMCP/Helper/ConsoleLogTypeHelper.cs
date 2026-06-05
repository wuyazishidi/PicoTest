namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 控制台日志类型辅助类
    /// 提供基于位掩码的日志类型判断方法
    /// </summary>
    public static class ConsoleLogTypeHelper
    {
        private const int ErrorMask = (int)EYIUIConsoleLogType.ErrorMask;
        private const int WarningMask = (int)EYIUIConsoleLogType.WarningMask;
        private const int LogMask = (int)EYIUIConsoleLogType.LogMask;

        /// <summary>
        /// 判断 mode 是否为错误类型
        /// </summary>
        public static bool IsError(int mode) => (mode & ErrorMask) != 0;

        /// <summary>
        /// 判断 mode 是否为警告类型
        /// </summary>
        public static bool IsWarning(int mode) => (mode & WarningMask) != 0;

        /// <summary>
        /// 判断 mode 是否为普通日志类型
        /// </summary>
        public static bool IsLog(int mode) => (mode & LogMask) != 0;

        /// <summary>
        /// 获取日志类型（优先级：Error > Warning > Log）
        /// </summary>
        public static EYIUIConsoleLogType GetLogType(int mode)
        {
            if (IsError(mode)) return EYIUIConsoleLogType.ErrorMask;
            if (IsWarning(mode)) return EYIUIConsoleLogType.WarningMask;
            if (IsLog(mode)) return EYIUIConsoleLogType.LogMask;
            return EYIUIConsoleLogType.None;
        }

        /// <summary>
        /// 检查 mode 是否匹配过滤器
        /// </summary>
        public static bool MatchesFilter(int mode, EYIUIConsoleLogType filter)
        {
            if ((filter & EYIUIConsoleLogType.ErrorMask) != 0 && IsError(mode))
                return true;
            if ((filter & EYIUIConsoleLogType.WarningMask) != 0 && IsWarning(mode))
                return true;
            if ((filter & EYIUIConsoleLogType.LogMask) != 0 && IsLog(mode))
                return true;
            return false;
        }
    }
}
