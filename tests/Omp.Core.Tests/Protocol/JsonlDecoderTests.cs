using Newtonsoft.Json.Linq;
using Omp.Core.Protocol;
using System.Text;

namespace Omp.Core.Tests.Protocol;

public class JsonlDecoderTests
{
    private readonly List<JToken> _values = new();
    private readonly List<Exception> _errors = new();

    private JsonlDecoder Decoder(int maxLineBytes = JsonlDecoder.DefaultMaxLineBytes) =>
        new(v => _values.Add(v), e => _errors.Add(e), maxLineBytes);

    private static void Push(JsonlDecoder decoder, byte[] bytes) => decoder.Push(new ArraySegment<byte>(bytes));

    private string Values() => string.Join("|", _values.Select(v => v.ToString(Newtonsoft.Json.Formatting.None)));

    [Fact]
    public void DecodesObjectsSplitAcrossArbitraryChunkBoundaries()
    {
        var decoder = Decoder();
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":\"two\"}\n{\"c\":[3]}\n");
        for (var i = 0; i < bytes.Length; i += 3)
        {
            decoder.Push(new ArraySegment<byte>(bytes, i, Math.Min(3, bytes.Length - i)));
        }

        Assert.Equal("{\"a\":1}|{\"b\":\"two\"}|{\"c\":[3]}", Values());
    }

    [Fact]
    public void ReassemblesMultibyteUtf8CharactersSplitBetweenChunks()
    {
        var decoder = Decoder();
        var bytes = Encoding.UTF8.GetBytes("{\"t\":\"zażółć 😀\"}\n");
        for (var i = 0; i < bytes.Length; i++)
        {
            decoder.Push(new ArraySegment<byte>(bytes, i, 1));
        }

        Assert.Empty(_errors);
        Assert.Equal("zażółć 😀", (string?)_values.Single()["t"]);
    }

    [Fact]
    public void AcceptsCrlfLineEndingsAndSkipsBlankLines()
    {
        var decoder = Decoder();
        Push(decoder, Encoding.UTF8.GetBytes("{\"a\":1}\r\n\r\n   \n{\"b\":2}\r\n"));
        Assert.Empty(_errors);
        Assert.Equal("{\"a\":1}|{\"b\":2}", Values());
    }

    [Fact]
    public void ReportsAMalformedLineAndKeepsDecoding()
    {
        var decoder = Decoder();
        Push(decoder, Encoding.UTF8.GetBytes("{\"a\":1}\n{not json\n{\"b\":2}\n"));
        Assert.Equal("{\"a\":1}|{\"b\":2}", Values());
        Assert.Contains("malformed", Assert.Single(_errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportsInvalidUtf8AsAnErrorInsteadOfSubstitutingCharacters()
    {
        var decoder = Decoder();
        Push(decoder, [0x7b, 0x22, 0x61, 0x22, 0x3a, 0x22, 0xff, 0x22, 0x7d, 0x0a]);
        Push(decoder, Encoding.UTF8.GetBytes("{\"ok\":true}\n"));
        Assert.Equal("{\"ok\":true}", Values());
        Assert.Single(_errors);
    }

    [Fact]
    public void KeepsDateLikeStringsAsStrings()
    {
        var decoder = Decoder();
        Push(decoder, Encoding.UTF8.GetBytes("{\"timestamp\":\"2026-10-04T09:28:28.224Z\"}\n"));
        Assert.Equal(JTokenType.String, _values.Single()["timestamp"]!.Type);
        Assert.Equal("2026-10-04T09:28:28.224Z", (string?)_values.Single()["timestamp"]);
    }

    [Fact]
    public void FlushesAFinalUnterminatedLineOnEnd()
    {
        var decoder = Decoder();
        Push(decoder, Encoding.UTF8.GetBytes("{\"a\":1}"));
        Assert.Empty(_values);
        decoder.End();
        Assert.Equal("{\"a\":1}", Values());
    }

    [Fact]
    public void DropsAnOverLongLineWithAnErrorAndResumesAtTheNextLine()
    {
        var decoder = Decoder(16);
        Push(decoder, Encoding.UTF8.GetBytes("{\"long\":\"aaaaaaaa"));
        Push(decoder, Encoding.UTF8.GetBytes("aaaaaaaaaaaaaaa\"}\n{\"b\":2}\n"));
        Assert.Equal("{\"b\":2}", Values());
        Assert.Contains("exceeds", Assert.Single(_errors).Message);
    }
}
