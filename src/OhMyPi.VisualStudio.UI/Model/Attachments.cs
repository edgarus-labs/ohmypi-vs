using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Composer attachment rules: paste classification, image admission limits and the prompt OMP receives.</summary>
internal static class Attachments
{
    /// <summary>
    /// The max image bytes.
    /// </summary>
    public const long MaxImageBytes = 10 * 1024 * 1024;
    /// <summary>
    /// The max images.
    /// </summary>
    public const int MaxImages = 8;
    /// <summary>
    /// The max total image bytes.
    /// </summary>
    public const long MaxTotalImageBytes = 20 * 1024 * 1024;
    /// <summary>
    /// The inline max lines.
    /// </summary>
    private const int InlineMaxLines = 10;
    /// <summary>
    /// The inline max chars.
    /// </summary>
    private const int InlineMaxChars = 1000;

    /// <summary>Long pastes (more than 10 lines or 1000 characters) become an attachment instead of flooding the input.</summary>
    public static PasteKind ClassifyPaste(string text) =>
        text.Length > InlineMaxChars || text.Split('\n').Length > InlineMaxLines ? PasteKind.Attachment : PasteKind.Inline;

    /// <summary>Appends <paramref name="incoming"/>, skipping files that are already attached.</summary>
    public static IReadOnlyList<Attachment> Merge(IReadOnlyList<Attachment> current, IReadOnlyList<Attachment> incoming)
    {
        var known = new HashSet<string>(current.OfType<FileAttachment>().Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
        var merged = current.ToList();
        foreach (var attachment in incoming)
        {
            if (attachment is FileAttachment file && !known.Add(file.Path))
            {
                continue;
            }

            merged.Add(attachment);
        }

        return merged;
    }

    /// <summary>Inside the working directory relative to it, otherwise absolute.</summary>
    public static string MentionPath(string path, string cwd)
    {
        var root = Normalize(cwd).TrimEnd('\\');
        var normalized = Normalize(path);
        if (root.Length == 0 || normalized.Length <= root.Length + 1)
        {
            return path;
        }

        if (!normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path.Substring(root.Length + 1);
    }

    /// <summary>
    /// Normalizes the specified path by replacing all forward slash characters with backslashes.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The string result.</returns>
    private static string Normalize(string path) => path.Replace('/', '\\');

    /// <summary>
    /// Returns the label of the specified attachment, appending the line count if the attachment is a pasted text attachment.
    /// </summary>
    /// <param name="attachment">The attachment.</param>
    /// <returns>The string result.</returns>
    public static string ChipLabel(Attachment attachment) =>
        attachment is PastedTextAttachment text ? $"{text.Label} · {Chrome.LineCount(text.Text)}" : attachment.Label;

    /// <summary>Splits image files into those within the per-image, count and total limits and messages for the rest.</summary>
    public static ImageAdmission AdmitImages(int count, long bytes, IReadOnlyList<(string Name, long Size)> files)
    {
        var accepted = new List<int>();
        var rejected = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            var (name, size) = files[i];
            var reason = size > MaxImageBytes ? "larger than 10 MB"
                : count >= MaxImages ? $"at most {MaxImages} images per prompt"
                : bytes + size > MaxTotalImageBytes ? "images in one prompt may total at most 20 MB"
                : null;
            if (reason is not null)
            {
                rejected.Add($"Image {(name.Length > 0 ? name : "from clipboard")} was not attached: {reason}.");
                continue;
            }
            accepted.Add(i);
            count++;
            bytes += size;
        }

        return new ImageAdmission(accepted, rejected);
    }

    /// <summary>
    /// The OMP prompt for the user's text and attachments: files become <c>@path</c> mentions (OMP reads them itself),
    /// pasted text becomes <c>&lt;pasted-text&gt;</c> blocks, images travel in the RPC <c>images</c> field. A slash
    /// command is trimmed so OMP sees the leading "/".
    /// </summary>
    public static ComposedPrompt Compose(string text, IReadOnlyList<Attachment> attachments, string cwd)
    {
        var message = PromptFormatter.Format(new PromptParts
        {
            Text = SlashCommands.IsCommand(text) ? text.Trim() : text,
            Pasted = attachments.OfType<PastedTextAttachment>().Select(p => new PastedText { Name = p.Label, Text = p.Text }).ToList(),
            Files = attachments.OfType<FileAttachment>().Select(f => MentionPath(f.Path, cwd)).ToList(),
        });
        var images = attachments.OfType<ImageAttachment>().Select(i => new PromptImage { Data = i.Data, MimeType = i.MimeType }).ToList();

        return new ComposedPrompt(message, images);
    }
}
