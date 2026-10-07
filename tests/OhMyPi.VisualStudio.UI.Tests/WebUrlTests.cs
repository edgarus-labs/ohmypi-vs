using OhMyPi.VisualStudio.UI.Views;

namespace OhMyPi.VisualStudio.UI.Tests
{
    /// <summary>Only http(s) URLs may reach the shell; everything else would run an arbitrary URI handler.</summary>
    public class WebUrlTests
    {
        [Theory]
        [InlineData("http://example.com")]
        [InlineData("https://example.com/a?b=c#d")]
        [InlineData("HTTPS://EXAMPLE.COM")]
        public void Accepts_http_and_https(string url) => Assert.True(MarkdownView.IsWebUrl(url));

        [Theory]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("javascript:alert(1)")]
        [InlineData("ms-msdt:/id PCWDiagnostic")]
        [InlineData("vscode://file/C:/x")]
        [InlineData("search-ms:query=x")]
        [InlineData("docs/readme.md")]
        [InlineData(@"\\server\share\x.exe")]
        [InlineData("//server/share")]
        [InlineData("")]
        public void Rejects_every_other_scheme_and_path(string url) => Assert.False(MarkdownView.IsWebUrl(url));

        [Fact]
        public void A_web_url_is_launched_in_its_normalized_form()
        {
            Assert.True(MarkdownView.TryWebUri("https://example.com/a b\"c", out var uri));
            Assert.Equal("https://example.com/a%20b%22c", uri!.AbsoluteUri);
        }
    }
}
