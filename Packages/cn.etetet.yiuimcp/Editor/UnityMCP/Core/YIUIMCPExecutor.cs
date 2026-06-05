using System;
using System.Threading.Tasks;
using UnityEditor;

namespace YIUIFramework.Editor.MCP
{
    public static class YIUIMCPExecutor
    {
        /// <summary>
        /// Unity 主线程安全的延迟方法
        /// </summary>
        private static Task DelayOnMainThread(int milliseconds)
        {
            if (milliseconds <= 0)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            var startTime = DateTime.Now;
            var targetTime = startTime.AddMilliseconds(milliseconds);

            // 使用 EditorApplication.update 来实现延迟
            void CheckDelay()
            {
                if (DateTime.Now >= targetTime)
                {
                    EditorApplication.update -= CheckDelay;
                    tcs.SetResult(true);
                }
            }

            EditorApplication.update += CheckDelay;
            return tcs.Task;
        }

        /// <summary>
        /// 统一执行原子工具
        /// </summary>
        public static async Task<YIUIMCPResult> ExecuteAsync(YIUIMCPBaseParams args, Func<YIUIMCPBaseParams, Task<YIUIMCPResult>> action)
        {
            var timeoutMs = args.timeoutMs;
            var delayBeforeMs = args.delayBeforeMs;
            var delayAfterMs = args.delayAfterMs;

            try
            {
                #if YIUIMCP_DEBUG
                YIUIMCPLog.Log($"[ExecuteAsync] 开始执行，delayBeforeMs={delayBeforeMs}, timeoutMs={timeoutMs}, delayAfterMs={delayAfterMs}");
                #endif
                
                if (delayBeforeMs > 0)
                {
                    await DelayOnMainThread(delayBeforeMs);
                }

                var executionTask = action(args);
                var timeoutTask = DelayOnMainThread(timeoutMs);

                var completedTask = await Task.WhenAny(executionTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    //注意这里并不会取消执行任务,只是返回超时结果
                    //目前也无法强制取消执行任务,可能会有一些资源泄漏
                    return YIUIMCPResult.FailureLog($"执行超时: {timeoutMs}ms");
                }

                var resultResult = await executionTask;

                if (!resultResult.success)
                {
                    return resultResult;
                }

                if (delayAfterMs > 0)
                {
                    await DelayOnMainThread(delayAfterMs);
                }

                return resultResult;
            }
            catch (Exception ex)
            {
                return YIUIMCPResult.FailureLog($"执行方法时发生错误: {ex.Message}");
            }
        }
    }
}