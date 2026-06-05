using System.Threading.Tasks;
using Sirenix.OdinInspector;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 日志工具参数
    /// </summary>
    [HideLabel]
    [HideReferenceObjectPicker]
    public class LogParams : YIUIMCPBaseParams
    {
        /// <summary>
        /// 日志消息
        /// </summary>
        [LabelText("日志消息")]
        public string message;
    }

    /// <summary>
    /// 日志工具
    /// </summary>
    [YIUIMCPTools("Log", "打印日志")]
    public class YIUIMCPTools_Log : YIUIMCPBaseExecutor<LogParams>
    {
        protected override async Task<YIUIMCPResult> Run(LogParams data)
        {
            YIUIMCPLog.Log(data.message);
            await Task.CompletedTask;
            return YIUIMCPResult.Success();
        }
    }

    /// <summary>
    /// 错误日志工具
    /// </summary>
    [YIUIMCPTools("LogError", "打印错误日志")]
    public class YIUIMCPTools_LogError : YIUIMCPBaseExecutor<LogParams>
    {
        protected override async Task<YIUIMCPResult> Run(LogParams data)
        {
            YIUIMCPLog.LogError(data.message);
            await Task.CompletedTask;
            return YIUIMCPResult.Success();
        }
    }
}