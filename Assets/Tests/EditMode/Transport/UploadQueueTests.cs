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
    }
}
