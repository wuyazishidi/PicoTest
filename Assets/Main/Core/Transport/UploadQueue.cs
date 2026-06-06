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
