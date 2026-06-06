// Assets/Main/Core/Recording/ChunkWriter.cs
using System;
using System.Buffers.Binary;
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
            // 使用 BinaryPrimitives 写入小端 int32 长度前缀
            var lenBuf = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lenBuf, payload.Length);
            _fs.Write(lenBuf, 0, 4);
            _fs.Write(payload, 0, payload.Length);
        }

        /// <summary>每段关闭时 flush 到磁盘（崩溃只丢当前未关段的 OS 缓冲尾部）。</summary>
        private void RollChunk()
        {
            CloseCurrent();
            _chunkIndex++;
            var path = Path.Combine(_dir, $"chunk_{_chunkIndex:D4}.ptc");
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            WriteHeader();
        }

        private void WriteHeader()
        {
            // magic: 4 bytes ASCII
            _fs.Write(Encoding.ASCII.GetBytes(Magic), 0, 4);

            // formatVersion: int32 little-endian
            var buf4 = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buf4, FormatVersion);
            _fs.Write(buf4, 0, 4);

            // sessionId: 16 bytes GUID
            _fs.Write(_sessionId.ToByteArray(), 0, 16);

            // streamIdLen: int32 little-endian, then UTF8 bytes
            var sidBytes = Encoding.UTF8.GetBytes(_streamId);
            BinaryPrimitives.WriteInt32LittleEndian(buf4, sidBytes.Length);
            _fs.Write(buf4, 0, 4);
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
