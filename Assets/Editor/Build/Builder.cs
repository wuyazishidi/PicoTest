using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PicoTest.Editor.Build
{
    /// <summary>
    /// batchmode 构建入口。用法：
    /// Unity.exe -batchmode -quit -projectPath ... -buildTarget Android -executeMethod PicoTest.Editor.Build.Builder.BuildPico [-development]
    /// 退出码：0 = 成功，1 = 失败（供 AI 自动化循环判定）。
    /// </summary>
    public static class Builder
    {
        private const string OutputDir = "Builds";

        public static void BuildPico()
        {
            try
            {
                bool development = Environment.GetCommandLineArgs().Contains("-development");

                var scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled)
                    .Select(s => s.path)
                    .ToArray();
                if (scenes.Length == 0)
                {
                    Debug.LogError("[Builder] No enabled scenes in EditorBuildSettings.");
                    EditorApplication.Exit(1);
                    return;
                }

                Directory.CreateDirectory(OutputDir);
                string apkPath = Path.Combine(OutputDir, development ? "PicoTest-dev.apk" : "PicoTest.apk");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    options = development
                        ? BuildOptions.Development | BuildOptions.AllowDebugging
                        : BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                Debug.Log($"[Builder] result={summary.result} size={summary.totalSize} " +
                          $"time={summary.totalTime} errors={summary.totalErrors} output={apkPath}");

                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Builder] Exception: {e}");
                EditorApplication.Exit(1);
            }
        }
    }
}
