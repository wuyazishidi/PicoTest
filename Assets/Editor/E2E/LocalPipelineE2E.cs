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
