using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class QuotedArgumentsTests
{
    [Fact]
    public void ADoubledQuoteInsideQuotesIsALiteralQuote()
    {
        Assert.Equal(new[] { "a \"b\" c", "d" }, CommandLine.Split("\"a \"\"b\"\" c\" d"));
        Assert.Empty(CommandLine.Split(null));
    }
}
