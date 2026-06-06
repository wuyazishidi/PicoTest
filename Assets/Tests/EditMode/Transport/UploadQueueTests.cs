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
    /// <summary>内存假服务器：实现 openapi 契约同款语义（注册/HEAD offset/PUT 分块/complete 校验）。
    /// 注意测试内部约定：HEAD 的 offset 经 Body 传十进制字符串（真实传输层 Task 10 会把 Content-Length 头转成 Body，语义对齐）。</summary>
    public class FakeIngestServer : IHttpTransport
    {
        public readonly Dictionary<string, MemoryStream> Files = new Dictionary<string, MemoryStream>();
        public readonly HashSet<string> Sessions = new HashSet<string>();
        public readonly HashSet<string> Completed = new HashSet<string>();
        public int FailNextNRequests;   // 注入故障

        /// <summary>记录每次 PUT 的完整路径（含 ?offset=...），用于断言续传 offset 语义。</summary>
        public readonly List<string> PutLog = new List<string>();

        /// <summary>
        /// 若为正数，对指定文件 key 的首个 PUT 强制返回 409（模拟乱序/服务端冲突），
        /// 触发一次后自动清零，后续 PUT 正常处理。
        /// key 为 basePath（不含 query），即 /api/v1/sessions/{sid}/files/{fileKey}。
        /// </summary>
        public readonly HashSet<string> ForceFirstPut409 = new HashSet<string>();
        private readonly HashSet<string> _firstPut409Fired = new HashSet<string>();

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
                long len = Files.TryGetValue(path, out var ms) ? ms.Length : 0;
                return new HttpResult(200, Encoding.UTF8.GetBytes(len.ToString()));
            }
            if (method == "PUT" && path.Contains("/files/"))
            {
                PutLog.Add(path);

                var q = path.Split('?');
                long offset = long.Parse(q[1].Replace("offset=", ""));
                var key = q[0];

                // 注入首个 PUT 409：模拟乱序冲突，触发一次后自动移除开关
                if (ForceFirstPut409.Contains(key) && !_firstPut409Fired.Contains(key))
                {
                    _firstPut409Fired.Add(key);
                    return new HttpResult(409, null);
                }

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
            var session = new CaptureSession(_root, "d", new ICaptureSource[] { new MockBodyPoseSource() });
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
            var remoteKey = $"/api/v1/sessions/{sid}/files/{Uri.EscapeDataString("streams/body_pose/" + Path.GetFileName(localChunk))}";
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

        /// <summary>
        /// [P3] 验证真续传语义：若服务端已有前 64 字节，首个 PUT 的 offset 必须为 64，
        /// 且最终字节与本地逐位一致。
        /// </summary>
        [Test]
        public void Upload_Resume_SkipsAlreadyReceivedBytes()
        {
            var dir = MakeCompletedSession();
            var meta = Manifest.Load(dir);
            var sid = meta.SessionId;

            // 找一个实际存在的 chunk 文件
            var localChunk = Directory.GetFiles(Path.Combine(dir, "streams", "body_pose"), "chunk_*.ptc")[0];
            var rel = "streams/body_pose/" + Path.GetFileName(localChunk);
            var fileKey = Uri.EscapeDataString(rel);
            var basePath = $"/api/v1/sessions/{sid}/files/{fileKey}";

            var localData = File.ReadAllBytes(localChunk);
            const int preSeeded = 64;

            var server = new FakeIngestServer();
            // 向服务端预置前 64 字节，模拟上次传输中断
            var preStream = new MemoryStream();
            preStream.Write(localData, 0, Math.Min(preSeeded, localData.Length));
            server.Files[basePath] = preStream;

            var q = new UploadQueue(server, partBytes: 64, maxRetries: 5, retryDelayMs: 0);
            Assert.IsTrue(q.UploadSession(dir));

            // 对该文件的首个 PUT 的 offset 应为 64（真续传，非从 0 重传）
            var firstPut = server.PutLog.Find(p => p.StartsWith(basePath + "?"));
            Assert.IsNotNull(firstPut, "应有对该文件的 PUT 请求");
            var firstOffset = long.Parse(firstPut.Split('?')[1].Replace("offset=", ""));
            Assert.AreEqual(preSeeded, firstOffset, "首个 PUT 的 offset 应跳过已接收字节");

            // 最终字节逐位一致
            CollectionAssert.AreEqual(localData, server.Files[basePath].ToArray());
        }

        /// <summary>
        /// [P6] 验证乱序 PUT 恢复：首个 PUT 强制返回 409，UploadSession 仍成功（HEAD 重查 offset 后恢复）。
        /// </summary>
        [Test]
        public void Upload_OutOfOrderPut_RecoversVia409()
        {
            var dir = MakeCompletedSession();
            var meta = Manifest.Load(dir);
            var sid = meta.SessionId;

            var localChunk = Directory.GetFiles(Path.Combine(dir, "streams", "body_pose"), "chunk_*.ptc")[0];
            var rel = "streams/body_pose/" + Path.GetFileName(localChunk);
            var fileKey = Uri.EscapeDataString(rel);
            var basePath = $"/api/v1/sessions/{sid}/files/{fileKey}";

            var server = new FakeIngestServer();
            // 注入：对该文件的首个 PUT 返回 409
            server.ForceFirstPut409.Add(basePath);

            var q = new UploadQueue(server, partBytes: 64, maxRetries: 10, retryDelayMs: 0);
            Assert.IsTrue(q.UploadSession(dir), "409 后经 HEAD 重查应能恢复并最终成功");

            // PutLog 中对该文件应有多于一次 PUT（第一次 409，后续成功）
            var puts = server.PutLog.FindAll(p => p.StartsWith(basePath + "?"));
            Assert.Greater(puts.Count, 1, "应有重试 PUT（第一次 409 触发重试）");
        }

        /// <summary>
        /// [P5] 验证 Failed 会话被拒绝：UploadSession 返回 false 且服务端无新会话注册。
        /// </summary>
        [Test]
        public void Upload_FailedSession_IsRejected()
        {
            // 手工构造一个 Status=Failed 的 manifest
            Directory.CreateDirectory(_root);
            var failedMeta = SessionMeta.CreateNew("test-device");
            failedMeta.Status = SessionStatus.Failed;
            Manifest.Save(_root, failedMeta);

            var server = new FakeIngestServer();
            var q = new UploadQueue(server, retryDelayMs: 0);

            var result = q.UploadSession(_root);

            Assert.IsFalse(result, "Failed 会话应被拒绝上传");
            Assert.AreEqual(0, server.Sessions.Count, "服务端不应收到任何会话注册");
        }
    }
}
