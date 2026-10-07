using System.Linq;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class AttachmentTests
    {
        [Fact]
        public void Short_pastes_stay_inline()
        {
            Assert.Equal(PasteKind.Inline, Attachments.ClassifyPaste("hello"));
            Assert.Equal(PasteKind.Inline, Attachments.ClassifyPaste(string.Join("\n", Enumerable.Repeat("x", 10))));
            Assert.Equal(PasteKind.Inline, Attachments.ClassifyPaste(new string('x', 1000)));
        }

        [Fact]
        public void Long_pastes_become_attachments()
        {
            Assert.Equal(PasteKind.Attachment, Attachments.ClassifyPaste(string.Join("\n", Enumerable.Repeat("x", 11))));
            Assert.Equal(PasteKind.Attachment, Attachments.ClassifyPaste(string.Join("\r\n", Enumerable.Repeat("x", 11))));
            Assert.Equal(PasteKind.Attachment, Attachments.ClassifyPaste(new string('x', 1001)));
        }

        [Fact]
        public void Merge_skips_files_already_attached()
        {
            var a = new FileAttachment("C:\\r\\a.cs");
            var text = new PastedTextAttachment("Pasted text 1", "t");
            var merged = Attachments.Merge(new Attachment[] { a, text }, new Attachment[] { new FileAttachment("C:\\r\\a.cs"), new FileAttachment("C:\\r\\b.cs"), new PastedTextAttachment("Pasted text 2", "t") });
            Assert.Equal(new[] { "a.cs", "Pasted text 1", "b.cs", "Pasted text 2" }, merged.Select(m => m.Label));
        }

        [Fact]
        public void MentionPath_is_relative_inside_cwd()
        {
            Assert.Equal("src\\a.cs", Attachments.MentionPath("C:\\repo\\src\\a.cs", "C:\\repo"));
            Assert.Equal("src\\a.cs", Attachments.MentionPath("c:\\Repo\\src\\a.cs", "C:\\repo\\"));
            Assert.Equal("C:\\other\\a.cs", Attachments.MentionPath("C:\\other\\a.cs", "C:\\repo"));
            Assert.Equal("C:\\repository\\a.cs", Attachments.MentionPath("C:\\repository\\a.cs", "C:\\repo"));
            Assert.Equal("C:\\repo", Attachments.MentionPath("C:\\repo", "C:\\repo"));
        }

        [Fact]
        public void TextChipLabel_shows_line_count()
        {
            Assert.Equal("Pasted text 1 · 3 lines", Attachments.ChipLabel(new PastedTextAttachment("Pasted text 1", "a\nb\nc")));
            Assert.Equal("a.cs", Attachments.ChipLabel(new FileAttachment("C:\\r\\a.cs")));
        }

        [Fact]
        public void AdmitImages_enforces_size_count_and_total_limits()
        {
            const long mb = 1024 * 1024;
            var result = Attachments.AdmitImages(0, 0, new[] { ("big.png", 11 * mb), ("ok.png", 2 * mb) });
            Assert.Equal(new[] { 1 }, result.Accepted);
            Assert.Equal(new[] { "Image big.png was not attached: larger than 10 MB." }, result.Rejected);

            var full = Attachments.AdmitImages(8, 0, new[] { ("x.png", 1L) });
            Assert.Empty(full.Accepted);
            Assert.Equal("Image x.png was not attached: at most 8 images per prompt.", full.Rejected.Single());

            var total = Attachments.AdmitImages(0, 15 * mb, new[] { ("", 6 * mb) });
            Assert.Equal("Image from clipboard was not attached: images in one prompt may total at most 20 MB.", total.Rejected.Single());
        }

        [Fact]
        public void Compose_builds_the_prompt_layout_omp_expects_and_collects_images()
        {
            var attachments = new Attachment[]
            {
                new FileAttachment("C:\\repo\\src\\a.cs"),
                new PastedTextAttachment("Pasted text 1", "x"),
                new ImageAttachment("Image 1", "QUJD", "image/png", 3),
                new FileAttachment("D:\\other dir\\b.cs"),
            };
            var prompt = Attachments.Compose("fix it", attachments, "C:\\repo");
            Assert.Equal("fix it\n\n<pasted-text name=\"Pasted text 1\">\nx\n</pasted-text>\n\nAttached: @src\\a.cs @\"D:\\other dir\\b.cs\"", prompt.Message);
            Assert.Equal("QUJD", prompt.Images.Single().Data);
            Assert.Equal("image/png", prompt.Images.Single().MimeType);
            Assert.Equal("hi", Attachments.Compose("hi", new Attachment[0], "C:\\repo").Message);
        }
    }
}
