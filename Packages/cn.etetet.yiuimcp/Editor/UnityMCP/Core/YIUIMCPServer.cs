using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace YIUIFramework.Editor.MCP
{
    public class YIUIMCPServer
    {
        private HttpListener _listener;
        public bool IsRunning { get; private set; }

        public static string InstanceId { get; private set; }

        public bool Start()
        {
            if (IsRunning)
            {
                return true;
            }

            var port = YIUIMCPServerConfig.Port;
            var prefix = GetPrefix(port);

            try
            {
                CleanupListener(false, false);

                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                IsRunning = true;
                InstanceId = Guid.NewGuid().ToString();

                YIUIMCPLog.Log($"<color=green>启动成功，端口:</color> <color=yellow>{port}</color>");

                // 异步监听，不阻塞主线程
                Task.Run(ListenLoop);
                return true;
            }
            catch (HttpListenerException e)
            {
                CleanupListener(true, true);
                YIUIMCPLog.LogError($"MCP 服务器启动失败，监听地址: {prefix}");
                YIUIMCPLog.LogError($"请检查是否有其他 Unity 实例正在运行，或使用强制重启释放端口");
                YIUIMCPLog.LogError($"错误详情: {e.Message} (ErrorCode: {e.ErrorCode})");
            }
            catch (Exception e)
            {
                CleanupListener(true, true);
                YIUIMCPLog.LogError($"MCP 服务器启动失败，监听地址: {prefix}");
                YIUIMCPLog.LogError($"错误详情: {e.GetType().Name}: {e.Message}");
            }

            return false;
        }

        public void Stop()
        {
            StopInternal(false);
        }

        public void ForceStop()
        {
            StopInternal(true);
        }

        private void StopInternal(bool forceAbort)
        {
            if (!IsRunning && _listener == null) return;

            var wasRunning = IsRunning;
            IsRunning = false;

            CleanupListener(forceAbort, forceAbort);

            #if YIUIMCP_DEBUG
            if (wasRunning)
            {
                YIUIMCPLog.Log($"MCP 服务器已停止");
            }
            #endif
        }

        private static string GetPrefix(int port)
        {
            return $"http://127.0.0.1:{port}/";
        }

        private void CleanupListener(bool logErrors, bool forceAbort)
        {
            var listener = _listener;
            _listener = null;

            if (listener == null)
            {
                return;
            }

            var shouldAbort = forceAbort;

            try
            {
                listener.Stop();
            }
            catch (Exception e)
            {
                shouldAbort = true;
                if (logErrors)
                {
                    YIUIMCPLog.LogError($"停止 MCP 监听器失败: {e.Message}");
                }
            }

            try
            {
                listener.Close();
            }
            catch (Exception e)
            {
                shouldAbort = true;
                if (logErrors)
                {
                    YIUIMCPLog.LogError($"关闭 MCP 监听器失败: {e.Message}");
                }
            }

            if (!shouldAbort)
            {
                return;
            }

            try
            {
                listener.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                if (logErrors)
                {
                    YIUIMCPLog.LogError($"强制终止 MCP 监听器失败: {e.Message}");
                }
            }
        }

        private async Task ListenLoop()
        {
            while (IsRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    ProcessRequest(context); // Fire and forget to not block listener
                }
                catch (HttpListenerException)
                {
                    // Listener stopped or disposed
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Listener disposed
                    break;
                }
                catch (Exception e)
                {
                    YIUIMCPLog.LogError($" 监听循环错误: {e}");
                }
            }
        }

        private async void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // CORS headers
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.Url.AbsolutePath == "/health" && request.HttpMethod == "GET")
                {
                    await HandleHealth(response);
                }
                else if (request.Url.AbsolutePath == "/rpc" && request.HttpMethod == "POST")
                {
                    await HandleRpc(request, response);
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                }
            }
            catch (Exception e)
            {
                YIUIMCPLog.LogError($" Request error: {e}");
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
        }

        private async Task HandleHealth(HttpListenerResponse response)
        {
            var json = "{\"status\":\"ok\", \"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + ", \"serverId\":\"" + InstanceId + "\"}";
            try
            {
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception e)
            {
                YIUIMCPLog.LogError($" 运行状况回写错误1: {e}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch (Exception ex)
                {
                    YIUIMCPLog.LogError($" 运行状况回写错误2: {ex}");
                }
            }
        }

        private async Task HandleRpc(HttpListenerRequest request, HttpListenerResponse response)
        {
            var startTime = DateTime.Now;

            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync();
            }

            string responseJson;

            try
            {
                var root = JObject.Parse(body);
                var method = root["method"]?.ToString();
                var id = root["id"]?.ToString();

                string paramsJson = null;
                if (root["params"] != null)
                {
                    paramsJson = root["params"].ToString(Formatting.None);
                }

                #if YIUIMCP_DEBUG
                YIUIMCPLog.Log($" [RPC] 开始处理: {method}");
                #endif

                var resultObj = await YIUIMCPDispatcher.Dispatch(() => YIUIMCPToolsRegistry.Invoke(method, paramsJson));

                #if YIUIMCP_DEBUG
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                YIUIMCPLog.Log($" [RPC] 处理完成: {method} (耗时: {elapsed:F0}ms)");
                #endif

                var responseDict = new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["result"] = resultObj,
                    ["id"] = id
                };
                responseJson = JsonConvert.SerializeObject(responseDict);
            }
            catch (Exception e)
            {
                var errorDict = new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["error"] = new
                    {
                        code = -32603,
                        message = e.Message
                    },
                    ["id"] = (string)null
                };
                responseJson = JsonConvert.SerializeObject(errorDict);
                YIUIMCPLog.LogError($" RPC Error: {e}");
            }

            try
            {
                var buffer = Encoding.UTF8.GetBytes(responseJson);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();

                var totalElapsed = (DateTime.Now - startTime).TotalMilliseconds;
                if (totalElapsed > 5000)
                {
                    YIUIMCPLog.LogError($" [RPC] 响应耗时过长: {totalElapsed:F0}ms (可能导致客户端超时)");
                }
            }
            catch (Exception e)
            {
                // Domain Reload 时连接会被强制关闭，这是正常的
                if (e is ObjectDisposedException)
                {
                    #if YIUIMCP_DEBUG
                    YIUIMCPLog.Log($" RPC write-back 连接已关闭（Domain Reload）: {e.Message}");
                    #endif
                }
                else
                {
                    YIUIMCPLog.LogError($" RPC回写错误: {e}");
                }

                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch
                {
                }
            }
        }
    }
}
