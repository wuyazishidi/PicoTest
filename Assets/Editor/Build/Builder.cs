using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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

        /// <summary>
        /// 编辑器内单场景 Release 构建共享入口（不 Exit、不需 batchmode，与开着的编辑器共存）。
        /// Release（非 Development）：关闭 CheckJNI，避免 PICO 回调 binder 线程的 JNI 检查 abort；
        /// Debug.Log 在 Release 下仍输出，诊断日志不受影响。
        /// </summary>
        private static void BuildSceneInEditor(string scenePath, string apkName)
        {
            try
            {
                if (!File.Exists(scenePath))
                {
                    Debug.LogError($"[Builder] 场景不存在：{scenePath} —— 先跑对应的场景生成菜单。");
                    return;
                }
                EnsureAndroidToolchain();
                Directory.CreateDirectory(OutputDir);
                string apkPath = Path.Combine(OutputDir, apkName);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[Builder] InEditor(Release) {apkName} result={summary.result} size={summary.totalSize} " +
                          $"time={summary.totalTime} errors={summary.totalErrors} output={apkPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Builder] InEditor {apkName} Exception: {e}");
            }
        }

        /// <summary>编辑器内构建 VST Live APK。产物 Builds/PicoTest-VstLive.apk。</summary>
        [MenuItem("PicoTest/Build VST Live (in-editor)")]
        public static void BuildVstLiveTestInEditor()
            => BuildSceneInEditor("Assets/Main/Scenes/FisheyeDomeXRLive.unity", "PicoTest-VstLive.apk");

        /// <summary>编辑器内构建 WebRTC 双目鱼眼穹顶 APK（Exp-WebRTC 场景）。产物 Builds/PicoTest-WebRtc.apk。</summary>
        [MenuItem("PicoTest/Build WebRTC Dome (in-editor)")]
        public static void BuildWebRtcInEditor()
            => BuildSceneInEditor("Assets/Experiments/Exp-WebRTC/Scenes/WebRtcDomeXRLive.unity", "PicoTest-WebRtc.apk");

        /// <summary>
        /// 编辑器内构建多 Tracker IMU 测试 APK（Exp-TrackerIMU 场景）。
        /// APK 名按 ENABLE_BODY_TRACKING（Android define，菜单 PicoTest/Tracker IMU 切）自动区分：
        /// 关（R1/R2）→ PicoTest-TrackerImu.apk；开（R3 对照轮）→ PicoTest-TrackerImu-bt.apk。
        /// </summary>
        [MenuItem("PicoTest/Tracker IMU/Build APK (in-editor)")]
        public static void BuildTrackerImuInEditor()
        {
            Debug.Log($"[Builder] TrackerImu 构建，ENABLE_BODY_TRACKING={(TrackerImuBtDefineOn() ? "开（R3 对照轮）" : "关（R1/R2）")}");
            BuildSceneInEditor("Assets/Experiments/Exp-TrackerIMU/Scenes/TrackerImuTest.unity", TrackerImuApkName());
        }

        /// <summary>
        /// 装机：把最新构建的 APK adb install 到 PICO 并启动。复用 Tools/install-latest-apk.ps1
        /// 默认行为（在 Builds\/Build\/项目根挑修改时间最新的 .apk；含 adb 定位/多设备检查）。
        /// </summary>
        [MenuItem("PicoTest/Install Latest APK + Launch (adb)")]
        public static void InstallLatestApk() => InstallApkViaScript(null);

        private static bool TrackerImuBtDefineOn()
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android, out string[] defines);
            return defines.Contains("ENABLE_BODY_TRACKING");
        }

        private static string TrackerImuApkName()
            => TrackerImuBtDefineOn() ? "PicoTest-TrackerImu-bt.apk" : "PicoTest-TrackerImu.apk";

        /// <summary>同步调用 Tools/install-latest-apk.ps1 -Launch 装机（阻塞编辑器 ~10-30s，输出转控制台）。
        /// apkPath 为 null 时不传 -Path，走脚本默认的"最新 APK"搜索。</summary>
        private static void InstallApkViaScript(string apkPath)
        {
            string pathArg = string.IsNullOrEmpty(apkPath) ? "" : $" -Path \"{apkPath}\"";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-ExecutionPolicy Bypass -File Tools\\install-latest-apk.ps1{pathArg} -Launch",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(180_000))
                    {
                        try { p.Kill(); } catch { }
                        Debug.LogError($"[Builder] 装机超时（180s）\n{stdout}");
                        EditorUtility.DisplayDialog("装机超时", "adb 安装 180 秒未完成，已终止。\n检查设备连接后重试（详情见 Console）。", "好");
                        return;
                    }
                    if (p.ExitCode == 0)
                    {
                        Debug.Log($"[Builder] 装机成功并已启动\n{stdout}");
                        // 脚本输出形如 "Latest APK: Builds\PicoTest-TrackerImu.apk (98.9 MB, built ...)" 和 "INSTALLED: ..."
                        string apkLine = stdout.Split('\n')
                            .Select(l => l.Trim())
                            .FirstOrDefault(l => l.StartsWith("INSTALLED:"));
                        string what = apkLine != null ? apkLine.Substring("INSTALLED:".Length).Trim() : "APK";
                        EditorUtility.DisplayDialog("装机成功", $"已安装并启动：\n{what}", "好");
                    }
                    else
                    {
                        Debug.LogError($"[Builder] 装机失败（exit {p.ExitCode}）\n{stdout}\n{stderr}");
                        EditorUtility.DisplayDialog("装机失败", $"adb 安装失败（exit {p.ExitCode}）。\n详情见 Console。", "好");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Builder] 装机脚本调用异常: {e}");
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
