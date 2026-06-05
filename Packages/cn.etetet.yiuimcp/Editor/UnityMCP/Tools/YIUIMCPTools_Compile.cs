using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.Compilation;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 编译参数
    /// </summary>
    [HideLabel]
    [HideReferenceObjectPicker]
    public class CompileParams : YIUIMCPBaseParams
    {
        /// <summary>
        /// 是否强制编译，默认 false
        /// </summary>
        [LabelText("强制编译")]
        public bool Force = false;
    }

    [YIUIMCPTools("EnterPlayMode", "进入运行模式")]
    public class YIUIMCPTools_EnterPlayMode : YIUIMCPBaseExecutor<YIUIMCPBaseParams>
    {
        protected override Task<YIUIMCPResult> Run(YIUIMCPBaseParams data)
        {
            data.delayAfterMs = 0; //强制立即执行 不允许传入延迟

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                YIUIMCPLog.Log("检测到运行模式，无需进入...");
                return Task.FromResult(YIUIMCPResult.Success("SKIPPED"));
            }

            EditorApplication.EnterPlaymode();

            //实际收不到这个消息 只会收到 Unknown error 这才是正常的 因为就是会断开连接
            return Task.FromResult(YIUIMCPResult.Success("ENTERING"));
        }
    }

    /// <summary>
    /// 步骤1：检查并退出运行模式
    /// 返回值：
    /// - EXITING: 正在退出，连接可能会重置
    /// - SKIPPED: 无需退出（已经是编辑模式）
    /// </summary>
    [YIUIMCPTools("StopPlayMode", "退出运行模式")]
    public class YIUIMCPTools_StopPlayMode : YIUIMCPBaseExecutor<YIUIMCPBaseParams>
    {
        protected override Task<YIUIMCPResult> Run(YIUIMCPBaseParams data)
        {
            data.delayAfterMs = 0; //强制立即执行 不允许传入延迟

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                YIUIMCPLog.Log("检测到运行模式，正在请求退出...");
                EditorApplication.ExitPlaymode();

                // 必须立即返回，不要 await，否则 Domain Reload 会杀掉 Task
                return Task.FromResult(YIUIMCPResult.Success("EXITING"));
            }

            //实际收不到这个消息 只会收到 Unknown error 这才是正常的 因为就是会断开连接
            return Task.FromResult(YIUIMCPResult.Success("SKIPPED"));
        }
    }

    /// <summary>
    /// 步骤2：触发编译
    /// 必须在编辑模式下调用。如果还在运行模式，会返回错误。
    /// 返回值： - TRIGGERED: 编译已触发，连接可能会重置
    /// </summary>
    [YIUIMCPTools("TriggerCompile", "触发编译")]
    public class YIUIMCPTools_TriggerCompile : YIUIMCPBaseExecutor<CompileParams>
    {
        protected override Task<YIUIMCPResult> Run(CompileParams data)
        {
            data.delayAfterMs = 0; //强制立即执行 不允许传入延迟

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return Task.FromResult(YIUIMCPResult.FailureLog("Unable to compile: Currently still in running mode. Call stop_play_mode first and wait for exit。"));
            }

            if (EditorApplication.isCompiling || CompileStatusMonitor.IsCompiling)
            {
                return Task.FromResult(YIUIMCPResult.FailureLog("Unable to compile: Compilation is already in progress."));
            }

            YIUIMCPHelper.ClearConsole();

            CompileStatusMonitor.UpdateRequestCompileTime();

            if (data.Force)
            {
                YIUIMCPLog.Log($"强制触发编译");
                CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else
            {
                YIUIMCPLog.Log($"触发编译");
                AssetDatabase.Refresh();
            }

            return Task.FromResult(YIUIMCPResult.Success("TRIGGERED"));
        }
    }

    /// <summary>
    /// 步骤3：获取编译结果
    /// 返回详细的编译报告字符串
    /// 就是给编译后用的, 不要拿来检查是否有错误, 如果有需求独立获取控制台日志 应该使用获取控制台日志相关的API
    /// 这个方法依赖 TriggerCompile
    /// </summary>
    [YIUIMCPTools("GetCompileResult", "获取编译结果")]
    public class YIUIMCPTools_GetCompileResult : YIUIMCPBaseExecutor<YIUIMCPBaseParams>
    {
        protected override Task<YIUIMCPResult> Run(YIUIMCPBaseParams data)
        {
            var sb = new StringBuilder();

            var isCompiling = EditorApplication.isCompiling || CompileStatusMonitor.IsCompiling;

            sb.AppendLine($"Status: {(isCompiling ? "Compiling" : "Compilation Complete")}");

            if (isCompiling)
            {
                sb.AppendLine($"Result: Failed");
                sb.AppendLine($"Cannot get compilation result while compiling");
                return Task.FromResult(YIUIMCPResult.Failure(sb.ToString()));
            }

            var consoleErrors = YIUIMCPHelper.GetConsoleErrors();

            if (consoleErrors.Count > 0)
            {
                sb.AppendLine($"Result: Failed, Detected {consoleErrors.Count} error(s)!");

                foreach (var err in consoleErrors.Take(10))
                {
                    sb.AppendLine(err);
                }

                if (consoleErrors.Count > 10)
                {
                    sb.AppendLine($"... {consoleErrors.Count - 10} more error(s) not displayed");
                }

                YIUIMCPLog.Log($"<color=red>编译完毕,检测到 {consoleErrors.Count} 个错误!</color>");
                return Task.FromResult(YIUIMCPResult.Failure(sb.ToString()));
            }
            else
            {
                sb.AppendLine($"Result: Success, No errors!");
                YIUIMCPLog.Log("编译完毕,没有错误!");
                return Task.FromResult(YIUIMCPResult.Success(sb.ToString()));
            }
        }
    }
}