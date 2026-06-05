using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sirenix.OdinInspector;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 控制台参数
    /// </summary>
    [HideLabel]
    [HideReferenceObjectPicker]
    public class ConsoleParams : YIUIMCPBaseParams
    {
        [LabelText("日志类型")]
        public EYIUIConsoleLogType logType = EYIUIConsoleLogType.LogMask;

        /// 日志类型，默认 Log
        [LabelText("最大日志数量")]
        public int logMaxCount = int.MaxValue; // 最大日志数量，默认不限制

        [LabelText("是否移除异常栈信息")]
        public bool removeStackTrace = true; // 是否移除异常栈信息，默认 true
    }

    [YIUIMCPTools("GetConsoleLog", "获取控制台日志")]
    public class YIUIMCPTools_GetConsoleLog : YIUIMCPBaseExecutor<ConsoleParams>
    {
        protected override Task<YIUIMCPResult> Run(ConsoleParams data)
        {
            var sb = new StringBuilder();

            var consoleErrors = YIUIMCPHelper.GetConsoleLogs(data.logType, data.removeStackTrace);

            if (consoleErrors.Count > 0)
            {
                sb.AppendLine($"Result: Detected {consoleErrors.Count} log(s)! The logs may not all be incorrect depending on the parameters passed in. Please judge based on the content of the logs");

                foreach (var err in consoleErrors.Take(data.logMaxCount))
                {
                    sb.AppendLine(err);
                }

                if (consoleErrors.Count > data.logMaxCount)
                {
                    sb.AppendLine($"... {consoleErrors.Count - data.logMaxCount} more log(s) not displayed");
                }

                YIUIMCPLog.Log($"完毕,检测到 {consoleErrors.Count} 个日志!");
                return Task.FromResult(YIUIMCPResult.Success(sb.ToString()));
            }
            else
            {
                sb.AppendLine($"Result: Success, No logs!");
                YIUIMCPLog.Log("完毕,没有日志!");
                return Task.FromResult(YIUIMCPResult.Success(sb.ToString()));
            }
        }
    }
}