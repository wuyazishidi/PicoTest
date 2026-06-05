using System;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 控制台日志类型标志（位标志）
    /// 基于 Unity 内部 LogMessageFlags 的正确定义
    /// </summary>
    [Flags]
    public enum EYIUIConsoleLogType
    {
        None = 0,

        /// <summary>
        /// 所有类型
        /// </summary>
        All = ErrorMask | WarningMask | LogMask,

        /// <summary>
        /// 日志掩码 - 包含所有普通日志类型
        /// </summary>
        LogMask = Log | ScriptingLog,

        // === 组合掩码（用于过滤） ===
        /// <summary>
        /// 错误掩码 - 包含所有错误类型
        /// </summary>
        ErrorMask = Error | Assert | Fatal | AssetImportError |
                ScriptingError | ScriptCompileError |
                ScriptingException | ScriptingAssertion,

        /// <summary>
        /// 警告掩码 - 包含所有警告类型
        /// </summary>
        WarningMask = AssetImportWarning | ScriptingWarning | ScriptCompileWarning,

        // === 错误类型 ===
        Error = 1 << 0, // 1 - kError
        Assert = 1 << 1, // 2 - kAssert
        Fatal = 1 << 4, // 16 - kFatal
        AssetImportError = 1 << 6, // 64 - kAssetImportError
        ScriptingError = 1 << 8, // 256 - kScriptingError
        ScriptCompileError = 1 << 11, // 2048 - kScriptCompileError
        ScriptingException = 1 << 17, // 131072 - kScriptingException
        ScriptingAssertion = 1 << 19, // 524288 - kScriptingAssertion

        // === 警告类型 ===
        AssetImportWarning = 1 << 7, // 128 - kAssetImportWarning
        ScriptingWarning = 1 << 9, // 512 - kScriptingWarning
        ScriptCompileWarning = 1 << 12, // 4096 - kScriptCompileWarning

        // === 日志类型 ===
        Log = 1 << 2, // 4 - kLog
        ScriptingLog = 1 << 10, // 1024 - kScriptingLog
    }
}