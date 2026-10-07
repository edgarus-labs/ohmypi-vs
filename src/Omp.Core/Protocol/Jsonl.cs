using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Omp.Core.Internal;

namespace Omp.Core.Protocol
{
    /// <summary>
    /// Incremental newline-delimited JSON decoder. Lines are split on raw bytes, so multibyte UTF-8
    /// sequences may straddle chunk boundaries. Not thread-safe.
    /// </summary>
    internal sealed class JsonlDecoder
    {
        /// <summary>Line ceiling until the server advertises its frame limit, and for servers that never do.</summary>
        public const int DefaultMaxLineBytes = 8 * 1024 * 1024;
        private const byte Newline = 0x0a;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly Action<JToken> _value;
        private readonly Action<Exception> _error;
        private readonly MemoryStream _pending = new MemoryStream();
        private bool _discarding;

        public JsonlDecoder(Action<JToken> value, Action<Exception> error, int maxLineBytes = DefaultMaxLineBytes)
        {
            _value = value;
            _error = error;
            MaxLineBytes = maxLineBytes;
        }

        /// <summary>Longest accepted line in bytes; longer lines are reported and dropped.</summary>
        public int MaxLineBytes { get; set; }

        public void Push(ArraySegment<byte> chunk)
        {
            var bytes = chunk.Array!;
            var start = chunk.Offset;
            var end = chunk.Offset + chunk.Count;
            while (start < end)
            {
                var newline = Array.IndexOf(bytes, Newline, start, end - start);
                if (newline == -1)
                {
                    Append(bytes, start, end - start);
                    return;
                }
                Append(bytes, start, newline - start);
                CompleteLine();
                start = newline + 1;
            }
        }

        /// <summary>Decode a trailing line that was not newline-terminated.</summary>
        public void End()
        {
            if (_discarding)
            {
                _discarding = false;
                return;
            }
            if (_pending.Length > 0) CompleteLine();
        }

        private void Append(byte[] bytes, int offset, int count)
        {
            if (_discarding || count == 0) return;
            if (_pending.Length + count > MaxLineBytes)
            {
                Reset();
                _discarding = true;
                _error(new InvalidDataException($"JSONL line exceeds {MaxLineBytes} bytes; line dropped"));
                return;
            }
            _pending.Write(bytes, offset, count);
        }

        private void CompleteLine()
        {
            if (_discarding)
            {
                _discarding = false;
                return;
            }
            var length = (int)_pending.Length;
            var buffer = _pending.GetBuffer();
            string text;
            try
            {
                text = StrictUtf8.GetString(buffer, 0, length);
            }
            catch (DecoderFallbackException)
            {
                Reset();
                _error(new InvalidDataException("Malformed JSONL line: invalid UTF-8"));
                return;
            }
            Reset();
            if (string.IsNullOrWhiteSpace(text)) return;
            JToken value;
            try
            {
                value = Json.Parse(text);
            }
            catch (Exception error) when (error is Newtonsoft.Json.JsonException)
            {
                var preview = text.Length > 120 ? text.Substring(0, 120) + "…" : text;
                _error(new InvalidDataException($"Malformed JSONL line ({error.Message}): {preview}", error));
                return;
            }
            _value(value);
        }

        private void Reset()
        {
            _pending.SetLength(0);
            if (_pending.Capacity > 1024 * 1024) _pending.Capacity = 0;
        }
    }

    internal readonly struct ChunkLimits
    {
        public static readonly ChunkLimits Default = new ChunkLimits(1024 * 1024, 64 * 1024 * 1024);

        public ChunkLimits(long maxFrameBytes, long maxReassembledFrameBytes)
        {
            MaxFrameBytes = maxFrameBytes;
            MaxReassembledFrameBytes = maxReassembledFrameBytes;
        }

        /// <summary>Physical frame ceiling advertised by <c>ready.maxFrameBytes</c>.</summary>
        public long MaxFrameBytes { get; }

        /// <summary>Logical frame ceiling advertised by <c>ready.maxReassembledFrameBytes</c>.</summary>
        public long MaxReassembledFrameBytes { get; }
    }

    internal readonly struct ReassemblyResult
    {
        public ReassemblyResult(JObject? frame, Exception? error)
        {
            Frame = frame;
            Error = error;
        }

        /// <summary>A complete logical frame ready for dispatch.</summary>
        public JObject? Frame { get; }

        /// <summary>The chunk (or the sequence it belonged to) was rejected.</summary>
        public Exception? Error { get; }
    }

