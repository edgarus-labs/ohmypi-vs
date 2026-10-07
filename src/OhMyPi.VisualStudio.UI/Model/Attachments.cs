using System;
using System.Collections.Generic;
using System.Linq;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
    internal enum PasteKind { Inline, Attachment }

    /// <summary>Something attached to the next prompt; shown as a removable chip above the input.</summary>
    internal abstract class Attachment
    {
        protected Attachment(string label) => Label = label;

        public string Label { get; }
    }

    /// <summary>A file or folder sent to OMP as an <c>@path</c> mention.</summary>
    internal sealed class FileAttachment : Attachment
    {
        public FileAttachment(string path) : base(System.IO.Path.GetFileName(path.TrimEnd('\\', '/')))
        {
            Path = path;
        }

        /// <summary>Absolute path.</summary>
        public string Path { get; }
    }

    /// <summary>Long pasted text, sent as a <c>&lt;pasted-text&gt;</c> block.</summary>
    internal sealed class PastedTextAttachment : Attachment
    {
        public PastedTextAttachment(string label, string text) : base(label)
        {
            Text = text;
        }

        public string Text { get; }
    }

    /// <summary>Pasted or dropped image, sent in the prompt's <c>images</c>.</summary>
    internal sealed class ImageAttachment : Attachment
    {
        public ImageAttachment(string label, string data, string mimeType, long bytes) : base(label)
        {
            Data = data;
            MimeType = mimeType;
            Bytes = bytes;
        }

        /// <summary>Base64 bytes.</summary>
        public string Data { get; }
        public string MimeType { get; }
        public long Bytes { get; }
    }

    internal sealed class ImageAdmission
    {
        public ImageAdmission(IReadOnlyList<int> accepted, IReadOnlyList<string> rejected)
        {
            Accepted = accepted;
            Rejected = rejected;
        }

        /// <summary>Indexes of the admitted files.</summary>
        public IReadOnlyList<int> Accepted { get; }
        /// <summary>One message per rejected file.</summary>
        public IReadOnlyList<string> Rejected { get; }
    }

    internal sealed class ComposedPrompt
    {
        public ComposedPrompt(string message, IReadOnlyList<PromptImage> images)
        {
            Message = message;
            Images = images;
        }

        public string Message { get; }
        public IReadOnlyList<PromptImage> Images { get; }
    }

    /// <summary>Composer attachment rules: paste classification, image admission limits and the prompt OMP receives.</summary>
    internal static class Attachments
    {
        public const long MaxImageBytes = 10 * 1024 * 1024;
        public const int MaxImages = 8;
        public const long MaxTotalImageBytes = 20 * 1024 * 1024;
        private const int InlineMaxLines = 10;
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
                if (attachment is FileAttachment file && !known.Add(file.Path)) continue;
                merged.Add(attachment);
            }
            return merged;
        }

        /// <summary>Inside the working directory relative to it, otherwise absolute.</summary>
        public static string MentionPath(string path, string cwd)
        {
            var root = Normalize(cwd).TrimEnd('\\');
            var normalized = Normalize(path);
            if (root.Length == 0 || normalized.Length <= root.Length + 1) return path;
            if (!normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return path;
            return path.Substring(root.Length + 1);
        }

        private static string Normalize(string path) => path.Replace('/', '\\');

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
                if (reason != null)
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
}
