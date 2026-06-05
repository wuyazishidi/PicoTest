using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace YIUIFramework.Editor.MCP
{
    public static class YIUIMCPDispatcher
    {
        /// <summary>
        /// Task 轮询警告阈值（秒）
        /// 超过此时间未完成的 Task 会输出警告
        /// </summary>
        private const int TaskPollingWarningTimeoutSeconds = 40; //目前超时是30 所以这里比超时大一点就行

        private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();
        private static readonly List<TaskPollingInfo> _pollingTasks = new List<TaskPollingInfo>();

        private class TaskPollingInfo
        {
            public Task Task;
            public TaskCompletionSource<object> Tcs;
            public int PollCount;
            public DateTime StartTime; // 记录开始时间
            public bool HasWarned; // 标记是否已经警告过
        }

        public static void Initialize()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            while (_executionQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    YIUIMCPLog.LogError($" 主线程调度程序中的错误: {e}");
                }
            }

            // 轮询未完成的 Task
            PollTasks();
        }

        private static void StartTaskPolling(Task task, TaskCompletionSource<object> tcs)
        {
            lock (_pollingTasks)
            {
                _pollingTasks.Add(new TaskPollingInfo
                {
                    Task = task,
                    Tcs = tcs,
                    PollCount = 0,
                    StartTime = DateTime.Now,
                    HasWarned = false
                });
            }

            #if YIUIMCP_DEBUG
            YIUIMCPLog.Log($"[Polling] 添加 Task 到轮询列表，当前轮询任务数: {_pollingTasks.Count}");
            #endif
        }

        private static void PollTasks()
        {
            lock (_pollingTasks)
            {
                for (int i = _pollingTasks.Count - 1; i >= 0; i--)
                {
                    var info = _pollingTasks[i];
                    info.PollCount++;

                    if (info.Task.IsCompleted)
                    {
                        #if YIUIMCP_DEBUG
                        YIUIMCPLog.Log($"[Polling] Task 完成（轮询 {info.PollCount} 次）");
                        #endif

                        // Task 完成，设置结果
                        try
                        {
                            if (info.Task.IsFaulted)
                            {
                                YIUIMCPLog.LogError($"[Polling] Task 失败: {info.Task.Exception?.Message}");
                                info.Tcs.SetException(info.Task.Exception?.InnerException ?? info.Task.Exception ?? new Exception("Task failed"));
                            }
                            else if (info.Task.IsCanceled)
                            {
                                info.Tcs.SetCanceled();
                            }
                            else
                            {
                                var property = info.Task.GetType().GetProperty("Result");
                                if (property != null)
                                {
                                    info.Tcs.SetResult(property.GetValue(info.Task));
                                }
                                else
                                {
                                    info.Tcs.SetResult(null);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            YIUIMCPLog.LogError($"[Polling] 设置结果时出错: {e.Message}");
                            info.Tcs.SetException(e);
                        }

                        // 从列表中移除
                        _pollingTasks.RemoveAt(i);
                    }
                    else if (!info.HasWarned)
                    {
                        // 计算实际经过的时间
                        var elapsedSeconds = (DateTime.Now - info.StartTime).TotalSeconds;

                        if (elapsedSeconds >= TaskPollingWarningTimeoutSeconds)
                        {
                            // 超过阈值且未警告过，输出警告（只警告一次）
                            info.HasWarned = true;
                            YIUIMCPLog.LogError($"[Polling] Task 长时间未完成（已等待 {elapsedSeconds:F1} 秒，轮询 {info.PollCount} 次），可能存在死锁");
                        }
                    }
                }
            }
        }

        public static Task<object> Dispatch(Func<object> action)
        {
            var tcs = new TaskCompletionSource<object>();

            _executionQueue.Enqueue(() =>
            {
                try
                {
                    var result = action();

                    // 处理 Task 返回值
                    if (result is Task task)
                    {
                        // 如果 Task 已完成（如 Task.FromResult），直接获取结果
                        if (task.IsCompleted)
                        {
                            if (task.IsFaulted)
                            {
                                tcs.SetException(task.Exception?.InnerException ?? task.Exception ?? new Exception("Task failed"));
                            }
                            else if (task.IsCanceled)
                            {
                                tcs.SetCanceled();
                            }
                            else
                            {
                                var property = task.GetType().GetProperty("Result");
                                if (property != null)
                                {
                                    tcs.SetResult(property.GetValue(task));
                                }
                                else
                                {
                                    tcs.SetResult(null);
                                }
                            }
                        }
                        else
                        {
                            // 未完成的 Task，启动轮询检查
                            StartTaskPolling(task, tcs);
                        }
                    }
                    else
                    {
                        tcs.SetResult(result);
                    }
                }
                catch (Exception e)
                {
                    YIUIMCPLog.LogError($"[Dispatch] 异常: {e.Message}\n{e.StackTrace}");
                    tcs.SetException(e);
                }
            });

            return tcs.Task;
        }

        public static Task Dispatch(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            _executionQueue.Enqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
            return tcs.Task;
        }
    }
}