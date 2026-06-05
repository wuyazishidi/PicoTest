using UnityEditor;
using UnityEditor.Compilation;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YIUIFramework.Editor.MCP
{
    [InitializeOnLoad]
    public static class YIUIMCPServerHelper
    {
        private static readonly YIUIMCPServer _server = new();
        public static bool IsRunning => _server.IsRunning;
        private static bool _isQuitting = false;

        // 服务器健康检查配置
        private static readonly float CHECK_INTERVAL = 3f; // 每3秒检查一次
        private static readonly float TIMEOUT_THRESHOLD = 5f; // 超过5秒未响应则重启
        private static double _lastCheckTime = 0;
        private static double _lastSuccessTime = 0;
        private static readonly HttpClient _httpClient;

        // 重启延迟状态
        private static bool _isWaitingForRestart = false;
        private static double _restartWaitStartTime = 0;
        private static readonly float RESTART_DELAY = 0.5f; // 重启前等待500ms

        // 异步检查状态
        private static bool _isCheckingHealth = false;
        private static bool _checkingHealth = false;
        private static bool _isForceRestarting = false;

        private const int FORCE_RESTART_MAX_ATTEMPTS = 5;
        private const int FORCE_RESTART_RETRY_DELAY_MS = 300;

        static YIUIMCPServerHelper()
        {
            YIUIMCPServerConfig.Initialize();

            YIUIMCPToolsRegistry.Initialize();

            if (YIUIMCPServerConfig.StartMode == EYIUIMCPStartMode.Close)
            {
                return;
            }

            YIUIMCPDispatcher.Initialize();

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            // 监听域重载
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // 监听编译
            CompilationPipeline.compilationStarted += OnCompilationStarted;

            // 监听 PlayMode 切换
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // 监听 Unity 退出
            EditorApplication.quitting += OnQuitting;

            if (YIUIMCPServerConfig.StartMode == EYIUIMCPStartMode.Auto)
            {
                //YIUIMCPLog.Log("自动启动MCP服务器");
                ResetLastTime();
                _lastSuccessTime = _lastCheckTime;
                _checkingHealth = true;
                EditorApplication.update += OnEditorUpdate;
            }
        }

        private static void ResetLastTime()
        {
            _lastCheckTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// Unity编辑器更新回调 - 用于定时检查服务器健康状态
        /// </summary>
        private static void OnEditorUpdate()
        {
            if (_isQuitting) return;

            if (!_checkingHealth)
            {
                ResetLastTime();
                return;
            }

            if (_isForceRestarting)
            {
                ResetLastTime();
                return;
            }

            if (EditorApplication.isCompiling)
            {
                ResetLastTime();
                return;
            }

            var currentTime = EditorApplication.timeSinceStartup;

            if (_isWaitingForRestart)
            {
                if (currentTime - _restartWaitStartTime >= RESTART_DELAY)
                {
                    _isWaitingForRestart = false;
                    OnStartServer();
                }

                ResetLastTime();
                return;
            }

            if (currentTime - _lastCheckTime < CHECK_INTERVAL)
            {
                return;
            }

            ResetLastTime();

            if (_isCheckingHealth)
            {
                return;
            }

            //YIUIMCPLog.Log($"检查MCP服务器健康状态");
            _isCheckingHealth = true;
            Task.Run(CheckServerHealthAsync);
        }

        /// <summary>
        /// 检查服务器健康状态（异步方式，不阻塞主线程）
        /// 注意：此方法在线程池线程执行（通过 Task.Run 启动）
        /// </summary>
        private static async Task CheckServerHealthAsync()
        {
            try
            {
                // 如果标志显示未运行，检查是否应该自动启动
                if (!IsRunning && !_isForceRestarting)
                {
                    //YIUIMCPLog.LogError($"MCP 服务器未运行，正在启动...");
                    // 调度到主线程执行 Unity API
                    await YIUIMCPDispatcher.Dispatch(OnStartServer);
                    return;
                }

                // 异步HTTP请求，不阻塞主线程（在线程池执行）
                var isHealthy = await PerformHealthCheckAsync();

                // 后续操作需要在主线程执行
                await YIUIMCPDispatcher.Dispatch(() =>
                {
                    if (isHealthy)
                    {
                        _lastSuccessTime = EditorApplication.timeSinceStartup;
                    }
                    else
                    {
                        if (EditorApplication.timeSinceStartup - _lastSuccessTime > TIMEOUT_THRESHOLD)
                        {
                            YIUIMCPLog.Log($"MCP 服务器超过 {TIMEOUT_THRESHOLD} 秒未响应，正在重启...");

                            OnStopServer();

                            _isWaitingForRestart = true;
                            _restartWaitStartTime = EditorApplication.timeSinceStartup;
                        }
                    }
                });
            }
            catch (Exception e)
            {
                await YIUIMCPDispatcher.Dispatch(() => { YIUIMCPLog.LogError($"检查服务器健康状态时出错: {e.Message}"); });
            }
            finally
            {
                await YIUIMCPDispatcher.Dispatch(() =>
                {
                    ResetLastTime();
                    _isCheckingHealth = false;
                });
            }
        }

        /// <summary>
        /// 执行HTTP健康检查（纯异步，不涉及Unity API）
        /// </summary>
        /// <returns>服务器是否健康</returns>
        private static async Task<bool> PerformHealthCheckAsync()
        {
            try
            {
                string url = $"http://127.0.0.1:{YIUIMCPServerConfig.Port}/health";
                var response = await _httpClient.GetAsync(url); // 真正的异步，不阻塞
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // 任何异常都认为服务器不健康
                return false;
            }
        }

        private static void OnQuitting()
        {
            _isQuitting = true;

            EditorApplication.update -= OnEditorUpdate;
            _httpClient?.Dispose();

            OnStopServer();
        }

        private static void OnBeforeAssemblyReload()
        {
            EditorApplication.update -= OnEditorUpdate;
            OnStopServer();
        }

        private static void OnCompilationStarted(object obj)
        {
            OnStopServer();
        }

        public static void OnCompilationFinished(object obj)
        {
            if (!CompileStatusMonitor.LastCompileSucceeded)
            {
                ResetLastTime();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    _checkingHealth = false;
                    OnStopServer();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                case PlayModeStateChange.EnteredPlayMode:
                    _checkingHealth = true;
                    ResetLastTime();
                    break;
            }
        }

        public static void OnStartServer()
        {
            //YIUIMCPLog.LogError($"启动MCP服务器");
            ResetLastTime();
            StartServerInternal();
        }

        public static void OnForceRestartServer()
        {
            ResetLastTime();
            if (_isQuitting) return;

            if (_isForceRestarting)
            {
                YIUIMCPLog.LogError("强制重启正在执行中，请稍后再试");
                return;
            }

            _isForceRestarting = true;
            var restoreHealthChecking = _checkingHealth;
            _checkingHealth = false;
            _isWaitingForRestart = false;

            var port = YIUIMCPServerConfig.Port;
            var currentProcessId = Process.GetCurrentProcess().Id;

            try
            {
                YIUIMCPLog.Log($"开始强制重启 MCP 服务器，端口: {port}");
                _server.ForceStop();
                YIUIMCPPortRecovery.TryForceReleasePort(port, currentProcessId);

                for (var attempt = 1; attempt <= FORCE_RESTART_MAX_ATTEMPTS; attempt++)
                {
                    if (attempt > 1)
                    {
                        Thread.Sleep(FORCE_RESTART_RETRY_DELAY_MS);
                    }

                    if (StartServerInternal())
                    {
                        YIUIMCPLog.Log($"强制重启成功，端口: {port}，尝试次数: {attempt}");
                        return;
                    }

                    if (attempt >= FORCE_RESTART_MAX_ATTEMPTS)
                    {
                        break;
                    }

                    YIUIMCPLog.LogError($"强制重启第 {attempt} 次启动失败，正在继续释放端口后重试...");
                    _server.ForceStop();
                    YIUIMCPPortRecovery.TryForceReleasePort(port, currentProcessId);
                }

                YIUIMCPLog.LogError($"强制重启失败，端口 {port} 仍不可用，请检查是否存在无法结束的系统级占用");
            }
            finally
            {
                _isForceRestarting = false;
                _checkingHealth = restoreHealthChecking;
                ResetLastTime();
            }
        }

        public static void OnStopServer()
        {
            //YIUIMCPLog.LogError($"停止MCP服务器");
            ResetLastTime();
            _server.Stop();
        }

        private static bool StartServerInternal()
        {
            if (_isQuitting)
            {
                return false;
            }

            var started = _server.Start();
            if (started)
            {
                _lastSuccessTime = EditorApplication.timeSinceStartup;
            }

            return started;
        }
    }
}
