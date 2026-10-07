using OhMyPi.VisualStudio.UI.Model;
using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class FileLinksTests
{
    private static readonly HashSet<string> Existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\repo\src\AuthService.cs",
        @"C:\repo\README.md",
        @"D:\other\Tool.cs",
    };

    private static FileTarget? Resolve(string text) => FileLinks.Resolve(text, @"C:\repo", Existing.Contains);

    [Theory]
    [InlineData("src/AuthService.cs", @"C:\repo\src\AuthService.cs", null)]
    [InlineData(@"src\AuthService.cs", @"C:\repo\src\AuthService.cs", null)]
    [InlineData("src/AuthService.cs:42", @"C:\repo\src\AuthService.cs", 42)]
    [InlineData("src/AuthService.cs:42:7", @"C:\repo\src\AuthService.cs", 42)]
    [InlineData("src/AuthService.cs#L42", @"C:\repo\src\AuthService.cs", 42)]
    [InlineData("src/AuthService.cs#L42-L50", @"C:\repo\src\AuthService.cs", 42)]
    [InlineData("src/AuthService.cs (42)", null, null)]
    [InlineData("README.md", @"C:\repo\README.md", null)]
    [InlineData(@"D:\other\Tool.cs:3", @"D:\other\Tool.cs", 3)]
    [InlineData("file:///D:/other/Tool.cs", @"D:\other\Tool.cs", null)]
    [InlineData("./src/AuthService.cs", @"C:\repo\src\AuthService.cs", null)]
    public void Resolves_paths_with_optional_line_to_existing_files(string text, string? path, int? line)
    {
        var target = Resolve(text);
        Assert.Equal(path, target?.Path);
        Assert.Equal(line, target?.Line);
    }

    [Theory]
    [InlineData("see src/a.cs now", 6, "src/a.cs")]
    [InlineData("'src/a.cs:12',", 3, "src/a.cs:12")]
    [InlineData(@"(C:\x\a.cs)", 2, @"C:\x\a.cs")]
    [InlineData("open src/a.cs.", 7, "src/a.cs")]
    [InlineData("M  src/a.cs | 36 ++--", 5, "src/a.cs")]
    [InlineData("a  b", 1, null)]
    [InlineData("x | y", 2, null)]
    [InlineData("abc", 3, null)]
    public void Token_at_a_character_is_the_path_like_word_around_it(string text, int index, string? token) => Assert.Equal(token, FileLinks.TokenAt(text, index));

    [Theory]
    [InlineData("src/Missing.cs")]
    [InlineData("ValidateToken")]
    [InlineData("var x = 1;")]
    [InlineData("https://example.com/a.cs")]
    [InlineData("")]
    [InlineData("src/AuthService.cs and more")]
    public void Ignores_text_that_is_not_an_existing_file(string text) => Assert.Null(Resolve(text));

    [Fact]
    public void Never_touches_the_file_system_for_text_with_illegal_path_characters()
    {
        var probed = false;
        Assert.Null(FileLinks.Resolve("a<b>|c.cs", @"C:\repo", _ => probed = true));
        Assert.False(probed);
    }

    [Theory]
    [InlineData(@"\\attacker\share\x.txt")]
    [InlineData("//attacker/share/x.txt")]
    [InlineData("file://attacker/share/x.txt")]
    [InlineData(@"\\?\C:\repo\README.md")]
    [InlineData(@"\\.\C:\repo\README.md")]
    public void Never_probes_network_or_device_paths(string text)
    {
        var probed = new List<string>();
        Assert.Null(FileLinks.Resolve(text, @"C:\repo", path => { probed.Add(path); return true; }));
        Assert.DoesNotContain(probed, p => p.StartsWith(@"\\", StringComparison.Ordinal));
    }
}
