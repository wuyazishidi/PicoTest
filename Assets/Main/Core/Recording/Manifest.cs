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
