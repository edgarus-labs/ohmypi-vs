using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Omp.Core.Protocol;

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
        if (!(value is JObject frame))
        {
            return new ReassemblyResult(null, new InvalidDataException("RPC frame must be a JSON object"));
        }

        if (Json.Str(frame, "type") != "rpc_chunk")
        {
            if (_pending is not null)
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
        if (chunkId is null || chunkId.Length == 0 || chunkId.Length > 128 ||
            !Json.IsSafeInteger(value["index"]) || !Json.IsSafeInteger(value["count"]) || !Json.IsSafeInteger(value["byteLength"]))
        {
            throw new InvalidDataException("Invalid RPC chunk metadata");
        }

        var index = (long)Json.Num(value["index"])!.Value;
        var count = (long)Json.Num(value["count"])!.Value;
        var length = (long)Json.Num(value["byteLength"])!.Value;
        if (index < 0 || count < 2 || index >= count || length < _limits.MaxFrameBytes || length > _limits.MaxReassembledFrameBytes)
        {
            throw new InvalidDataException("Invalid RPC chunk metadata");
        }

        var data = Json.Str(value, "data");
        if (data is null || data.Length == 0 || !Base64Pattern.IsMatch(data))
        {
            throw new InvalidDataException("Invalid RPC chunk data");
        }

        var bytes = Convert.FromBase64String(data);
        if (bytes.Length > _limits.MaxFrameBytes)
        {
            throw new InvalidDataException("RPC chunk payload exceeds the transport limit");
        }

        if (_pending is null)
        {
            if (index != 0)
            {
                throw new InvalidDataException("RPC chunk sequence must start at index 0");
            }

            _pending = new Pending(chunkId, count, length);
        }
        var pending = _pending;
        if (pending.ChunkId != chunkId || pending.Count != count || pending.ByteLength != length || pending.NextIndex != index)
        {
            throw new InvalidDataException("RPC chunk sequence mismatch");
        }

        pending.Parts.Add(bytes);
        pending.ReceivedBytes += bytes.Length;
        pending.NextIndex++;
        if (pending.ReceivedBytes > pending.ByteLength)
        {
            throw new InvalidDataException("RPC chunk sequence exceeds its declared length");
        }

        if (pending.NextIndex < pending.Count)
        {
            return default;
        }

        if (pending.ReceivedBytes != pending.ByteLength)
        {
            throw new InvalidDataException("RPC chunk sequence length mismatch");
        }

        _pending = null;

        var all = new byte[pending.ReceivedBytes];
        var offset = 0;
        foreach (var part in pending.Parts)
        {
            Buffer.BlockCopy(part, 0, all, offset, part.Length);
            offset += part.Length;
        }
        var frame = Json.Parse(StrictUtf8.GetString(all));
        if (!(frame is JObject record))
        {
            throw new InvalidDataException("Reassembled RPC frame must be a JSON object");
        }

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
