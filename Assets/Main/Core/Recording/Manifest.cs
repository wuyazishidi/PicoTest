// Assets/Main/Core/Recording/Manifest.cs
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PicoTest.Core.Recording
{
    public static class Manifest
    {
        public const string FileName = "manifest.json";
        public const string CorruptSuffix = ".corrupt";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
        };

        /// <summary>
        /// 原子写入 manifest.json。
        /// 先写 .tmp，再用 File.Replace（NTFS 原子换名）替换 final，
        /// 避免 Delete+Move 之间崩溃导致 manifest 完全丢失。
        /// </summary>
        public static void Save(string sessionDir, SessionMeta meta)
        {
            Directory.CreateDirectory(sessionDir);
            var tmp = Path.Combine(sessionDir, FileName + ".tmp");
            File.WriteAllText(tmp, JsonConvert.SerializeObject(meta, Settings));
            var final = Path.Combine(sessionDir, FileName);
            if (File.Exists(final)) File.Replace(tmp, final, null); // NTFS 原子换名
            else File.Move(tmp, final);
        }

        public static SessionMeta Load(string sessionDir) =>
            JsonConvert.DeserializeObject<SessionMeta>(
                File.ReadAllText(Path.Combine(sessionDir, FileName)), Settings);

        /// <summary>
        /// 启动时调用：Recording 状态 = 上次崩溃 → 从 chunk 文件重建帧数并标记 Recovered。
        ///
        /// 容错约定：
        /// - 若 manifest.json 为空文件或 JSON 损坏（JsonReaderException），
        ///   则将其改名为 manifest.json.corrupt（保留现场），返回 false，不抛异常。
        /// - 若反序列化成功但 meta 为 null，同样改名并返回 false。
        /// - meta.Streams 为 null 时跳过流重建，防御性处理。
        /// </summary>
        public static bool RecoverIfNeeded(string sessionDir)
        {
            var path = Path.Combine(sessionDir, FileName);
            if (!File.Exists(path)) return false;

            SessionMeta meta;
            try
            {
                meta = Load(sessionDir);
            }
            catch (Exception ex) when (ex is JsonReaderException || ex is JsonSerializationException || ex is IOException)
            {
                MarkCorrupt(path);
                return false;
            }

            if (meta == null)
            {
                MarkCorrupt(path);
                return false;
            }

            if (meta.Status != SessionStatus.Recording) return false;

            if (meta.Streams != null)
            {
                foreach (var stream in meta.Streams)
                {
                    var streamDir = Path.Combine(sessionDir, "streams", stream.Id);
                    if (!Directory.Exists(streamDir)) continue;
                    stream.FrameCount = Directory.GetFiles(streamDir, "chunk_*.ptc")
                        .Sum(f => ChunkReader.ReadAllFrames(f).Count());
                }
            }

            meta.Status = SessionStatus.Recovered;
            Save(sessionDir, meta);
            return true;
        }

        private static void MarkCorrupt(string manifestPath)
        {
            var corruptPath = manifestPath + CorruptSuffix;
            if (File.Exists(corruptPath)) File.Delete(corruptPath);
            File.Move(manifestPath, corruptPath);
        }
    }
}
