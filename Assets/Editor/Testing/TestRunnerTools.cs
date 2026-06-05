using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using YIUIFramework.Editor.MCP;

namespace PicoTest.Editor.Testing
{
    /// <summary>
    /// YIUIMCP 扩展：Unity Test Runner 工具。
    ///
    /// 设计说明：PlayMode 测试会触发 Domain Reload，RPC 的返回通道会被杀掉，
    /// 因此采用 TriggerCompile 同款的"触发 + 轮询"两段式：
    ///   1. RunTests   —— 触发测试运行，立即返回 TRIGGERED
    ///   2. GetTestResult —— 读取结果文件（由 [InitializeOnLoad] 持久回调写入，跨 Domain Reload 存活）
    /// 结果文件：Logs/TestResults/latest.json（Logs 已被 gitignore）。
    /// </summary>
    public static class TestResultStore
    {
        public static readonly string ResultPath =
            Path.Combine("Logs", "TestResults", "latest.json");

        public static void Write(object payload)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, JsonConvert.SerializeObject(payload, Formatting.Indented));
        }
    }

    /// <summary>跨 Domain Reload 的持久测试回调：每次域加载都重新注册。</summary>
    [InitializeOnLoad]
    public static class TestResultRecorder
    {
        static TestResultRecorder()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Recorder());
        }

        private class Recorder : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                TestResultStore.Write(new
                {
                    status = "running",
                    startedAt = DateTime.Now.ToString("o"),
                });
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var failures = new List<object>();
                CollectFailures(result, failures);

                TestResultStore.Write(new
                {
                    status = "finished",
                    finishedAt = DateTime.Now.ToString("o"),
                    passed = result.PassCount,
                    failed = result.FailCount,
                    skipped = result.SkipCount,
                    inconclusive = result.InconclusiveCount,
                    durationSeconds = result.Duration,
                    failures,
                });
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }

            private static void CollectFailures(ITestResultAdaptor node, List<object> failures)
            {
                if (!node.HasChildren)
                {
                    if (node.TestStatus == TestStatus.Failed)
                    {
                        failures.Add(new
                        {
                            name = node.FullName,
                            message = Truncate(node.Message, 2000),
                            stackTrace = Truncate(node.StackTrace, 2000),
                        });
                    }
                    return;
                }

                foreach (var child in node.Children)
                {
                    CollectFailures(child, failures);
                }
            }

            private static string Truncate(string s, int max) =>
                string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + "...");
        }
    }

    public class RunTestsParams : YIUIMCPBaseParams
    {
        /// <summary>EditMode 或 PlayMode。</summary>
        public string testMode = "EditMode";

        /// <summary>可选：按测试全名过滤（分号分隔多个）。</summary>
        public string testNames = "";
    }

    [YIUIMCPTools("RunTests", "运行 Unity Test Runner 测试（EditMode/PlayMode），触发后用 GetTestResult 轮询结果")]
    public class YIUIMCPTools_RunTests : YIUIMCPBaseExecutor<RunTestsParams>
    {
        protected override Task<YIUIMCPResult> Run(RunTestsParams data)
        {
            data.delayAfterMs = 0;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return Task.FromResult(YIUIMCPResult.FailureLog(
                    "Cannot run tests: editor is in play mode. Call StopPlayMode first."));
            }

            if (EditorApplication.isCompiling)
            {
                return Task.FromResult(YIUIMCPResult.FailureLog(
                    "Cannot run tests: compilation in progress."));
            }

            var mode = data.testMode != null && data.testMode.Equals("PlayMode", StringComparison.OrdinalIgnoreCase)
                ? TestMode.PlayMode
                : TestMode.EditMode;

            var filter = new Filter { testMode = mode };
            if (!string.IsNullOrWhiteSpace(data.testNames))
            {
                filter.testNames = data.testNames.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            }

            TestResultStore.Write(new
            {
                status = "triggered",
                mode = mode.ToString(),
                triggeredAt = DateTime.Now.ToString("o"),
            });

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(filter));

            return Task.FromResult(YIUIMCPResult.Success($"TRIGGERED ({mode})"));
        }
    }

    [YIUIMCPTools("GetTestResult", "读取最近一次 RunTests 的结果（finished 且 failed==0 才算成功）")]
    public class YIUIMCPTools_GetTestResult : YIUIMCPBaseExecutor<YIUIMCPBaseParams>
    {
        protected override Task<YIUIMCPResult> Run(YIUIMCPBaseParams data)
        {
            data.delayAfterMs = 0;

            if (!File.Exists(TestResultStore.ResultPath))
            {
                return Task.FromResult(YIUIMCPResult.Failure("NO_RESULT: no test run recorded yet."));
            }

            var json = File.ReadAllText(TestResultStore.ResultPath);

            // 结果语义：finished 且 0 失败 => success；running/triggered => failure(让轮询方继续等)
            var isFinished = json.Contains("\"status\": \"finished\"");
            var hasNoFailures = json.Contains("\"failed\": 0");

            return Task.FromResult(isFinished && hasNoFailures
                ? YIUIMCPResult.Success(json)
                : YIUIMCPResult.Failure(json));
        }
    }
}
