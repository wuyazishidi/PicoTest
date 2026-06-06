// Assets/Main/Core/Recording/ChunkReader.cs
// NOTE: All multi-byte integers are read with BinaryPrimitives.ReadInt32LittleEndian,
// which is explicit about byte order and correct on any platform.
// This is symmetric with ChunkWriter, which uses BinaryPrimitives.WriteInt32LittleEndian.
// No platform-dependent byte-order assumption is made in either direction.
using System;
using System.Buffers.Binary;
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
            {
                // --- 读头部 ---
                // magic: 4 bytes
                var magicBuf = new byte[4];
                if (fs.Read(magicBuf, 0, 4) < 4) yield break;
                var magic = System.Text.Encoding.ASCII.GetString(magicBuf);
                if (magic != ChunkWriter.Magic) throw new InvalidDataException($"Bad magic in {path}");

                // formatVersion: int32 little-endian
                var buf4 = new byte[4];
                if (fs.Read(buf4, 0, 4) < 4) yield break;
                // int formatVersion = BinaryPrimitives.ReadInt32LittleEndian(buf4); // unused

                // sessionId: 16 bytes
                var sidBuf = new byte[16];
                if (fs.Read(sidBuf, 0, 16) < 16) yield break;

                // streamIdLen: int32 little-endian
                if (fs.Read(buf4, 0, 4) < 4) yield break;
                int sidLen = BinaryPrimitives.ReadInt32LittleEndian(buf4);
                if (sidLen < 0 || sidLen > 4096) throw new InvalidDataException($"Invalid streamId length {sidLen} in {path}");
                var streamIdBuf = new byte[sidLen];
                if (sidLen > 0 && fs.Read(streamIdBuf, 0, sidLen) < sidLen) yield break;

                // --- 读帧 ---
                while (true)
                {
                    // 需要至少 4 字节读长度前缀
                    if (fs.Length - fs.Position < 4) yield break;
                    if (fs.Read(buf4, 0, 4) < 4) yield break;
                    int len = BinaryPrimitives.ReadInt32LittleEndian(buf4);
                    if (len < 0 || fs.Length - fs.Position < len) yield break; // 残帧：静默丢弃
                    var frame = new byte[len];
                    if (len > 0 && fs.Read(frame, 0, len) < len) yield break;
                    yield return frame;
                }
            }
        }
    }
}
