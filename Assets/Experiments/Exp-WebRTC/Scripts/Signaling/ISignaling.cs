using System;

namespace PicoTest.Experiments.WebRTC.Signaling
{
    /// <summary>
    /// 信令客户端抽象：与本地/远端信令服务器交换 offer/answer/candidate。
    /// 首版 WebSocket 实现（<see cref="WebSocketSignaling"/>）；可换 HTTP/Ayame。
    /// 回调可能在后台线程触发 —— 订阅者需自行 marshal 到主线程（照 VstCamera 纪律）。
    /// </summary>
    public interface ISignaling
    {
        event Action OnConnected;
        event Action<SignalingMessage> OnMessage;   // 收到 answer/candidate/offer
        event Action<string> OnError;
        event Action OnClosed;

        void Connect(string url);
        void Send(SignalingMessage msg);
        void Close();
    }
}
