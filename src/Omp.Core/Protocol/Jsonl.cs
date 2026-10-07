using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.IO;
using System.Text;

namespace Omp.Core.Protocol;

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
        if (_pending.Length > 0)
        {
            CompleteLine();
        }
    }

    private void Append(byte[] bytes, int offset, int count)
    {
        if (_discarding || count == 0)
        {
            return;
        }

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
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

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
        if (_pending.Capacity > 1024 * 1024)
        {
            _pending.Capacity = 0;
        }
    }
}
