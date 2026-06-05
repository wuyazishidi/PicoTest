using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// YIUIMCP工具助手
    /// </summary>
    public static class YIUIMCPHelper
    {
        //清空日志
        public static void ClearConsole()
        {
            var assembly = Assembly.GetAssembly(typeof(UnityEditor.Editor));
            if (assembly == null)
            {
                YIUIMCPLog.LogError("无法获取 UnityEditor.Editor 程序集");
                return;
            }

            var type = assembly.GetType("UnityEditor.LogEntries");
            if (type == null)
            {
                YIUIMCPLog.LogError("无法获取 LogEntries 类型");
                return;
            }

            var method = type.GetMethod("Clear");
            if (method == null)
            {
                YIUIMCPLog.LogError("无法获取 Clear 方法");
                return;
            }

            method?.Invoke(null, null);
        }

        /// <summary>
        /// 从 Console 读取现有的编译错误
        /// </summary>
        public static List<string> GetConsoleErrors()
        {
            return GetConsoleLogs(EYIUIConsoleLogType.ErrorMask);
        }

        /// <summary>
        /// 从 Console 读取日志
        /// </summary>
        /// <param name="logTypes">要获取的日志类型（可使用位运算组合多种类型，如 EYIUIConsoleLogType.ErrorMask | EYIUIConsoleLogType.WarningMask）</param>
        /// <param name="removeStackTrace">是否去掉堆栈信息，默认为 true</param>
        /// <returns>日志列表</returns>
        public static List<string> GetConsoleLogs(EYIUIConsoleLogType logTypes, bool removeStackTrace = true)
        {
            var logs = new List<string>();

            try
            {
                Type logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                if (logEntriesType == null)
                {
                    YIUIMCPLog.LogError("无法获取 LogEntries 类型");
                    return logs;
                }

                Type logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                if (logEntryType == null)
                {
                    YIUIMCPLog.LogError("无法获取 LogEntry 类型");
                    return logs;
                }

                BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // 获取方法
                var startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", staticFlags);
                var endGettingEntriesMethod = logEntriesType.GetMethod("EndGettingEntries", staticFlags);
                var getCountMethod = logEntriesType.GetMethod("GetCount", staticFlags);
                var getEntryInternalMethod = logEntriesType.GetMethod("GetEntryInternal", staticFlags);

                if (startGettingEntriesMethod == null || endGettingEntriesMethod == null || getCountMethod == null || getEntryInternalMethod == null)
                {
                    YIUIMCPLog.LogError("无法获取 LogEntries 方法");
                    return logs;
                }

                // 获取字段
                var modeField = logEntryType.GetField("mode", instanceFlags);
                var messageField = logEntryType.GetField("message", instanceFlags);
                var fileField = logEntryType.GetField("file", instanceFlags);
                var lineField = logEntryType.GetField("line", instanceFlags);

                if (modeField == null || messageField == null || fileField == null || lineField == null)
                {
                    YIUIMCPLog.LogError("无法获取 LogEntry 字段");
                    return logs;
                }

                try
                {
                    // 开始获取日志条目
                    startGettingEntriesMethod.Invoke(null, null);

                    int totalEntries = (int)getCountMethod.Invoke(null, null);

                    object logEntryInstance = Activator.CreateInstance(logEntryType);

                    for (int i = 0; i < totalEntries; i++)
                    {
                        getEntryInternalMethod.Invoke(null, new object[]
                        {
                            i,
                            logEntryInstance
                        });

                        int mode = (int)modeField.GetValue(logEntryInstance);
                        string message = (string)messageField.GetValue(logEntryInstance);
                        string file = (string)fileField.GetValue(logEntryInstance);
                        int line = (int)lineField.GetValue(logEntryInstance);

                        if (string.IsNullOrEmpty(message))
                        {
                            continue;
                        }

                        // 使用位掩码过滤日志类型
                        if (!ConsoleLogTypeHelper.MatchesFilter(mode, logTypes))
                        {
                            continue;
                        }

                        // 根据参数决定是否去掉堆栈信息
                        string finalMessage = removeStackTrace ? ExtractFirstLine(message) : message;

                        logs.Add($"[{line}]: [{finalMessage}]");
                    }
                }
                finally
                {
                    // 确保调用 EndGettingEntries
                    try
                    {
                        endGettingEntriesMethod.Invoke(null, null);
                    }
                    catch (Exception e)
                    {
                        YIUIMCPLog.LogError($"调用 EndGettingEntries 失败: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                YIUIMCPLog.LogError($"读取 Console 错误失败: {e.Message}\n{e.StackTrace}");
            }

            return logs;
        }

        /// <summary>
        /// 提取消息的第一行（去掉堆栈信息）
        /// </summary>
        private static string ExtractFirstLine(string fullMessage)
        {
            if (string.IsNullOrEmpty(fullMessage))
                return fullMessage;

            string[] lines = fullMessage.Split(new[]
            {
                '\r',
                '\n'
            }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0] : fullMessage;
        }
    }
}