    /// <summary>
    /// Protocol v2 <c>rpc_chunk</c> reassembler applying the validation rules of OMP's <c>RpcFrameDecoder</c>.
    /// A rejected sequence is dropped and decoding continues. Not thread-safe.
    /// </summary>
    internal sealed class ChunkReassembler
    {
        private static readonly Regex Base64Pattern = new Regex("^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$", RegexOptions.CultureInvariant);
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly ChunkLimits _limits;
        private Pending? _pending;

        public ChunkReassembler(ChunkLimits limits)
        {
            _limits = limits;
        }

        public ReassemblyResult Push(JToken value)
        {
            if (!(value is JObject frame)) return new ReassemblyResult(null, new InvalidDataException("RPC frame must be a JSON object"));
            if (Json.Str(frame, "type") != "rpc_chunk")
            {
                if (_pending != null)
                {
                    _pending = null;
                    return new ReassemblyResult(frame, new InvalidDataException("RPC chunk sequence interrupted by another frame"));
                }
                return new ReassemblyResult(frame, null);
            }
            try
            {
                return PushChunk(frame);
            }
            catch (Exception error) when (error is InvalidDataException || error is Newtonsoft.Json.JsonException || error is DecoderFallbackException || error is FormatException)
            {
                _pending = null;
                return new ReassemblyResult(null, error);
            }
        }

        private ReassemblyResult PushChunk(JObject value)
        {
            var chunkId = Json.Str(value, "chunkId");
            if (chunkId == null || chunkId.Length == 0 || chunkId.Length > 128 ||
                !Json.IsSafeInteger(value["index"]) || !Json.IsSafeInteger(value["count"]) || !Json.IsSafeInteger(value["byteLength"]))
                throw new InvalidDataException("Invalid RPC chunk metadata");
            var index = (long)Json.Num(value["index"])!.Value;
            var count = (long)Json.Num(value["count"])!.Value;
            var length = (long)Json.Num(value["byteLength"])!.Value;
            if (index < 0 || count < 2 || index >= count || length < _limits.MaxFrameBytes || length > _limits.MaxReassembledFrameBytes)
                throw new InvalidDataException("Invalid RPC chunk metadata");
            var data = Json.Str(value, "data");
            if (data == null || data.Length == 0 || !Base64Pattern.IsMatch(data)) throw new InvalidDataException("Invalid RPC chunk data");
            var bytes = Convert.FromBase64String(data);
            if (bytes.Length > _limits.MaxFrameBytes) throw new InvalidDataException("RPC chunk payload exceeds the transport limit");

            if (_pending == null)
            {
                if (index != 0) throw new InvalidDataException("RPC chunk sequence must start at index 0");
                _pending = new Pending(chunkId, count, length);
            }
            var pending = _pending;
            if (pending.ChunkId != chunkId || pending.Count != count || pending.ByteLength != length || pending.NextIndex != index)
                throw new InvalidDataException("RPC chunk sequence mismatch");
            pending.Parts.Add(bytes);
            pending.ReceivedBytes += bytes.Length;
            pending.NextIndex++;
            if (pending.ReceivedBytes > pending.ByteLength) throw new InvalidDataException("RPC chunk sequence exceeds its declared length");
            if (pending.NextIndex < pending.Count) return default;
            if (pending.ReceivedBytes != pending.ByteLength) throw new InvalidDataException("RPC chunk sequence length mismatch");
            _pending = null;

            var all = new byte[pending.ReceivedBytes];
            var offset = 0;
            foreach (var part in pending.Parts)
            {
                Buffer.BlockCopy(part, 0, all, offset, part.Length);
                offset += part.Length;
            }
            var frame = Json.Parse(StrictUtf8.GetString(all));
            if (!(frame is JObject record)) throw new InvalidDataException("Reassembled RPC frame must be a JSON object");
            return new ReassemblyResult(record, null);
        }

        private sealed class Pending
        {
            public Pending(string chunkId, long count, long byteLength)
            {
                ChunkId = chunkId;
                Count = count;
                ByteLength = byteLength;
            }

            public string ChunkId { get; }
            public long Count { get; }
            public long ByteLength { get; }
            public long NextIndex { get; set; }
            public List<byte[]> Parts { get; } = new List<byte[]>();
            public long ReceivedBytes { get; set; }
        }
    }
}
