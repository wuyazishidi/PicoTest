using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 编译状态监控器
    /// </summary>
    [InitializeOnLoad]
    public static class CompileStatusMonitor
    {
        public static bool IsCompiling { get; private set; }
        public static bool LastCompileSucceeded { get; private set; } = true;
        public static DateTime RequestCompileTime { get; private set; }
        public static DateTime CompileStartTime { get; private set; }
        public static DateTime CompileEndTime { get; private set; }
        public static double CompileDuration => (CompileEndTime - CompileStartTime).TotalSeconds;
        public static List<CompilerMessage> CompileErrors { get; private set; } = new();

        static CompileStatusMonitor()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            IsCompiling = EditorApplication.isCompiling;
        }

        public static void UpdateRequestCompileTime()
        {
            RequestCompileTime = DateTime.Now;
            CompileStartTime = RequestCompileTime;
            CompileEndTime = RequestCompileTime;
        }

        private static void OnCompilationStarted(object context)
        {
            IsCompiling = true;
            LastCompileSucceeded = false;
            CompileStartTime = DateTime.Now;
            CompileErrors.Clear();
            YIUIMCPLog.Log("<color=yellow>编译开始...</color>");
        }

        private static void OnCompilationFinished(object context)
        {
            IsCompiling = false;
            CompileEndTime = DateTime.Now;
            LastCompileSucceeded = CompileErrors.Count <= 0;
            if (LastCompileSucceeded)
            {
                YIUIMCPLog.Log($"编译完成，耗时 {CompileDuration:F2} 秒，无错误");
            }
            else
            {
                YIUIMCPLog.Log($"编译完成，耗时 {CompileDuration:F2} 秒，{CompileErrors.Count} 个错误");
            }

            YIUIMCPServerHelper.OnCompilationFinished(context);
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                {
                    CompileErrors.Add(msg);
                }
            }
        }
    }
}