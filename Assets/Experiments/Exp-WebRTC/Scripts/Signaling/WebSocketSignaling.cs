using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PicoTest.Experiments.WebRTC.Signaling
{
    /// <summary>
    /// 自定义 JSON over WebSocket 信令客户端（System.Net.WebSockets.ClientWebSocket）。
    /// 首版本地测试用（见 Exp-WebRTC/Server/signaling.js）。回调在后台接收线程触发。
    /// 注意：编译不依赖原生库；运行需可达的信令服务器。
    /// </summary>
    public sealed class WebSocketSignaling : ISignaling
    {
        public event Action OnConnected;
        public event Action<SignalingMessage> OnMessage;
        public event Action<string> OnError;
        public event Action OnClosed;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;

        public void Connect(string url)
        {
            Close();
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            _ = RunAsync(url, _cts.Token);
        }

        private async Task RunAsync(string url, CancellationToken ct)
        {
            try
            {
                await _ws.ConnectAsync(new Uri(url), ct);
                OnConnected?.Invoke();
                var buf = new byte[64 * 1024];
                var sb = new StringBuilder();
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    sb.Clear();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                            OnClosed?.Invoke();
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    } while (!r.EndOfMessage);

                    try { var msg = SignalingMessage.Parse(sb.ToString()); if (msg != null) OnMessage?.Invoke(msg); }
                    catch (Exception e) { OnError?.Invoke($"parse: {e.Message}"); }
                }
                OnClosed?.Invoke();
            }
            catch (OperationCanceledException) { OnClosed?.Invoke(); }
            catch (Exception e) { OnError?.Invoke(e.Message); }
        }

        public void Send(SignalingMessage msg)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) { OnError?.Invoke("send: not open"); return; }
            var bytes = Encoding.UTF8.GetBytes(msg.ToJson());
            _ = ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        public void Close()
        {
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Dispose(); } catch { }
            _ws = null;
            _cts = null;
        }
    }
}
