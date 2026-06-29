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

        /// <summary>
        /// 本机 2022.3.16f1 未装 SDK/NDK/JDK 模块，借用同 LTS 线 2022.3.21f1 的工具链。
        /// 可用环境变量 PICOTEST_ANDROID_TOOLCHAIN 覆盖（指向 AndroidPlayer 目录）。
        /// </summary>
        private static void EnsureAndroidToolchain()
        {
            string root = Environment.GetEnvironmentVariable("PICOTEST_ANDROID_TOOLCHAIN");
            if (string.IsNullOrEmpty(root))
            {
                root = @"D:\Unity\UnityEditor\2022.3.21f1-x86_64\Editor\Data\PlaybackEngines\AndroidPlayer";
            }

            string sdk = Path.Combine(root, "SDK");
            string ndk = Path.Combine(root, "NDK");
            string jdk = Path.Combine(root, "OpenJDK");

            // NDK 可能是嵌套目录（如 NDK\android-ndk-r23b）—— 用 source.properties 探测真根
            if (Directory.Exists(ndk) && !File.Exists(Path.Combine(ndk, "source.properties")))
            {
                foreach (var sub in Directory.GetDirectories(ndk))
                {
                    if (File.Exists(Path.Combine(sub, "source.properties")))
                    {
                        ndk = sub;
                        break;
                    }
                }
            }

            if (Directory.Exists(sdk)) UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = sdk;
            if (Directory.Exists(ndk)) UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndk;
            if (Directory.Exists(jdk)) UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = jdk;

            Debug.Log($"[Builder] Android toolchain: SDK={UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath} " +
                      $"NDK={UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath} " +
                      $"JDK={UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath}");
        }

        /// <summary>
        /// 专用：只构建 VST 实时鱼眼穹顶测试场景到 Builds/PicoTest-VstLive.apk（dev 构建，便于 adb logcat
        /// 看 [VST] 诊断）。不依赖 EditorBuildSettings。需编辑器关闭（batchmode 与 YIUIMCP 互斥）+ PICO 4U 设备。
        /// 用法：Unity.exe -batchmode -quit -projectPath ... -buildTarget Android -executeMethod PicoTest.Editor.Build.Builder.BuildVstLiveTest -logFile build.log
        /// </summary>
        public static void BuildVstLiveTest()
        {
            try
            {
                EnsureAndroidToolchain();
                Directory.CreateDirectory(OutputDir);
                string apkPath = Path.Combine(OutputDir, "PicoTest-VstLive.apk");

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Main/Scenes/FisheyeDomeXRLive.unity" },
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[Builder] VstLive result={summary.result} size={summary.totalSize} time={summary.totalTime} errors={summary.totalErrors} output={apkPath}");
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Builder] VstLive Exception: {e}");
                EditorApplication.Exit(1);
            }
        }

        public static void BuildPico()
        {
            try
            {
                EnsureAndroidToolchain();
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
