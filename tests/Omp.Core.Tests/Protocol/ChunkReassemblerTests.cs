using Newtonsoft.Json.Linq;
using Omp.Core.Protocol;
using System.Text;

namespace Omp.Core.Tests.Protocol;

public class ChunkReassemblerTests
{
    private static readonly ChunkLimits Limits = new(16, 1024);

    private static List<JObject> ChunkFrames(JObject frame, int size, string chunkId = "rpc-1")
    {
        var bytes = Encoding.UTF8.GetBytes(frame.ToString(Newtonsoft.Json.Formatting.None));

        return ChunkBytes(bytes, size, chunkId);
    }

    private static List<JObject> ChunkBytes(byte[] bytes, int size, string chunkId)
    {
        var count = (bytes.Length + size - 1) / size;

        return [.. Enumerable.Range(0, count).Select(index => new JObject
        {
            ["type"] = "rpc_chunk",
            ["chunkId"] = chunkId,
            ["index"] = index,
            ["count"] = count,
            ["byteLength"] = bytes.Length,
            ["data"] = Convert.ToBase64String(bytes, index * size, Math.Min(size, bytes.Length - index * size)),
        })];
    }

    private static JObject With(JObject chunk, string key, JToken value)
    {
        var copy = (JObject)chunk.DeepClone();
        copy[key] = value;

        return copy;
    }

    private static readonly JObject Padded = new() { ["type"] = "x", ["pad"] = "0123456789abcdef" };

    [Fact]
    public void PassesOrdinaryFramesThrough()
    {
        var result = new ChunkReassembler(Limits).Push(new JObject { ["type"] = "agent_start" });
        Assert.Null(result.Error);
        Assert.Equal("agent_start", (string?)result.Frame!["type"]);
    }

    [Fact]
    public void ReassemblesAValidChunkSequenceIncludingSplitMultibyteCharacters()
    {
        var r = new ChunkReassembler(Limits);
        var frame = new JObject { ["type"] = "response", ["id"] = "x", ["data"] = "ąćęłńóśźż 😀 long enough" };
        var chunks = ChunkFrames(frame, 7);
        var results = chunks.Select(r.Push).ToList();
        Assert.All(results.Take(results.Count - 1), x => Assert.True(x.Frame == null && x.Error == null));
        Assert.True(JToken.DeepEquals(frame, results.Last().Frame));
    }

    [Theory]
    [InlineData("chunkId", "", "Invalid RPC chunk metadata")]
    [InlineData("chunkId", "129", "Invalid RPC chunk metadata")]
    [InlineData("index", "-1", "Invalid RPC chunk metadata")]
    [InlineData("count", "1", "Invalid RPC chunk metadata")]
    [InlineData("index", "count", "Invalid RPC chunk metadata")]
    [InlineData("byteLength", "15", "Invalid RPC chunk metadata")]
    [InlineData("byteLength", "1025", "Invalid RPC chunk metadata")]
    [InlineData("data", "@@@", "Invalid RPC chunk data")]
    [InlineData("data", "17", "RPC chunk payload exceeds the transport limit")]
    public void RejectsInvalidMetadataNamingTheRule(string key, string value, string message)
    {
        var first = ChunkFrames(Padded, 8)[0];
        JToken replacement = (key, value) switch
        {
            ("chunkId", "129") => new string('c', 129),
            ("index", "count") => first["count"]!,
            ("data", "17") => Convert.ToBase64String(new byte[17]),
            ("chunkId", _) or ("data", _) => value,
            _ => int.Parse(value),
        };
        Assert.Equal(message, new ChunkReassembler(Limits).Push(With(first, key, replacement)).Error!.Message);
    }

    [Fact]
    public void AcceptsTheLongestChunkIdAndTheSmallestDeclaredLength()
    {
        var r = new ChunkReassembler(Limits);
        var first = With(With(ChunkFrames(Padded, 8)[0], "chunkId", new string('c', 128)), "byteLength", 16);
        var result = r.Push(first);
        Assert.Null(result.Error);
    }

    [Fact]
    public void RejectsASequenceThatOverrunsItsDeclaredLengthBeforeTheLastChunk()
    {
        var r = new ChunkReassembler(Limits);
        JObject Chunk(int index) => new()
        {
            ["type"] = "rpc_chunk",
            ["chunkId"] = "o",
            ["index"] = index,
            ["count"] = 3,
            ["byteLength"] = 20,
            ["data"] = Convert.ToBase64String(new byte[16]),
        };
        Assert.Null(r.Push(Chunk(0)).Error);
        Assert.Equal("RPC chunk sequence exceeds its declared length", r.Push(Chunk(1)).Error!.Message);
    }

    [Fact]
    public void RejectsASequenceThatDoesNotStartAtIndex0()
    {
        var chunks = ChunkFrames(Padded, 8);
        Assert.Contains("index 0", new ChunkReassembler(Limits).Push(chunks[1]).Error!.Message);
    }

    [Fact]
    public void RejectsInterleavedSequencesAndRecoversForTheNextOne()
    {
        var r = new ChunkReassembler(Limits);
        var a = ChunkFrames(new JObject { ["type"] = "a", ["pad"] = "0123456789abcdef" }, 8, "a");
        var b = ChunkFrames(new JObject { ["type"] = "b", ["pad"] = "0123456789abcdef" }, 8, "b");
        var first = r.Push(a[0]);
        Assert.True(first.Frame is null && first.Error is null);
        Assert.Contains("mismatch", r.Push(b[0]).Error!.Message);
        var results = b.Select(r.Push).ToList();
        Assert.Equal("b", (string?)results.Last().Frame!["type"]);
    }

    [Fact]
    public void ReportsAnInterruptedSequenceButStillDeliversTheInterruptingFrame()
    {
        var r = new ChunkReassembler(Limits);
        r.Push(ChunkFrames(Padded, 8)[0]);
        var result = r.Push(new JObject { ["type"] = "agent_start" });
        Assert.Contains("interrupted", result.Error!.Message);
        Assert.Equal("agent_start", (string?)result.Frame!["type"]);
    }

    [Fact]
    public void RejectsAByteLengthMismatch()
    {
        var r = new ChunkReassembler(Limits);
        var chunks = ChunkFrames(Padded, 8).Select(c => With(c, "byteLength", (int)c["byteLength"]! + 1)).ToList();
        var results = chunks.Select(r.Push).ToList();
        Assert.Equal("RPC chunk sequence length mismatch", results.Last().Error!.Message);
    }

    [Fact]
    public void RejectsReassembledPayloadsThatAreNotAJsonObject()
    {
        var r = new ChunkReassembler(Limits);
        var chunks = ChunkBytes(Encoding.UTF8.GetBytes("\"just a long string value\""), 13, "s");
        var results = chunks.Select(r.Push).ToList();
        Assert.NotNull(results[1].Error);
    }
}
