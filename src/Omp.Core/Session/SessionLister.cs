using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Omp.Core.Session;

/// <summary>Summaries of the OMP session files stored in one directory.</summary>
internal static class SessionLister
{
    /// <summary>
    /// The head bytes.
    /// </summary>
    private const int HeadBytes = 64 * 1024;
    /// <summary>
    /// The first message chars.
    /// </summary>
    private const int FirstMessageChars = 200;
    /// <summary>Session files read at the same time, so large session directories stay within handle limits.</summary>
    private const int ReadConcurrency = 16;

    /// <summary>
    /// Summaries of the session files (<c>*.jsonl</c>) in <paramref name="dir"/>, newest first. Only the first 64 KiB of
    /// each file is read; files without a session header are skipped, and so are entries that cannot be read: a file
    /// that vanished silently, any other failure with a warning. Only a directory that cannot be listed fails the call.
    /// The directory is listed and the files are read on thread-pool threads, never on the caller's thread.
    /// <paramref name="read"/> and <paramref name="enumerate"/> replace file access in tests.
    /// </summary>
    public static async Task<IReadOnlyList<SessionSummary>> ListAsync(string dir, IOmpLogger logger, Func<string, Task<SessionSummary?>>? read = null, Func<string, IEnumerable<string>>? enumerate = null)
    {
        read ??= ReadSummaryAsync;
        enumerate ??= Directory.EnumerateFiles;
        string[] files;
        try
        {
            files = await Task.Run(() => enumerate(dir).Where(name => name.EndsWith(".jsonl", StringComparison.Ordinal)).ToArray()).ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<SessionSummary>();
        }
        var summaries = new List<SessionSummary>();
        var next = -1;
        async Task WorkerAsync()
        {
            int index;
            while ((index = Interlocked.Increment(ref next)) < files.Length)
            {
                var file = files[index];
                try
                {
                    var summary = await read(file).ConfigureAwait(false);
                    if (summary is not null)
                    {
                        lock (summaries)
                        {
                            summaries.Add(summary);
                        }
                    }
                }
                catch (Exception error) when (error is FileNotFoundException || error is DirectoryNotFoundException)
                {
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    logger.Warn($"Skipped unreadable session file {file}", error);
                }
            }
        }
        await Task.WhenAll(Enumerable.Range(0, Math.Min(ReadConcurrency, files.Length)).Select(_ => Task.Run(WorkerAsync))).ConfigureAwait(false);

        return summaries.OrderByDescending(s => s.Modified).ToArray();
    }

    /// <summary>
    /// Asynchronously reads and parses a session file to extract metadata and summary details into a SessionSummary object.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the session summary?.</returns>
    private static async Task<SessionSummary?> ReadSummaryAsync(string file)
    {
        byte[] head;
        int bytesRead;
        long size;
        DateTime modified;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
        {
            size = stream.Length;
            modified = File.GetLastWriteTimeUtc(file);
            head = new byte[Math.Min(HeadBytes, size)];
            bytesRead = 0;
            while (bytesRead < head.Length)
            {
                var n = await stream.ReadAsync(head, bytesRead, head.Length - bytesRead).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                bytesRead += n;
            }
        }
        var completeBytes = bytesRead == size ? bytesRead : Array.LastIndexOf(head, (byte)'\n', Math.Max(0, bytesRead - 1)) + 1;
        var complete = Encoding.UTF8.GetString(head, 0, completeBytes);
        var summary = new SessionSummary
        {
            Path = file,
            Modified = new DateTimeOffset(modified, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            Size = size,
        };
        var hasHeader = false;
        foreach (var line in complete.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JToken entry;
            try
            {
                entry = Json.Parse(line);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                continue;
            }
            if (!(entry is JObject record))
            {
                continue;
            }

            var type = Json.Str(record, "type");
            if (type == "session" && !hasHeader)
            {
                hasHeader = true;
                summary.Id = Json.Str(record, "id");
                summary.Cwd = Json.Str(record, "cwd");
            }
            else if (type == "title" && !string.IsNullOrWhiteSpace(Json.Str(record, "title")))
            {
                summary.Title = Json.Str(record, "title")!.Trim();
            }
            else if (type == "message" && summary.FirstMessage is null && record["message"] is JObject message)
            {
                if (Json.Str(message, "role") != "user")
                {
                    continue;
                }

                var text = UserText(message["content"])?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    summary.FirstMessage = text!.Length > FirstMessageChars ? text.Substring(0, FirstMessageChars) + "…" : text;
                }
            }
        }

        return hasHeader ? summary : null;
    }

    /// <summary>
    /// Extracts the user-provided text from a JSON token, supporting both direct string values and structured text blocks within an array.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>The string? result.</returns>
    private static string? UserText(JToken? content)
    {
        var text = Json.Str(content);
        if (text is not null)
        {
            return text;
        }

        if (!(content is JArray blocks))
        {
            return null;
        }

        foreach (var block in blocks)
        {
            if (Json.Str(block, "type") == "text" && Json.Str(block, "text") is string value)
            {
                return value;
            }
        }

        return null;
    }
}
