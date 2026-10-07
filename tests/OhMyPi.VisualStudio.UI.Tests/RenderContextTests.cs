using System;
using System.IO;
using Omp.Core.Changes;
using OhMyPi.VisualStudio.UI.Views;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class RenderContextTests
    {
        private static RenderContext Context(string cwd) =>
            new RenderContext(new OpenState(), (_, __) => { }, (_, __) => { }, () => { }, _ => { }, _ => { }, (_, __) => { }) { Cwd = cwd };

        [Fact]
        public void A_missing_file_is_probed_once_until_misses_are_forgotten()
        {
            var root = Path.Combine(Path.GetTempPath(), "omp-ui-ctx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var ctx = Context(root);
                Assert.Null(ctx.Links.ResolveFile("New.cs"));
                File.WriteAllText(Path.Combine(root, "New.cs"), "class New {}");
                Assert.Null(ctx.Links.ResolveFile("New.cs"));
                ctx.ForgetMissingFiles();
                Assert.Equal(Path.Combine(root, "New.cs"), ctx.Links.ResolveFile("New.cs")?.Path);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Theory]
        [InlineData("src/a.cs", true)]
        [InlineData(@"SRC\A.CS", true)]
        [InlineData(@"C:\repo\src\a.cs", true)]
        [InlineData("src/b.cs", false)]
        [InlineData("https://example.com/src/a.cs", false)]
        public void Tool_paths_are_tracked_when_they_resolve_to_a_changed_file(string raw, bool tracked)
        {
            var ctx = Context(@"C:\repo");
            ctx.Changes = new[] { new TrackedChange { Path = @"C:\repo\src\a.cs" } };
            Assert.Equal(tracked, ctx.IsTracked(raw));
        }
    }
}
