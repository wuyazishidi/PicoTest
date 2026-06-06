// Assets/Main/Core/Recording/SessionMeta.cs
using System;
using System.Collections.Generic;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Recording
{
    public enum SessionStatus { Recording, Completed, Failed, Recovered }

    public sealed class StreamInfo
    {
        public string Id;
        public StreamType Type;
        public int NominalHz;
        public long FrameCount;
        public long DroppedFrames;
    }

    /// <summary>manifest.json 的对象模型。时间基准约定对齐 YC-Ego（单调 ns + BOOTTIME 锚点，PC 上锚点为 0）。</summary>
    public sealed class SessionMeta
    {
        public string SessionId;
        public string StartedAtUtc;
        public string DeviceInfo;
        public int SchemaVersion;
        public long TimeBaseOriginNs;
        public long BootTimeAnchorNs;
        public SessionStatus Status;
        public List<StreamInfo> Streams = new List<StreamInfo>();
        public List<string> Tags = new List<string>();

        public static SessionMeta CreateNew(string deviceInfo) => new SessionMeta
        {
            SessionId = Guid.NewGuid().ToString(),
            StartedAtUtc = DateTime.UtcNow.ToString("o"),
            DeviceInfo = deviceInfo,
            SchemaVersion = CoreInfo.SchemaVersion,
            Status = SessionStatus.Recording,
        };
    }
}
