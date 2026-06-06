# M2a 数据管线 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建成可测的采集链路：Mock 源 → SessionRecorder → 分段落盘 → UploadQueue → Node 接收服务，e2e 脚本一键验收。

**Architecture:** 全部业务逻辑在 `Main.Core`（asmdef 禁 UnityEngine，EditMode 秒测）；帧由 `Tick(nowNs)` 显式泵送（确定性测试）；落盘走独立线程+有界队列；上传走 `IHttpTransport` 抽象（测试用内存假服务器，真栈用 HttpClient → Node/Express）。

**Tech Stack:** Unity 2022.3 C# / Newtonsoft.Json（已装，Core 可引用——它是纯 .NET 预编译库）/ Node.js 22 + Express。

**约定（所有任务遵守）：**
- 测试命令：`powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1 -Mode EditMode`（前置：Unity 编辑器开着；改完代码先 `/unity-compile` 确认编译）
- 提交前必须全绿（hook 强制 `.gates/tests-green`）；commit 信息 conventional + `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- 新文件出现后 Unity 需要刷新才会编译：执行编译 flow 即可（它会触发 AssetDatabase.Refresh）
- 时间戳一律 long 纳秒；字节序一律小端（BinaryWriter 默认）

---

### Task 1: Schema 基元（Vec3f/Quatf/StreamType/常量）

**Files:**
- Create: `Assets/Main/Core/Schema/Primitives.cs`
- Test: `Assets/Tests/EditMode/Schema/PrimitivesTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Schema/PrimitivesTests.cs
using NUnit.Framework;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Schema
{
    public class PrimitivesTests
    {
        [Test]
        public void Vec3f_RoundTrip_ViaBytes()
        {
            var v = new Vec3f(1.5f, -2.25f, 3.75f);
            var buf = new byte[Vec3f.Size];
            v.WriteTo(buf, 0);
            var back = Vec3f.ReadFrom(buf, 0);
            Assert.AreEqual(v, back);
        }

        [Test]
        public void Quatf_RoundTrip_ViaBytes()
        {
            var q = new Quatf(0.1f, 0.2f, 0.3f, 0.9f);
            var buf = new byte[Quatf.Size];
            q.WriteTo(buf, 0);
            Assert.AreEqual(q, Quatf.ReadFrom(buf, 0));
        }

        [Test]
        public void Sizes_AreFixed()
        {
            Assert.AreEqual(12, Vec3f.Size);
            Assert.AreEqual(16, Quatf.Size);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**（编译错误 = 失败形态之一）

Run: `powershell -ExecutionPolicy Bypass -Command "& '.\Packages\cn.etetet.yiuimcp\Config\compile-unity-flow.ps1' -Force 0 -NoWait 1"`
Expected: 编译错误 `Vec3f does not exist`

- [ ] **Step 3: 最小实现**

```csharp
// Assets/Main/Core/Schema/Primitives.cs
using System;

namespace PicoTest.Core.Schema
{
    /// <summary>流类型。值入盘，禁止改已有值，只许追加。</summary>
    public enum StreamType { BodyPose = 1, Video = 2, PointCloud = 3 }

    public readonly struct Vec3f : IEquatable<Vec3f>
    {
        public const int Size = 12;
        public readonly float X, Y, Z;
        public Vec3f(float x, float y, float z) { X = x; Y = y; Z = z; }

        public void WriteTo(byte[] buf, int offset)
        {
            BitConverter.GetBytes(X).CopyTo(buf, offset);
            BitConverter.GetBytes(Y).CopyTo(buf, offset + 4);
            BitConverter.GetBytes(Z).CopyTo(buf, offset + 8);
        }

        public static Vec3f ReadFrom(byte[] buf, int offset) =>
            new Vec3f(BitConverter.ToSingle(buf, offset),
                      BitConverter.ToSingle(buf, offset + 4),
                      BitConverter.ToSingle(buf, offset + 8));

        public bool Equals(Vec3f o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object o) => o is Vec3f v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
    }

    public readonly struct Quatf : IEquatable<Quatf>
    {
        public const int Size = 16;
        public readonly float X, Y, Z, W;
        public Quatf(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        public void WriteTo(byte[] buf, int offset)
        {
            BitConverter.GetBytes(X).CopyTo(buf, offset);
            BitConverter.GetBytes(Y).CopyTo(buf, offset + 4);
            BitConverter.GetBytes(Z).CopyTo(buf, offset + 8);
            BitConverter.GetBytes(W).CopyTo(buf, offset + 12);
        }

        public static Quatf ReadFrom(byte[] buf, int offset) =>
            new Quatf(BitConverter.ToSingle(buf, offset),
                      BitConverter.ToSingle(buf, offset + 4),
                      BitConverter.ToSingle(buf, offset + 8),
                      BitConverter.ToSingle(buf, offset + 12));

        public bool Equals(Quatf o) => X == o.X && Y == o.Y && Z == o.Z && W == o.W;
        public override bool Equals(object o) => o is Quatf q && Equals(q);
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
    }
}
```

- [ ] **Step 4: 编译通过 + 测试绿**

Run: 编译 flow → `Tools\run-tests.ps1 -Mode EditMode`
Expected: `EditMode: passed=5`（原有 2 + 新 3）

- [ ] **Step 5: Commit** `feat(core): schema primitives (Vec3f/Quatf/StreamType)`

---

### Task 2: 帧序列化（BodyPoseFrame / VideoFrameMeta）

**Files:**
- Create: `Assets/Main/Core/Schema/Frames.cs`
- Test: `Assets/Tests/EditMode/Schema/FramesTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Schema/FramesTests.cs
using NUnit.Framework;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Schema
{
    public class FramesTests
    {
        [Test]
        public void BodyPoseFrame_RoundTrip()
        {
            var f = new BodyPoseFrame(123456789L);
            for (int i = 0; i < BodyPoseFrame.JointCount; i++)
                f.SetJoint(i, new Vec3f(i, i * 2, i * 3), new Quatf(0, 0, 0, 1));

            var bytes = f.ToBytes();
            Assert.AreEqual(BodyPoseFrame.ByteSize, bytes.Length);

            var back = BodyPoseFrame.FromBytes(bytes);
            Assert.AreEqual(123456789L, back.TimestampNs);
            Assert.AreEqual(new Vec3f(5, 10, 15), back.GetJointPosition(5));
        }

        [Test]
        public void BodyPoseFrame_ByteSize_Is680()
        {
            // 8(ts) + 24 * (12 + 16)
            Assert.AreEqual(680, BodyPoseFrame.ByteSize);
        }

        [Test]
        public void VideoFrameMeta_RoundTrip_WithPayload()
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            var f = new VideoFrameMeta(42L, 7, 640, 480, payload);
            var bytes = f.ToBytes();
            var back = VideoFrameMeta.FromBytes(bytes);
            Assert.AreEqual(42L, back.TimestampNs);
            Assert.AreEqual(7, back.FrameIndex);
            Assert.AreEqual(640, back.Width);
            Assert.AreEqual(4, back.Payload.Length);
            Assert.AreEqual(f.Crc32, back.Crc32);
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 最小实现**

```csharp
// Assets/Main/Core/Schema/Frames.cs
using System;

namespace PicoTest.Core.Schema
{
    /// <summary>24 骨骼点（对齐 PICO Body Tracking 布局）。固定 680 字节。</summary>
    public sealed class BodyPoseFrame
    {
        public const int JointCount = 24;
        public const int ByteSize = 8 + JointCount * (Vec3f.Size + Quatf.Size);

        public long TimestampNs { get; }
        private readonly Vec3f[] _positions = new Vec3f[JointCount];
        private readonly Quatf[] _rotations = new Quatf[JointCount];

        public BodyPoseFrame(long timestampNs) { TimestampNs = timestampNs; }

        public void SetJoint(int index, Vec3f position, Quatf rotation)
        {
            _positions[index] = position;
            _rotations[index] = rotation;
        }

        public Vec3f GetJointPosition(int index) => _positions[index];
        public Quatf GetJointRotation(int index) => _rotations[index];

        public byte[] ToBytes()
        {
            var buf = new byte[ByteSize];
            BitConverter.GetBytes(TimestampNs).CopyTo(buf, 0);
            int o = 8;
            for (int i = 0; i < JointCount; i++)
            {
                _positions[i].WriteTo(buf, o); o += Vec3f.Size;
                _rotations[i].WriteTo(buf, o); o += Quatf.Size;
            }
            return buf;
        }

        public static BodyPoseFrame FromBytes(byte[] buf)
        {
            var f = new BodyPoseFrame(BitConverter.ToInt64(buf, 0));
            int o = 8;
            for (int i = 0; i < JointCount; i++)
            {
                var p = Vec3f.ReadFrom(buf, o); o += Vec3f.Size;
                var r = Quatf.ReadFrom(buf, o); o += Quatf.Size;
                f.SetJoint(i, p, r);
            }
            return f;
        }
    }

    /// <summary>视频帧（M2 为合成载荷；真实视频 M4 走 mp4+index 外部容器）。</summary>
    public sealed class VideoFrameMeta
    {
        public long TimestampNs { get; }
        public long FrameIndex { get; }
        public int Width { get; }
        public int Height { get; }
        public byte[] Payload { get; }
        public uint Crc32 { get; }

        public VideoFrameMeta(long timestampNs, long frameIndex, int width, int height, byte[] payload)
            : this(timestampNs, frameIndex, width, height, payload, Schema.Crc32.Compute(payload)) { }

        private VideoFrameMeta(long ts, long idx, int w, int h, byte[] payload, uint crc)
        {
            TimestampNs = ts; FrameIndex = idx; Width = w; Height = h; Payload = payload; Crc32 = crc;
        }

        public byte[] ToBytes()
        {
            var buf = new byte[8 + 8 + 4 + 4 + 4 + 4 + Payload.Length];
            BitConverter.GetBytes(TimestampNs).CopyTo(buf, 0);
            BitConverter.GetBytes(FrameIndex).CopyTo(buf, 8);
            BitConverter.GetBytes(Width).CopyTo(buf, 16);
            BitConverter.GetBytes(Height).CopyTo(buf, 20);
            BitConverter.GetBytes(Crc32).CopyTo(buf, 24);
            BitConverter.GetBytes(Payload.Length).CopyTo(buf, 28);
            Payload.CopyTo(buf, 32);
            return buf;
        }

        public static VideoFrameMeta FromBytes(byte[] buf)
        {
            long ts = BitConverter.ToInt64(buf, 0);
            long idx = BitConverter.ToInt64(buf, 8);
            int w = BitConverter.ToInt32(buf, 16);
            int h = BitConverter.ToInt32(buf, 20);
            uint crc = BitConverter.ToUInt32(buf, 24);
            int len = BitConverter.ToInt32(buf, 28);
            var payload = new byte[len];
            Array.Copy(buf, 32, payload, 0, len);
            return new VideoFrameMeta(ts, idx, w, h, payload, crc);
        }
    }
}
```

同文件夹新增 CRC32（上传校验也要用）：

```csharp
// Assets/Main/Core/Schema/Crc32.cs
namespace PicoTest.Core.Schema
{
    /// <summary>标准 CRC-32 (IEEE 802.3)。与 Node 端 buffer-crc32 兼容。</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(byte[] data) => Compute(data, 0, data.Length);

        public static uint Compute(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
```

测试追加（同 FramesTests.cs 或新建 Crc32Tests.cs）：

```csharp
// Assets/Tests/EditMode/Schema/Crc32Tests.cs
using NUnit.Framework;
using PicoTest.Core.Schema;
using System.Text;

namespace PicoTest.Tests.EditMode.Schema
{
    public class Crc32Tests
    {
        [Test]
        public void Crc32_KnownVector()
        {
            // CRC32("123456789") = 0xCBF43926（标准测试向量）
            var crc = Crc32.Compute(Encoding.ASCII.GetBytes("123456789"));
            Assert.AreEqual(0xCBF43926u, crc);
        }
    }
}
```

- [ ] **Step 4: 编译 + 测试绿**（passed=9） → **Step 5: Commit** `feat(core): frame serialization + crc32`

---

### Task 3: ChunkWriter / ChunkReader（分段落盘 + 截断容错）

**Files:**
- Create: `Assets/Main/Core/Recording/ChunkWriter.cs`、`Assets/Main/Core/Recording/ChunkReader.cs`
- Test: `Assets/Tests/EditMode/Recording/ChunkTests.cs`

格式：头 = magic `PTCH`(4B) + formatVersion int32 + sessionId GUID(16B) + streamIdLen int32 + streamId UTF8；帧记录 = [int32 长度][payload]。

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Recording/ChunkTests.cs
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PicoTest.Core.Recording;

namespace PicoTest.Tests.EditMode.Recording
{
    public class ChunkTests
    {
        private string _dir;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "ptc_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { Directory.Delete(_dir, true); }

        [Test]
        public void WriteThenRead_RoundTrips()
        {
            var sid = Guid.NewGuid();
            using (var w = new ChunkWriter(_dir, sid, "body_pose", maxChunkBytes: 1024 * 1024))
            {
                w.AppendFrame(new byte[] { 1, 2, 3 });
                w.AppendFrame(new byte[] { 4, 5 });
            }
            var frames = ChunkReader.ReadAllFrames(Path.Combine(_dir, "chunk_0001.ptc")).ToList();
            Assert.AreEqual(2, frames.Count);
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, frames[1]);
        }

        [Test]
        public void RollsToNewChunk_WhenMaxBytesExceeded()
        {
            using (var w = new ChunkWriter(_dir, Guid.NewGuid(), "s", maxChunkBytes: 200))
            {
                for (int i = 0; i < 10; i++) w.AppendFrame(new byte[50]);
            }
            Assert.Greater(Directory.GetFiles(_dir, "chunk_*.ptc").Length, 1);
        }

        [Test]
        public void TruncatedTail_IsToleratedOnRead()
        {
            var path = Path.Combine(_dir, "chunk_0001.ptc");
            using (var w = new ChunkWriter(_dir, Guid.NewGuid(), "s", 1024 * 1024))
            {
                w.AppendFrame(new byte[100]);
                w.AppendFrame(new byte[100]);
            }
            // 模拟崩溃：截掉最后 30 字节
            using (var fs = new FileStream(path, FileMode.Open)) fs.SetLength(fs.Length - 30);
            var frames = ChunkReader.ReadAllFrames(path).ToList();
            Assert.AreEqual(1, frames.Count); // 完整的第一帧保留，残帧丢弃
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
// Assets/Main/Core/Recording/ChunkWriter.cs
using System;
using System.IO;
using System.Text;

namespace PicoTest.Core.Recording
{
    /// <summary>单流分段写入器。非线程安全 —— 由 SessionRecorder 的单写线程独占。</summary>
    public sealed class ChunkWriter : IDisposable
    {
        public const string Magic = "PTCH";
        public const int FormatVersion = 1;

        private readonly string _dir;
        private readonly Guid _sessionId;
        private readonly string _streamId;
        private readonly long _maxChunkBytes;
        private FileStream _fs;
        private int _chunkIndex;

        public int ChunkCount => _chunkIndex;

        public ChunkWriter(string dir, Guid sessionId, string streamId, long maxChunkBytes)
        {
            _dir = dir; _sessionId = sessionId; _streamId = streamId; _maxChunkBytes = maxChunkBytes;
            Directory.CreateDirectory(dir);
            RollChunk();
        }

        public void AppendFrame(byte[] payload)
        {
            if (_fs.Length + 4 + payload.Length > _maxChunkBytes && _fs.Length > HeaderLength())
                RollChunk();
            var len = BitConverter.GetBytes(payload.Length);
            _fs.Write(len, 0, 4);
            _fs.Write(payload, 0, payload.Length);
        }

        /// <summary>每段关闭时 flush 到磁盘（崩溃只丢当前未关段的 OS 缓冲尾部）。</summary>
        private void RollChunk()
        {
            CloseCurrent();
            _chunkIndex++;
            var path = Path.Combine(_dir, $"chunk_{_chunkIndex:D4}.ptc");
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var sidBytes = Encoding.UTF8.GetBytes(_streamId);
            _fs.Write(Encoding.ASCII.GetBytes(Magic), 0, 4);
            _fs.Write(BitConverter.GetBytes(FormatVersion), 0, 4);
            _fs.Write(_sessionId.ToByteArray(), 0, 16);
            _fs.Write(BitConverter.GetBytes(sidBytes.Length), 0, 4);
            _fs.Write(sidBytes, 0, sidBytes.Length);
        }

        private int HeaderLength() => 4 + 4 + 16 + 4 + Encoding.UTF8.GetByteCount(_streamId);

        private void CloseCurrent()
        {
            if (_fs == null) return;
            _fs.Flush(true);
            _fs.Dispose();
            _fs = null;
        }

        public void Dispose() => CloseCurrent();
    }
}
```

```csharp
// Assets/Main/Core/Recording/ChunkReader.cs
using System;
using System.Collections.Generic;
using System.IO;

namespace PicoTest.Core.Recording
{
    public static class ChunkReader
    {
        /// <summary>读一个 chunk 的全部完整帧；尾部残帧（崩溃截断）静默丢弃。</summary>
        public static IEnumerable<byte[]> ReadAllFrames(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                var magic = new string(br.ReadChars(4));
                if (magic != ChunkWriter.Magic) throw new InvalidDataException($"Bad magic in {path}");
                br.ReadInt32();                  // formatVersion
                br.ReadBytes(16);                // sessionId
                int sidLen = br.ReadInt32();
                br.ReadBytes(sidLen);            // streamId

                while (true)
                {
                    if (fs.Length - fs.Position < 4) yield break;
                    int len = br.ReadInt32();
                    if (len < 0 || fs.Length - fs.Position < len) yield break; // 残帧
                    yield return br.ReadBytes(len);
                }
            }
        }
    }
}
```

- [ ] **Step 4: 编译 + 测试绿**（passed=12） → **Step 5: Commit** `feat(core): chunk writer/reader with crash-tolerant tail`

---

### Task 4: SessionMeta / Manifest（含崩溃恢复）

**Files:**
- Create: `Assets/Main/Core/Recording/SessionMeta.cs`、`Assets/Main/Core/Recording/Manifest.cs`
- Test: `Assets/Tests/EditMode/Recording/ManifestTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Recording/ManifestTests.cs
using System;
using System.IO;
using NUnit.Framework;
using PicoTest.Core;
using PicoTest.Core.Recording;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Recording
{
    public class ManifestTests
    {
        private string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "ptm_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { Directory.Delete(_dir, true); }

        private static SessionMeta NewMeta()
        {
            var m = SessionMeta.CreateNew("test-device");
            m.Streams.Add(new StreamInfo { Id = "body_pose", Type = StreamType.BodyPose, NominalHz = 72 });
            return m;
        }

        [Test]
        public void SaveLoad_RoundTrips()
        {
            var m = NewMeta();
            m.Tags.Add("walk");
            Manifest.Save(_dir, m);
            var back = Manifest.Load(_dir);
            Assert.AreEqual(m.SessionId, back.SessionId);
            Assert.AreEqual(CoreInfo.SchemaVersion, back.SchemaVersion);
            Assert.AreEqual("walk", back.Tags[0]);
            Assert.AreEqual(StreamType.BodyPose, back.Streams[0].Type);
        }

        [Test]
        public void Recover_RebuildsFromChunks_AndMarksRecovered()
        {
            var m = NewMeta();
            m.Status = SessionStatus.Recording;   // 崩溃 = 停在 Recording 没终化
            Manifest.Save(_dir, m);
            var streamDir = Path.Combine(_dir, "streams", "body_pose");
            using (var w = new ChunkWriter(streamDir, Guid.Parse(m.SessionId), "body_pose", 1 << 20))
            {
                w.AppendFrame(new byte[10]);
                w.AppendFrame(new byte[10]);
            }

            var recovered = Manifest.RecoverIfNeeded(_dir);

            Assert.IsTrue(recovered);
            var back = Manifest.Load(_dir);
            Assert.AreEqual(SessionStatus.Recovered, back.Status);
            Assert.AreEqual(2, back.Streams[0].FrameCount);
        }

        [Test]
        public void Recover_NoOp_WhenCompleted()
        {
            var m = NewMeta();
            m.Status = SessionStatus.Completed;
            Manifest.Save(_dir, m);
            Assert.IsFalse(Manifest.RecoverIfNeeded(_dir));
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
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
```

```csharp
// Assets/Main/Core/Recording/Manifest.cs
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PicoTest.Core.Recording
{
    public static class Manifest
    {
        public const string FileName = "manifest.json";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
        };

        public static void Save(string sessionDir, SessionMeta meta)
        {
            Directory.CreateDirectory(sessionDir);
            var tmp = Path.Combine(sessionDir, FileName + ".tmp");
            File.WriteAllText(tmp, JsonConvert.SerializeObject(meta, Settings));
            var final = Path.Combine(sessionDir, FileName);
            if (File.Exists(final)) File.Delete(final);
            File.Move(tmp, final); // 原子替换，防写一半的 manifest
        }

        public static SessionMeta Load(string sessionDir) =>
            JsonConvert.DeserializeObject<SessionMeta>(
                File.ReadAllText(Path.Combine(sessionDir, FileName)), Settings);

        /// <summary>启动时调用：Recording 状态 = 上次崩溃 → 从 chunk 文件重建帧数并标记 Recovered。</summary>
        public static bool RecoverIfNeeded(string sessionDir)
        {
            var path = Path.Combine(sessionDir, FileName);
            if (!File.Exists(path)) return false;
            var meta = Load(sessionDir);
            if (meta.Status != SessionStatus.Recording) return false;

            foreach (var stream in meta.Streams)
            {
                var streamDir = Path.Combine(sessionDir, "streams", stream.Id);
                if (!Directory.Exists(streamDir)) continue;
                stream.FrameCount = Directory.GetFiles(streamDir, "chunk_*.ptc")
                    .Sum(f => ChunkReader.ReadAllFrames(f).Count());
            }

            meta.Status = SessionStatus.Recovered;
            Save(sessionDir, meta);
            return true;
        }
    }
}
```

- [ ] **Step 4: 编译 + 测试绿**（passed=15） → **Step 5: Commit** `feat(core): session manifest with crash recovery`

---

### Task 5: SessionRecorder（写线程 + 有界队列 + 丢帧计数）

**Files:**
- Create: `Assets/Main/Core/Recording/SessionRecorder.cs`
- Test: `Assets/Tests/EditMode/Recording/SessionRecorderTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Recording/SessionRecorderTests.cs
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PicoTest.Core.Recording;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Recording
{
    public class SessionRecorderTests
    {
        private string _root;
        [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "ptr_" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        [Test]
        public void RecordsFrames_AndFinalizesManifest()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "body_pose", Type = StreamType.BodyPose, NominalHz = 72 });

            using (var rec = new SessionRecorder(_root, meta))
            {
                rec.Start();
                for (int i = 0; i < 100; i++)
                    rec.Enqueue("body_pose", new byte[680]);
                rec.Stop();
            }

            var dir = Path.Combine(_root, meta.SessionId);
            var back = Manifest.Load(dir);
            Assert.AreEqual(SessionStatus.Completed, back.Status);
            Assert.AreEqual(100, back.Streams[0].FrameCount);
            Assert.AreEqual(0, back.Streams[0].DroppedFrames);

            var total = Directory.GetFiles(Path.Combine(dir, "streams", "body_pose"), "chunk_*.ptc")
                .Sum(f => ChunkReader.ReadAllFrames(f).Count());
            Assert.AreEqual(100, total);
        }

        [Test]
        public void Enqueue_AfterStop_IsRejected()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });
            var rec = new SessionRecorder(_root, meta);
            rec.Start();
            rec.Stop();
            Assert.IsFalse(rec.Enqueue("s", new byte[1]));
            rec.Dispose();
        }

        [Test]
        public void QueueOverflow_CountsDroppedFrames()
        {
            var meta = SessionMeta.CreateNew("test");
            meta.Streams.Add(new StreamInfo { Id = "s", Type = StreamType.BodyPose, NominalHz = 72 });
            // queueCapacity=2 且不启动写线程消费 → 必然溢出
            using (var rec = new SessionRecorder(_root, meta, queueCapacity: 2, startWriters: false))
            {
                rec.Start();
                for (int i = 0; i < 10; i++) rec.Enqueue("s", new byte[1]);
                Assert.GreaterOrEqual(rec.GetDroppedFrames("s"), 8);
            }
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
// Assets/Main/Core/Recording/SessionRecorder.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PicoTest.Core.Recording
{
    /// <summary>
    /// 会话录制器：每流一个有界队列 + 一个写线程（IO 不进调用线程 —— YC-Ego 红线）。
    /// 队列满 → 丢帧并计数（不阻塞采集线程）。Stop() 排空队列、终化 manifest(Completed)。
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        private const long MaxChunkBytes = 64L * 1024 * 1024;

        private sealed class StreamChannel
        {
            public BlockingCollection<byte[]> Queue;
            public Thread Writer;
            public ChunkWriter ChunkWriter;
            public long FrameCount;
            public long Dropped;
        }

        private readonly string _root;
        private readonly SessionMeta _meta;
        private readonly int _queueCapacity;
        private readonly bool _startWriters;
        private readonly Dictionary<string, StreamChannel> _channels = new Dictionary<string, StreamChannel>();
        private volatile bool _running;

        public string SessionDir { get; }
        public SessionMeta Meta => _meta;

        public SessionRecorder(string root, SessionMeta meta, int queueCapacity = 1024, bool startWriters = true)
        {
            _root = root; _meta = meta; _queueCapacity = queueCapacity; _startWriters = startWriters;
            SessionDir = Path.Combine(root, meta.SessionId);
        }

        public void Start()
        {
            Directory.CreateDirectory(SessionDir);
            _meta.Status = SessionStatus.Recording;
            Manifest.Save(SessionDir, _meta);

            foreach (var info in _meta.Streams)
            {
                var streamDir = Path.Combine(SessionDir, "streams", info.Id);
                var ch = new StreamChannel
                {
                    Queue = new BlockingCollection<byte[]>(_queueCapacity),
                    ChunkWriter = new ChunkWriter(streamDir, Guid.Parse(_meta.SessionId), info.Id, MaxChunkBytes),
                };
                if (_startWriters)
                {
                    ch.Writer = new Thread(() => WriterLoop(ch)) { IsBackground = true, Name = $"rec-{info.Id}" };
                    ch.Writer.Start();
                }
                _channels[info.Id] = ch;
            }
            _running = true;
        }

        /// <summary>采集线程调用。队列满或已停止返回 false。</summary>
        public bool Enqueue(string streamId, byte[] frame)
        {
            if (!_running) return false;
            var ch = _channels[streamId];
            if (ch.Queue.TryAdd(frame)) return true;
            Interlocked.Increment(ref ch.Dropped);
            return false;
        }

        public long GetDroppedFrames(string streamId) => Interlocked.Read(ref _channels[streamId].Dropped);
        public long GetFrameCount(string streamId) => Interlocked.Read(ref _channels[streamId].FrameCount);

        private static void WriterLoop(StreamChannel ch)
        {
            foreach (var frame in ch.Queue.GetConsumingEnumerable())
            {
                ch.ChunkWriter.AppendFrame(frame);
                Interlocked.Increment(ref ch.FrameCount);
            }
        }

        /// <summary>停止：拒绝新帧 → 排空队列 → 关 writer → 终化 manifest。</summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;

            foreach (var ch in _channels.Values)
            {
                ch.Queue.CompleteAdding();
                ch.Writer?.Join(10000);
                ch.ChunkWriter.Dispose();
            }

            foreach (var info in _meta.Streams)
            {
                var ch = _channels[info.Id];
                info.FrameCount = Interlocked.Read(ref ch.FrameCount);
                info.DroppedFrames = Interlocked.Read(ref ch.Dropped);
            }
            _meta.Status = SessionStatus.Completed;
            Manifest.Save(SessionDir, _meta);
        }

        public void Dispose()
        {
            if (_running) Stop();
            foreach (var ch in _channels.Values) { ch.Queue.Dispose(); }
        }
    }
}
```

- [ ] **Step 4: 编译 + 测试绿**（passed=18） → **Step 5: Commit** `feat(core): session recorder with writer threads + drop accounting`

---

### Task 6: Mock 采集源（Tick 驱动，确定性）

**Files:**
- Create: `Assets/Main/Core/Capture/ICaptureSource.cs`、`Assets/Main/Core/Capture/MockBodyPoseSource.cs`、`Assets/Main/Core/Capture/MockVideoSource.cs`
- Test: `Assets/Tests/EditMode/Capture/MockSourceTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Capture/MockSourceTests.cs
using System.Collections.Generic;
using NUnit.Framework;
using PicoTest.Core.Capture;
using PicoTest.Core.Schema;

namespace PicoTest.Tests.EditMode.Capture
{
    public class MockSourceTests
    {
        [Test]
        public void BodyPoseSource_Produces72FramesPerSecond()
        {
            var src = new MockBodyPoseSource();
            var frames = new List<byte[]>();
            src.FrameProduced += (ts, bytes) => frames.Add(bytes);
            src.Start();
            // 模拟 1 秒：每 10ms tick 一次
            for (long now = 0; now <= 1_000_000_000L; now += 10_000_000L) src.Tick(now);
            src.Stop();
            Assert.AreEqual(73, frames.Count); // t=0 一帧 + 72 帧
            Assert.AreEqual(BodyPoseFrame.ByteSize, frames[0].Length);
        }

        [Test]
        public void BodyPoseSource_TimestampsMonotonic_AndDeterministic()
        {
            var a = Run(); var b = Run();
            CollectionAssert.AreEqual(a, b); // 同种子同输出
            for (int i = 1; i < a.Count; i++)
            {
                var prev = BodyPoseFrame.FromBytes(a[i - 1]).TimestampNs;
                var cur = BodyPoseFrame.FromBytes(a[i]).TimestampNs;
                Assert.Greater(cur, prev);
            }

            List<byte[]> Run()
            {
                var s = new MockBodyPoseSource(seed: 42);
                var list = new List<byte[]>();
                s.FrameProduced += (ts, bytes) => list.Add(bytes);
                s.Start();
                for (long now = 0; now <= 200_000_000L; now += 5_000_000L) s.Tick(now);
                s.Stop();
                return list;
            }
        }

        [Test]
        public void VideoSource_Produces30FramesPerSecond_WithValidCrc()
        {
            var src = new MockVideoSource();
            var frames = new List<byte[]>();
            src.FrameProduced += (ts, bytes) => frames.Add(bytes);
            src.Start();
            for (long now = 0; now <= 1_000_000_000L; now += 10_000_000L) src.Tick(now);
            src.Stop();
            Assert.AreEqual(31, frames.Count);
            var f = VideoFrameMeta.FromBytes(frames[0]);
            Assert.AreEqual(Crc32.Compute(f.Payload), f.Crc32);
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
// Assets/Main/Core/Capture/ICaptureSource.cs
using System;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Capture
{
    /// <summary>
    /// 采集源。帧由 Tick(nowNs) 显式泵送（薄壳层在 Update 里泵，测试手动泵 —— 确定性）。
    /// M4 真实源（BodyTracking/相机）实现同一接口，链路不变。
    /// </summary>
    public interface ICaptureSource
    {
        string StreamId { get; }
        StreamType Type { get; }
        int NominalHz { get; }
        /// <summary>(timestampNs, 序列化帧字节)。订阅方负责 Enqueue。</summary>
        event Action<long, byte[]> FrameProduced;
        void Start();
        void Tick(long nowNs);
        void Stop();
    }
}
```

```csharp
// Assets/Main/Core/Capture/MockBodyPoseSource.cs
using System;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Capture
{
    /// <summary>合成 24 关节运动（正弦驱动，种子确定）。72Hz。</summary>
    public sealed class MockBodyPoseSource : ICaptureSource
    {
        public string StreamId => "body_pose";
        public StreamType Type => StreamType.BodyPose;
        public int NominalHz => 72;
        public event Action<long, byte[]> FrameProduced;

        private readonly int _seed;
        private long _intervalNs;
        private long _nextDueNs;
        private bool _running;

        public MockBodyPoseSource(int seed = 1) { _seed = seed; }

        public void Start()
        {
            _intervalNs = 1_000_000_000L / NominalHz;
            _nextDueNs = 0;
            _running = true;
        }

        public void Tick(long nowNs)
        {
            if (!_running) return;
            while (nowNs >= _nextDueNs)
            {
                FrameProduced?.Invoke(_nextDueNs, Synthesize(_nextDueNs));
                _nextDueNs += _intervalNs;
            }
        }

        public void Stop() => _running = false;

        private byte[] Synthesize(long tsNs)
        {
            var f = new BodyPoseFrame(tsNs);
            double t = tsNs / 1e9;
            for (int j = 0; j < BodyPoseFrame.JointCount; j++)
            {
                float phase = _seed * 0.37f + j * 0.21f;
                f.SetJoint(j,
                    new Vec3f((float)Math.Sin(t + phase), 1.0f + j * 0.05f, (float)Math.Cos(t + phase)),
                    new Quatf(0, (float)Math.Sin((t + phase) / 2), 0, (float)Math.Cos((t + phase) / 2)));
            }
            return f.ToBytes();
        }
    }
}
```

```csharp
// Assets/Main/Core/Capture/MockVideoSource.cs
using System;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Capture
{
    /// <summary>合成视频帧（小载荷伪图样）。30fps。</summary>
    public sealed class MockVideoSource : ICaptureSource
    {
        public string StreamId => "video";
        public StreamType Type => StreamType.Video;
        public int NominalHz => 30;
        public event Action<long, byte[]> FrameProduced;

        private long _intervalNs;
        private long _nextDueNs;
        private long _frameIndex;
        private bool _running;

        public void Start()
        {
            _intervalNs = 1_000_000_000L / NominalHz;
            _nextDueNs = 0;
            _frameIndex = 0;
            _running = true;
        }

        public void Tick(long nowNs)
        {
            if (!_running) return;
            while (nowNs >= _nextDueNs)
            {
                var payload = new byte[256];
                for (int i = 0; i < payload.Length; i++)
                    payload[i] = (byte)((_frameIndex + i) & 0xFF);
                var frame = new VideoFrameMeta(_nextDueNs, _frameIndex, 640, 480, payload);
                FrameProduced?.Invoke(_nextDueNs, frame.ToBytes());
                _frameIndex++;
                _nextDueNs += _intervalNs;
            }
        }

        public void Stop() => _running = false;
    }
}
```

- [ ] **Step 4: 编译 + 测试绿**（passed=21） → **Step 5: Commit** `feat(core): tick-driven deterministic mock capture sources`

---

### Task 7: CaptureSession 编排器（源↔录制器装配 + 状态快照）

**Files:**
- Create: `Assets/Main/Core/Capture/CaptureSession.cs`、`Assets/Main/Core/Capture/IClock.cs`
- Test: `Assets/Tests/EditMode/Capture/CaptureSessionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Capture/CaptureSessionTests.cs
using System;
using System.IO;
using NUnit.Framework;
using PicoTest.Core.Capture;
using PicoTest.Core.Recording;

namespace PicoTest.Tests.EditMode.Capture
{
    public class CaptureSessionTests
    {
        private string _root;
        [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "pts_" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        [Test]
        public void FullSession_TwoSources_RecordsAndCompletes()
        {
            var session = new CaptureSession(_root, "test-device",
                new ICaptureSource[] { new MockBodyPoseSource(), new MockVideoSource() });

            session.Start();
            for (long now = 0; now <= 500_000_000L; now += 5_000_000L) session.Tick(now);
            var dir = session.SessionDir;
            session.Stop();

            var meta = Manifest.Load(dir);
            Assert.AreEqual(SessionStatus.Completed, meta.Status);
            Assert.AreEqual(2, meta.Streams.Count);
            // 0.5s: body 72Hz → 37 帧（含 t=0），video 30Hz → 16 帧
            Assert.AreEqual(37, meta.Streams.Find(s => s.Id == "body_pose").FrameCount);
            Assert.AreEqual(16, meta.Streams.Find(s => s.Id == "video").FrameCount);
        }

        [Test]
        public void Snapshot_ReflectsRecordingState()
        {
            var session = new CaptureSession(_root, "d", new ICaptureSource[] { new MockBodyPoseSource() });
            Assert.IsFalse(session.GetSnapshot().IsRecording);
            session.Start();
            session.Tick(100_000_000L);
            var snap = session.GetSnapshot();
            Assert.IsTrue(snap.IsRecording);
            Assert.Greater(snap.Streams[0].FrameCount, 0);
            session.Stop();
            Assert.IsFalse(session.GetSnapshot().IsRecording);
        }

        [Test]
        public void AddTag_AppearsInFinalManifest()
        {
            var session = new CaptureSession(_root, "d", new ICaptureSource[] { new MockBodyPoseSource() });
            session.Start();
            session.AddTag("squat");
            var dir = session.SessionDir;
            session.Stop();
            CollectionAssert.Contains(Manifest.Load(dir).Tags, "squat");
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
// Assets/Main/Core/Capture/IClock.cs
using System.Diagnostics;

namespace PicoTest.Core.Capture
{
    public interface IClock { long NowNs(); }

    /// <summary>Stopwatch 单调纳秒时钟（线程安全），对齐 YC-Ego TimeBase 约定。</summary>
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        public long NowNs() => (long)(_sw.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency));
    }
}
```

```csharp
// Assets/Main/Core/Capture/CaptureSession.cs
using System;
using System.Collections.Generic;
using System.Linq;
using PicoTest.Core.Recording;

namespace PicoTest.Core.Capture
{
    public sealed class StreamSnapshot
    {
        public string Id;
        public long FrameCount;
        public long DroppedFrames;
    }

    public sealed class SessionSnapshot
    {
        public bool IsRecording;
        public string SessionId;
        public List<StreamSnapshot> Streams = new List<StreamSnapshot>();
    }

    /// <summary>采集会话编排：装配源→录制器，Tick 泵送，快照供 RemoteBridge/操控台。</summary>
    public sealed class CaptureSession : IDisposable
    {
        private readonly string _root;
        private readonly string _deviceInfo;
        private readonly ICaptureSource[] _sources;
        private SessionRecorder _recorder;
        private bool _recording;

        public string SessionDir => _recorder?.SessionDir;

        public CaptureSession(string root, string deviceInfo, ICaptureSource[] sources)
        {
            _root = root; _deviceInfo = deviceInfo; _sources = sources;
        }

        public void Start()
        {
            if (_recording) return;
            var meta = SessionMeta.CreateNew(_deviceInfo);
            foreach (var s in _sources)
                meta.Streams.Add(new StreamInfo { Id = s.StreamId, Type = s.Type, NominalHz = s.NominalHz });

            _recorder = new SessionRecorder(_root, meta);
            _recorder.Start();
            foreach (var s in _sources)
            {
                var captured = s; // 闭包
                captured.FrameProduced += (ts, bytes) => _recorder.Enqueue(captured.StreamId, bytes);
                captured.Start();
            }
            _recording = true;
        }

        public void Tick(long nowNs)
        {
            if (!_recording) return;
            foreach (var s in _sources) s.Tick(nowNs);
        }

        public void AddTag(string tag)
        {
            if (_recorder != null && !_recorder.Meta.Tags.Contains(tag))
                _recorder.Meta.Tags.Add(tag);
        }

        public void Stop()
        {
            if (!_recording) return;
            foreach (var s in _sources) s.Stop();
            _recorder.Stop();
            _recording = false;
        }

        public SessionSnapshot GetSnapshot()
        {
            var snap = new SessionSnapshot { IsRecording = _recording, SessionId = _recorder?.Meta.SessionId };
            if (_recorder != null && _recording)
                snap.Streams = _recorder.Meta.Streams
                    .Select(i => new StreamSnapshot
                    {
                        Id = i.Id,
                        FrameCount = _recorder.GetFrameCount(i.Id),
                        DroppedFrames = _recorder.GetDroppedFrames(i.Id),
                    }).ToList();
            return snap;
        }

        public void Dispose() { Stop(); _recorder?.Dispose(); }
    }
}
```

注意：`FrameProduced` 事件订阅在 Start 中重复 Start 会重复订阅 —— 已用 `if (_recording) return;` 防重入；每次 Start 新建 recorder，事件指向新 recorder 闭包，旧源事件残留无害（源 Stop 后不再发帧），YAGNI 不做退订。

- [ ] **Step 4: 编译 + 测试绿**（passed=24） → **Step 5: Commit** `feat(core): capture session orchestrator with snapshots`

---

### Task 8: UploadQueue（契约客户端 + 续传/重试，对内存假服务器测试）

**Files:**
- Create: `Assets/Main/Core/Transport/IHttpTransport.cs`、`Assets/Main/Core/Transport/UploadQueue.cs`
- Test: `Assets/Tests/EditMode/Transport/UploadQueueTests.cs`（含 `FakeIngestServer`）

- [ ] **Step 1: 写失败测试**

```csharp
// Assets/Tests/EditMode/Transport/UploadQueueTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using PicoTest.Core.Capture;
using PicoTest.Core.Recording;
using PicoTest.Core.Transport;

namespace PicoTest.Tests.EditMode.Transport
{
    /// <summary>内存假服务器：实现 openapi.yaml 同款语义（注册/HEAD offset/PUT 分块/complete 校验）。</summary>
    public class FakeIngestServer : IHttpTransport
    {
        public readonly Dictionary<string, MemoryStream> Files = new Dictionary<string, MemoryStream>();
        public readonly HashSet<string> Sessions = new HashSet<string>();
        public readonly HashSet<string> Completed = new HashSet<string>();
        public int FailNextNRequests;   // 注入故障

        public HttpResult Send(string method, string path, byte[] body)
        {
            if (FailNextNRequests > 0) { FailNextNRequests--; return new HttpResult(503, null); }

            if (method == "POST" && path == "/api/v1/sessions")
            {
                var id = JObject.Parse(Encoding.UTF8.GetString(body))["SessionId"].ToString();
                Sessions.Add(id);
                return new HttpResult(201, null);
            }
            if (method == "HEAD" && path.Contains("/files/"))
            {
                var key = path;
                long len = Files.TryGetValue(key, out var ms) ? ms.Length : 0;
                return new HttpResult(200, Encoding.UTF8.GetBytes(len.ToString()));
            }
            if (method == "PUT" && path.Contains("/files/"))
            {
                var q = path.Split('?');
                long offset = long.Parse(q[1].Replace("offset=", ""));
                var key = q[0];
                if (!Files.ContainsKey(key)) Files[key] = new MemoryStream();
                var ms2 = Files[key];
                if (ms2.Length != offset) return new HttpResult(409, null); // 乱序保护
                ms2.Seek(offset, SeekOrigin.Begin);
                ms2.Write(body, 0, body.Length);
                return new HttpResult(200, null);
            }
            if (method == "POST" && path.EndsWith("/complete"))
            {
                var sid = path.Split('/')[4];
                Completed.Add(sid);
                return new HttpResult(200, null);
            }
            return new HttpResult(404, null);
        }
    }

    public class UploadQueueTests
    {
        private string _root;
        [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "ptu_" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private string MakeCompletedSession()
        {
            var session = new CaptureSession(_root, "d", new Core.Capture.ICaptureSource[] { new MockBodyPoseSource() });
            session.Start();
            for (long now = 0; now <= 100_000_000L; now += 5_000_000L) session.Tick(now);
            var dir = session.SessionDir;
            session.Stop();
            return dir;
        }

        [Test]
        public void Upload_TransfersAllFiles_AndCompletes()
        {
            var dir = MakeCompletedSession();
            var server = new FakeIngestServer();
            var q = new UploadQueue(server, partBytes: 1024);

            var ok = q.UploadSession(dir);

            Assert.IsTrue(ok);
            var sid = Manifest.Load(dir).SessionId;
            Assert.Contains(sid, new List<string>(server.Completed));
            // 服务端收到的 chunk 与本地逐字节一致
            var localChunk = Directory.GetFiles(Path.Combine(dir, "streams", "body_pose"), "chunk_*.ptc")[0];
            var remoteKey = $"/api/v1/sessions/{sid}/files/streams%2Fbody_pose%2F{Path.GetFileName(localChunk)}";
            CollectionAssert.AreEqual(File.ReadAllBytes(localChunk), server.Files[remoteKey].ToArray());
        }

        [Test]
        public void Upload_ResumesFromServerOffset_AfterTransientFailures()
        {
            var dir = MakeCompletedSession();
            var server = new FakeIngestServer { FailNextNRequests = 3 };
            var q = new UploadQueue(server, partBytes: 64, maxRetries: 10, retryDelayMs: 0);

            Assert.IsTrue(q.UploadSession(dir));
            Assert.AreEqual(1, server.Completed.Count);
        }

        [Test]
        public void Upload_Fails_WhenRetriesExhausted()
        {
            var dir = MakeCompletedSession();
            var server = new FakeIngestServer { FailNextNRequests = 1000 };
            var q = new UploadQueue(server, partBytes: 64, maxRetries: 2, retryDelayMs: 0);
            Assert.IsFalse(q.UploadSession(dir));
        }
    }
}
```

- [ ] **Step 2: 编译失败确认** → **Step 3: 实现**

```csharp
// Assets/Main/Core/Transport/IHttpTransport.cs
namespace PicoTest.Core.Transport
{
    public sealed class HttpResult
    {
        public int Status;
        public byte[] Body;
        public HttpResult(int status, byte[] body) { Status = status; Body = body; }
        public bool Ok => Status >= 200 && Status < 300;
    }

    /// <summary>同步 HTTP 抽象。测试给内存假服务器，真栈给 HttpClientTransport（Task 10）。</summary>
    public interface IHttpTransport
    {
        HttpResult Send(string method, string path, byte[] body);
    }
}
```

```csharp
// Assets/Main/Core/Transport/UploadQueue.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using PicoTest.Core.Recording;
using PicoTest.Core.Schema;

namespace PicoTest.Core.Transport
{
    /// <summary>
    /// 会话上传：注册 manifest → 逐文件（HEAD 查 offset 续传 + PUT 分块）→ complete 校验和。
    /// 同步阻塞设计 —— 调用方放后台线程（采集期间不上传是上层纪律）。
    /// </summary>
    public sealed class UploadQueue
    {
        private readonly IHttpTransport _http;
        private readonly int _partBytes;
        private readonly int _maxRetries;
        private readonly int _retryDelayMs;

        public UploadQueue(IHttpTransport http, int partBytes = 1 << 20, int maxRetries = 5, int retryDelayMs = 1000)
        {
            _http = http; _partBytes = partBytes; _maxRetries = maxRetries; _retryDelayMs = retryDelayMs;
        }

        public bool UploadSession(string sessionDir)
        {
            var meta = Manifest.Load(sessionDir);
            var manifestJson = File.ReadAllBytes(Path.Combine(sessionDir, Manifest.FileName));

            if (!SendWithRetry("POST", "/api/v1/sessions", manifestJson)) return false;

            var checksums = new Dictionary<string, uint>();
            foreach (var file in EnumerateSessionFiles(sessionDir))
            {
                var rel = file.Substring(sessionDir.Length + 1).Replace('\\', '/');
                var key = Uri.EscapeDataString(rel);
                var data = File.ReadAllBytes(file);
                checksums[rel] = Crc32.Compute(data);

                if (!UploadFileResumable(meta.SessionId, key, data)) return false;
            }

            var completeBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { checksums }));
            return SendWithRetry("POST", $"/api/v1/sessions/{meta.SessionId}/complete", completeBody);
        }

        private static IEnumerable<string> EnumerateSessionFiles(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                if (Path.GetFileName(f) != Manifest.FileName) // manifest 经 sessions 注册传输
                    yield return f;
        }

        private bool UploadFileResumable(string sessionId, string fileKey, byte[] data)
        {
            var basePath = $"/api/v1/sessions/{sessionId}/files/{fileKey}";

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                var head = _http.Send("HEAD", basePath, null);
                if (!head.Ok) { Backoff(attempt); continue; }
                long offset = long.Parse(Encoding.UTF8.GetString(head.Body));

                bool failed = false;
                while (offset < data.Length)
                {
                    int len = (int)Math.Min(_partBytes, data.Length - offset);
                    var part = new byte[len];
                    Array.Copy(data, offset, part, 0, len);
                    var resp = _http.Send("PUT", $"{basePath}?offset={offset}", part);
                    if (!resp.Ok) { failed = true; break; }
                    offset += len;
                }
                if (!failed) return true;
                Backoff(attempt);
            }
            return false;
        }

        private bool SendWithRetry(string method, string path, byte[] body)
        {
            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                if (_http.Send(method, path, body).Ok) return true;
                Backoff(attempt);
            }
            return false;
        }

        private void Backoff(int attempt)
        {
            if (_retryDelayMs > 0)
                Thread.Sleep(Math.Min(_retryDelayMs * (1 << Math.Min(attempt, 5)), 30000));
        }
    }
}
```

- [ ] **Step 4: 编译 + 测试绿**（passed=27） → **Step 5: Commit** `feat(core): upload queue with resume + retry against fake ingest server`

---

### Task 9: Node 接收服务 + OpenAPI 契约

**Files:**
- Create: `Server/package.json`、`Server/server.js`、`Server/openapi.yaml`、`Server/.gitignore`
- Test: `Server/server.test.js`（node 内置 test runner）

- [ ] **Step 1: 写契约 + 失败测试**

```yaml
# Server/openapi.yaml
openapi: "3.0.3"
info: { title: PicoTest Ingest API, version: "1.0.0" }
paths:
  /health:
    get: { responses: { "200": { description: ok } } }
  /api/v1/sessions:
    post:
      summary: 注册会话（body = manifest.json 原文）
      responses: { "201": { description: created } }
  /api/v1/sessions/{id}/files/{fileKey}:
    head:
      summary: 查询已接收字节数（断点续传）。响应 body 为十进制数字
      responses: { "200": { description: offset } }
    put:
      summary: 追加分块。query offset 必须等于服务端当前长度，否则 409
      parameters: [ { name: offset, in: query, required: true, schema: { type: integer } } ]
      responses: { "200": { description: appended }, "409": { description: offset mismatch } }
  /api/v1/sessions/{id}/complete:
    post:
      summary: 客户端送 { checksums: { relPath: crc32 } }，服务端校验后落定
      responses: { "200": { description: verified }, "422": { description: checksum mismatch } }
```

```javascript
// Server/server.test.js  — 运行：node --test（Node 22 内置）
const test = require('node:test');
const assert = require('node:assert');
const { createApp, crc32 } = require('./server.js');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

function listen(app) {
  return new Promise(res => { const s = app.listen(0, () => res(s)); });
}
function req(port, method, p, body) {
  return new Promise((resolve, reject) => {
    const r = http.request({ port, method, path: p }, resp => {
      const chunks = [];
      resp.on('data', c => chunks.push(c));
      resp.on('end', () => resolve({ status: resp.statusCode, body: Buffer.concat(chunks) }));
    });
    r.on('error', reject);
    if (body) r.write(body);
    r.end();
  });
}

test('full upload flow: register -> head -> put -> complete', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ingest-'));
  const server = await listen(createApp(dataDir));
  const port = server.address().port;

  const manifest = JSON.stringify({ SessionId: 'abc-123' });
  assert.strictEqual((await req(port, 'POST', '/api/v1/sessions', manifest)).status, 201);

  const fileKey = encodeURIComponent('streams/body_pose/chunk_0001.ptc');
  const base = `/api/v1/sessions/abc-123/files/${fileKey}`;

  let head = await req(port, 'HEAD', base);
  assert.strictEqual(head.status, 200);

  const part1 = Buffer.from([1, 2, 3]);
  assert.strictEqual((await req(port, 'PUT', `${base}?offset=0`, part1)).status, 200);
  // 乱序拒绝
  assert.strictEqual((await req(port, 'PUT', `${base}?offset=99`, part1)).status, 409);
  const part2 = Buffer.from([4, 5]);
  assert.strictEqual((await req(port, 'PUT', `${base}?offset=3`, part2)).status, 200);

  const full = Buffer.from([1, 2, 3, 4, 5]);
  const good = JSON.stringify({ checksums: { 'streams/body_pose/chunk_0001.ptc': crc32(full) } });
  assert.strictEqual((await req(port, 'POST', '/api/v1/sessions/abc-123/complete', good)).status, 200);

  server.close();
});

test('complete rejects bad checksum with 422', async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ingest-'));
  const server = await listen(createApp(dataDir));
  const port = server.address().port;
  await req(port, 'POST', '/api/v1/sessions', JSON.stringify({ SessionId: 's2' }));
  const fk = encodeURIComponent('a.bin');
  await req(port, 'PUT', `/api/v1/sessions/s2/files/${fk}?offset=0`, Buffer.from([9]));
  const bad = JSON.stringify({ checksums: { 'a.bin': 12345 } });
  assert.strictEqual((await req(port, 'POST', '/api/v1/sessions/s2/complete', bad)).status, 422);
  server.close();
});
```

- [ ] **Step 2: 跑测试确认失败**

Run: `Set-Location Server; node --test`
Expected: FAIL（server.js 不存在）

- [ ] **Step 3: 实现服务**

```json
// Server/package.json
{
  "name": "picotest-ingest",
  "version": "1.0.0",
  "private": true,
  "scripts": { "start": "node server.js", "test": "node --test" },
  "dependencies": { "express": "^4.18.2" }
}
```

```
# Server/.gitignore
node_modules/
data/
```

```javascript
// Server/server.js — PicoTest 数据接收服务（契约见 openapi.yaml）
const express = require('express');
const fs = require('node:fs');
const path = require('node:path');

// 与 C# PicoTest.Core.Schema.Crc32 相同的 IEEE CRC-32
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let c = i;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
    t[i] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let crc = 0xFFFFFFFF;
  for (const b of buf) crc = CRC_TABLE[(crc ^ b) & 0xFF] ^ (crc >>> 8);
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

function createApp(dataDir) {
  const app = express();
  const sessions = new Set();

  const sessionDir = id => path.join(dataDir, id);
  const fileOf = (id, fileKey) => {
    const rel = decodeURIComponent(fileKey);
    if (rel.includes('..')) throw new Error('path traversal');
    return path.join(sessionDir(id), rel);
  };

  app.get('/health', (_, res) => res.json({ status: 'ok' }));

  app.post('/api/v1/sessions', express.raw({ type: '*/*', limit: '10mb' }), (req, res) => {
    const manifest = JSON.parse(req.body.toString('utf8'));
    const id = manifest.SessionId;
    if (!id) return res.status(400).json({ error: 'SessionId missing' });
    fs.mkdirSync(sessionDir(id), { recursive: true });
    fs.writeFileSync(path.join(sessionDir(id), 'manifest.json'), req.body);
    sessions.add(id);
    res.status(201).json({ id });
  });

  app.head('/api/v1/sessions/:id/files/:fileKey', (req, res) => {
    const f = fileOf(req.params.id, req.params.fileKey);
    const len = fs.existsSync(f) ? fs.statSync(f).size : 0;
    // HEAD 无 body —— 长度经 Content-Length 头传递（C# 端读这个头）
    res.set('Content-Length', String(len)).status(200).end();
  });

  app.put('/api/v1/sessions/:id/files/:fileKey',
    express.raw({ type: '*/*', limit: '64mb' }), (req, res) => {
      const f = fileOf(req.params.id, req.params.fileKey);
      const offset = Number(req.query.offset);
      fs.mkdirSync(path.dirname(f), { recursive: true });
      const current = fs.existsSync(f) ? fs.statSync(f).size : 0;
      if (current !== offset) return res.status(409).json({ expected: current });
      fs.appendFileSync(f, req.body);
      res.status(200).json({ received: req.body.length });
    });

  app.post('/api/v1/sessions/:id/complete',
    express.raw({ type: '*/*', limit: '10mb' }), (req, res) => {
      const { checksums } = JSON.parse(req.body.toString('utf8'));
      for (const [rel, expected] of Object.entries(checksums)) {
        const f = path.join(sessionDir(req.params.id), rel);
        if (!fs.existsSync(f)) return res.status(422).json({ missing: rel });
        const actual = crc32(fs.readFileSync(f));
        if (actual !== Number(expected) >>> 0)
          return res.status(422).json({ mismatch: rel, expected, actual });
      }
      fs.writeFileSync(path.join(sessionDir(req.params.id), '.complete'), new Date().toISOString());
      res.status(200).json({ verified: Object.keys(checksums).length });
    });

  return app;
}

if (require.main === module) {
  const dataDir = process.env.INGEST_DATA_DIR || path.join(__dirname, 'data');
  const port = Number(process.env.INGEST_PORT || 8077);
  fs.mkdirSync(dataDir, { recursive: true });
  createApp(dataDir).listen(port, () => console.log(`[ingest] listening :${port}, data -> ${dataDir}`));
}

module.exports = { createApp, crc32 };
```

- [ ] **Step 4: 安装依赖 + 测试绿**

Run: `Set-Location Server; npm install; node --test`
Expected: `pass 2`

注意：HEAD 的 offset 经 **Content-Length 响应头**传递（Express 对 HEAD 不发 body）。**Task 8 的 FakeIngestServer 语义要对齐**：C# 真实传输层（Task 10）读 Content-Length 头；Fake 直接用 body 传数字属于测试内部约定，无碍（接口层面都是 `HttpResult.Body` 携带数字字符串，Task 10 负责把头转成 Body）。

- [ ] **Step 5: Commit** `feat(server): node ingest service with resumable upload + crc32 verification`

---

### Task 10: HttpClientTransport（真实 HTTP 实现）

**Files:**
- Create: `Assets/Main/Core/Transport/HttpClientTransport.cs`
- Test: 无独立单测（逻辑零分支；由 Task 11 e2e 覆盖真栈）

- [ ] **Step 1: 实现**

```csharp
// Assets/Main/Core/Transport/HttpClientTransport.cs
using System;
using System.Net.Http;
using System.Text;

namespace PicoTest.Core.Transport
{
    /// <summary>IHttpTransport 的真实实现。HEAD 的 offset 从 Content-Length 头转为 Body（对齐 FakeIngestServer 语义）。</summary>
    public sealed class HttpClientTransport : IHttpTransport, IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;

        public HttpClientTransport(string baseUrl, string apiToken = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (!string.IsNullOrEmpty(apiToken))
                _client.DefaultRequestHeaders.Add("X-Api-Token", apiToken);
        }

        public HttpResult Send(string method, string path, byte[] body)
        {
            try
            {
                var req = new HttpRequestMessage(new HttpMethod(method), _baseUrl + path);
                if (body != null) req.Content = new ByteArrayContent(body);
                var resp = _client.SendAsync(req).GetAwaiter().GetResult();

                byte[] respBody;
                if (method == "HEAD")
                {
                    long len = resp.Content?.Headers?.ContentLength ?? 0;
                    respBody = Encoding.UTF8.GetBytes(len.ToString());
                }
                else
                {
                    respBody = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
                return new HttpResult((int)resp.StatusCode, respBody);
            }
            catch (Exception)
            {
                return new HttpResult(0, null); // 网络异常 = 不 Ok，交给重试
            }
        }

        public void Dispose() => _client.Dispose();
    }
}
```

- [ ] **Step 2: 编译通过 + 全量回归绿** → **Step 3: Commit** `feat(core): real HttpClient transport`

---

### Task 11: e2e —— Editor 菜单入口 + run-e2e-local.ps1

**Files:**
- Create: `Assets/Editor/E2E/LocalPipelineE2E.cs`、`Tools/run-e2e-local.ps1`

流程：脚本起真 Node（:8077）→ RPC `ExecuteMenu("PicoTest/E2E/Run Local Pipeline")` → 菜单方法在编辑器内跑 Mock 采集→录制→真 HTTP 上传 → Console 打 `[E2E] PASS/FAIL` → 脚本用 `AssertConsoleContains` 断言 → 关 Node、清数据。

- [ ] **Step 1: Editor 入口**

```csharp
// Assets/Editor/E2E/LocalPipelineE2E.cs
using System.IO;
using UnityEditor;
using UnityEngine;
using PicoTest.Core.Capture;
using PicoTest.Core.Recording;
using PicoTest.Core.Transport;

namespace PicoTest.Editor.E2E
{
    /// <summary>本地全链路 e2e：Mock 源 → 录制 → 真 HTTP 上传到 localhost:8077。由 run-e2e-local.ps1 经 RPC 触发。</summary>
    public static class LocalPipelineE2E
    {
        [MenuItem("PicoTest/E2E/Run Local Pipeline")]
        public static void Run()
        {
            var root = Path.Combine(Path.GetTempPath(), "picotest-e2e");
            if (Directory.Exists(root)) Directory.Delete(root, true);

            var session = new CaptureSession(root, "editor-e2e",
                new ICaptureSource[] { new MockBodyPoseSource(), new MockVideoSource() });
            session.Start();
            for (long now = 0; now <= 2_000_000_000L; now += 5_000_000L) session.Tick(now);
            var dir = session.SessionDir;
            session.Stop();

            var meta = Manifest.Load(dir);
            if (meta.Status != SessionStatus.Completed || meta.Streams[0].DroppedFrames > 0)
            {
                Debug.LogError($"[E2E] FAIL: bad session status={meta.Status} dropped={meta.Streams[0].DroppedFrames}");
                return;
            }

            using (var transport = new HttpClientTransport("http://127.0.0.1:8077"))
            {
                var ok = new UploadQueue(transport).UploadSession(dir);
                if (ok) Debug.Log($"[E2E] PASS: session {meta.SessionId} uploaded & verified");
                else Debug.LogError("[E2E] FAIL: upload failed");
            }
        }
    }
}
```

- [ ] **Step 2: e2e 脚本**（写完后补 BOM：`[IO.File]::WriteAllBytes($p, [byte[]](0xEF,0xBB,0xBF) + [IO.File]::ReadAllBytes($p))`）

```powershell
# Tools/run-e2e-local.ps1 — 本地全链路验收（前置：Unity 编辑器开着 + Server/npm install 已执行）
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$serverDir = Join-Path $root "Server"
$dataDir = Join-Path $env:TEMP ("ingest-e2e-" + (Get-Date -Format "HHmmss"))

# 1) 起 Node 接收服务
$env:INGEST_DATA_DIR = $dataDir; $env:INGEST_PORT = "8077"
$node = Start-Process node -ArgumentList "server.js" -WorkingDirectory $serverDir -PassThru -WindowStyle Hidden
try {
    $up = $false
    foreach ($i in 1..20) {
        Start-Sleep -Milliseconds 500
        try { if ((Invoke-RestMethod "http://127.0.0.1:8077/health" -TimeoutSec 2).status -eq "ok") { $up = $true; break } } catch {}
    }
    if (-not $up) { Write-Host "FAILED: ingest server didn't start" -ForegroundColor Red; exit 1 }

    # 2) RPC 触发 Editor 内 e2e
    function Rpc([string]$method, $params) {
        $body = @{ jsonrpc = "2.0"; method = $method; params = $params; id = 1 } | ConvertTo-Json -Depth 5 -Compress
        (Invoke-RestMethod "http://127.0.0.1:3212/rpc" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 120).result
    }
    $exec = Rpc "ExecuteMenu" @{ menuPath = "PicoTest/E2E/Run Local Pipeline"; ChangeTimeoutMs = $true; timeoutMs = 110000 }
    if (-not $exec.success) { Write-Host "FAILED to execute menu: $($exec.message)" -ForegroundColor Red; exit 1 }

    # 3) 断言 Console 有 PASS
    $assert = Rpc "AssertConsoleContains" @{ keyword = "[E2E] PASS" }
    if ($assert.success) {
        # 4) 服务端侧验证：存在 .complete 标记
        $complete = Get-ChildItem $dataDir -Recurse -Filter ".complete" -ErrorAction SilentlyContinue
        if ($complete) { Write-Host "E2E PASS (client + server verified)" -ForegroundColor Green; exit 0 }
        Write-Host "FAILED: client passed but server has no .complete marker" -ForegroundColor Red; exit 1
    }
    Write-Host "FAILED: $($assert.message)" -ForegroundColor Red; exit 1
} finally {
    Stop-Process -Id $node.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
}
```

注意：`AssertConsoleContains` 的参数名以 `Packages/cn.etetet.yiuimcp/Editor/UnityMCP/Tools/YIUIMCPTools_AssertConsoleContains.cs` 实际字段为准 —— 实施时先 Read 该文件核对（可能是 `keyword`/`contains`/其他）。

- [ ] **Step 3: 跑通**

Run: 编译 flow → `powershell -ExecutionPolicy Bypass -File Tools\run-e2e-local.ps1`
Expected: `E2E PASS (client + server verified)`，退出码 0

- [ ] **Step 4: 全量回归** `Tools\run-tests.ps1 -Mode All` 绿 → **Step 5: Commit** `feat(e2e): local full-pipeline acceptance (mock -> record -> upload -> verify)`

---

### Task 12: 收尾 —— journal + push + 计划勾选

- [ ] `Docs/journal/` 新增 M2a 记录（做了什么/测试数量/遗留）
- [ ] `git push`
- [ ] 把本计划文件中所有完成的 checkbox 勾上并 commit

---

## Self-Review 结论

- 规格覆盖：设计 §1→T1/T2，§2→T3/T4/T5，§3→T8/T9/T10，§7→各任务测试+T11；§4/§5/§6（Bridge/操控台/VR 面板）属 M2b 计划 —— 故意不在本计划
- 占位符：无（AssertConsoleContains 参数名标注了"实施时核对"，给了确切核对文件路径，非 TBD）
- 类型一致性：`Enqueue(streamId, byte[])` / `Tick(nowNs)` / `HttpResult.Body` 全计划一致；FakeIngestServer 与 Node 的 HEAD 语义差异已显式说明并在 T10 适配
