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
    /// Android 构建/装机入口（参考 YC-Ego BuildScript 的注册表式组织），两种用法：
    ///   - 编辑器菜单：PicoTest/Build APK/*（每个场景一个独立 APK）、PicoTest/Install Latest APK。
    ///   - Headless（需编辑器关闭，batchmode 与 YIUIMCP 互斥）：
    ///       Unity.exe -batchmode -quit -projectPath ... -buildTarget Android \
    ///                 -executeMethod PicoTest.Editor.Build.Builder.BuildSceneApk -scene xrlive [-outputPath ...]
    /// 约定：一律 Release（BuildOptions.None）——Development 构建在 PICO 回调 binder 线程上
    /// CheckJNI abort（见 journal 2026-07-05）；Debug.Log Release 下仍输出，logcat 诊断不受影响。
    /// </summary>
    public static class Builder
    {
        private const string OutputDir = "Builds";

        // ───────────────────────── 场景注册表 ─────────────────────────
        // 每个可独立打包的场景一条：key（batchmode -scene 参数）→ 场景路径 → APK 名。
        // FisheyeDomeDemo.unity 不注册：纯 PC 扫视 demo（普通相机+直读裸 StreamingAssets），
        // 且依赖 gitignore 的 sbs_frame.png（含可识别人物，宪法 #12），出 APK 无意义。

        public readonly struct SceneApk
        {
            public readonly string Key, ScenePath;
            public readonly Func<string> ApkName;
            public SceneApk(string key, string scenePath, Func<string> apkName)
            { Key = key; ScenePath = scenePath; ApkName = apkName; }
        }

        public static readonly SceneApk[] SceneRegistry =
        {
            new SceneApk("xrlive", "Assets/Main/Scenes/FisheyeDomeXRLive.unity",
                () => "PicoTest-VstLive.apk"),
            new SceneApk("vstpassthrough", "Assets/Experiments/Exp-VstPassthrough/Scenes/VstPassthroughDemo.unity",
                () => "PicoTest-VstPassthrough.apk"),
            new SceneApk("webrtc", "Assets/Experiments/Exp-WebRTC/Scenes/WebRtcDomeXRLive.unity",
                () => "PicoTest-WebRtc.apk"),
            new SceneApk("trackerimu", "Assets/Experiments/Exp-TrackerIMU/Scenes/TrackerImuTest.unity",
                TrackerImuApkName),
            new SceneApk("xrdemo", "Assets/Main/Scenes/FisheyeDomeXRDemo.unity",
                () => "PicoTest-XRDemo.apk"),
        };

        // ───────────────────────── Build 菜单（每场景一项） ─────────────────────────

        [MenuItem("PicoTest/Build APK/XR Live - VST 实时鱼眼穹顶", false, 10)]
        public static void BuildXrLiveApk() => BuildByKey("xrlive");

        [MenuItem("PicoTest/Build APK/VST Passthrough - 透视复现", false, 11)]
        public static void BuildVstPassthroughApk() => BuildByKey("vstpassthrough");

        [MenuItem("PicoTest/Build APK/WebRTC Dome - 远端双目流", false, 12)]
        public static void BuildWebRtcApk() => BuildByKey("webrtc");

        [MenuItem("PicoTest/Build APK/Tracker IMU - 多追踪器", false, 13)]
        public static void BuildTrackerImuApk()
        {
            Debug.Log($"[Builder] TrackerImu 构建，ENABLE_BODY_TRACKING={(TrackerImuBtDefineOn() ? "开（R3 对照轮）" : "关（R1/R2）")}");
            BuildByKey("trackerimu");
        }

        [MenuItem("PicoTest/Build APK/Fisheye XR Demo - 静帧立体", false, 14)]
        public static void BuildXrDemoApk() => BuildByKey("xrdemo");

        // ───────────────────────── 构建核心 ─────────────────────────

        /// <summary>按注册表 key 构建单场景 Release APK（编辑器内共存版：不 Exit）。成功后资源管理器定位。</summary>
        private static void BuildByKey(string key)
        {
            var entry = FindEntry(key);
            if (entry == null) { Debug.LogError($"[Builder] 未注册的场景 key：{key}"); return; }
            string apkPath = DoBuildScene(entry.Value, null);
            if (apkPath != null) EditorUtility.RevealInFinder(Path.GetFullPath(apkPath));
        }

        /// <summary>共享构建体。返回 APK 路径（失败 null）。outputPath 为 null 时用 Builds/{注册表 APK 名}。</summary>
        private static string DoBuildScene(SceneApk entry, string outputPath)
        {
            try
            {
                if (!File.Exists(entry.ScenePath))
                {
                    Debug.LogError($"[Builder] 场景不存在：{entry.ScenePath} —— 先跑对应的场景生成菜单。");
                    return null;
                }
                // xrdemo 依赖 gitignore 的静帧（宪法 #12 不入库）：缺文件构建照样成功但真机黑屏，提前警告
                if (entry.Key == "xrdemo" && !File.Exists("Assets/StreamingAssets/sbs_frame.png"))
                    Debug.LogWarning("[Builder] StreamingAssets/sbs_frame.png 缺失（gitignore 不入库）——" +
                                     "XRDemo 真机将无帧可显。先跑 Tools/extract-fisheye-frame.ps1。");

                EnsureAndroidToolchain();
                Directory.CreateDirectory(OutputDir);
                string apkPath = outputPath ?? Path.Combine(OutputDir, entry.ApkName());

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { entry.ScenePath },
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,   // Release：规避 Development 构建 CheckJNI abort
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[Builder] {entry.Key} result={summary.result} size={summary.totalSize} " +
                          $"time={summary.totalTime} errors={summary.totalErrors} output={apkPath}");
                return summary.result == BuildResult.Succeeded ? apkPath : null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Builder] {entry.Key} 构建异常: {e}");
                return null;
            }
        }

        private static SceneApk? FindEntry(string key)
        {
            foreach (var e in SceneRegistry)
                if (e.Key == key) return e;
            return null;
        }

        // ───────────────────────── batchmode 入口（-executeMethod） ─────────────────────────

        /// <summary>
        /// 通用单场景构建：-scene {key}（见 SceneRegistry），可选 -outputPath 覆盖产物路径。
        /// 退出码：0 成功 / 1 构建失败 / 2 参数错误。需编辑器关闭。
        /// Unity.exe -batchmode -quit -projectPath ... -buildTarget Android
        ///           -executeMethod PicoTest.Editor.Build.Builder.BuildSceneApk -scene vstpassthrough -logFile build.log
        /// </summary>
        public static void BuildSceneApk()
        {
            string key = ParseArg("-scene", null);
            var entry = key != null ? FindEntry(key) : null;
            if (entry == null)
            {
                Debug.LogError($"[Builder] -scene 参数缺失或未注册（收到 \"{key}\"）。可用：" +
                               string.Join(", ", SceneRegistry.Select(e => e.Key)));
                EditorApplication.Exit(2);
                return;
            }
            ClearScriptAssembliesAndRefresh();   // batchmode 不自动刷新，且缓存外部改过的 .cs（YC-Ego §9.6.1）
            string apk = DoBuildScene(entry.Value, ParseArg("-outputPath", null));
            EditorApplication.Exit(apk != null ? 0 : 1);
        }

        /// <summary>（保留兼容）batchmode dev 构建 XR Live 场景——Development+AllowDebugging，便于深度调试。
        /// 注意：Development 构建在 PICO 回调线程有 CheckJNI abort 前科，常规用 BuildSceneApk（Release）。</summary>
        public static void BuildVstLiveTest()
        {
            try
            {
                ClearScriptAssembliesAndRefresh();
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
                Debug.Log($"[Builder] VstLive(dev) result={summary.result} size={summary.totalSize} time={summary.totalTime} errors={summary.totalErrors} output={apkPath}");
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Builder] VstLive Exception: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>batchmode 构建 EditorBuildSettings 勾选的全部场景（整包）。[-development]</summary>
        public static void BuildPico()
        {
            try
            {
                ClearScriptAssembliesAndRefresh();
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

        // ───────────────────────── Install 菜单（adb） ─────────────────────────

        /// <summary>
        /// 装机：把最新构建的 APK adb install 到 PICO 并启动。复用 Tools/install-latest-apk.ps1
        /// 默认行为（在 Builds\/Build\/项目根挑修改时间最新的 .apk；含 adb 定位/多设备检查）。
        /// </summary>
        [MenuItem("PicoTest/Install Latest APK + Launch (adb)")]
        public static void InstallLatestApk() => InstallApkViaScript(null);

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

        // ───────────────────────── 工具 ─────────────────────────

        private static bool TrackerImuBtDefineOn()
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android, out string[] defines);
            return defines.Contains("ENABLE_BODY_TRACKING");
        }

        /// <summary>TrackerImu APK 名按 ENABLE_BODY_TRACKING define 自动区分（防混包）。</summary>
        private static string TrackerImuApkName()
            => TrackerImuBtDefineOn() ? "PicoTest-TrackerImu-bt.apk" : "PicoTest-TrackerImu.apk";

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

        // batchmode（-executeMethod）下 Unity 不自动刷新 AssetDatabase，且外部改过的 .cs 仍会用
        // Library/ScriptAssemblies 里的陈旧缓存（YC-Ego troubleshooting §9.6.1 两种表现均有记录）。
        // 修法照抄 YC-Ego：删缓存 + 强制同步刷新（重建 ~几秒，换来"改的代码一定进 APK"）。
        private static void ClearScriptAssembliesAndRefresh()
        {
            string dir = Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies");
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); Debug.Log("[Builder] cleared Library/ScriptAssemblies"); }
                catch (Exception e) { Debug.LogWarning($"[Builder] clear ScriptAssemblies failed (will fall back on Refresh): {e.Message}"); }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        private static string ParseArg(string name, string def)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return def;
        }
    }
}
