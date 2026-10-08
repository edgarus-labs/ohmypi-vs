using OhMyPi.VisualStudio.UI.Model;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class CodeHighlighterTests
{
    private static string Dump(string code, string? language) =>
        string.Join("|", CodeHighlighter.Tokens(code, language).Where(t => t.Kind != CodeTokenKind.Text).Select(t => $"{t.Kind}:{code.Substring(t.Start, t.Length)}"));

    [Theory]
    [InlineData("cs", "csharp")]
    [InlineData("C#", "csharp")]
    [InlineData("ts", "typescript")]
    [InlineData("py", "python")]
    [InlineData("sh", "bash")]
    [InlineData("ps1", "powershell")]
    [InlineData("xaml", "xml")]
    [InlineData("yml", "yaml")]
    [InlineData("json {.cls}", "json")]
    [InlineData("text", null)]
    [InlineData("diff", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("brainfuck", null)]
    public void Fence_labels_map_to_a_language_family_or_nothing(string? label, string? family) => Assert.Equal(family, CodeHighlighter.Normalize(label));

    [Theory]
    [InlineData("{\"a\":1}", "json")]
    [InlineData("  [1, 2]", "json")]
    [InlineData("<Project Sdk=\"x\" />", "xml")]
    [InlineData("< 5 items left", null)]
    [InlineData("[INFO] Compiling src/a.cs:12", null)]
    [InlineData("[1/4] Resolving packages...", null)]
    [InlineData("[12:00:01] wrote src/Foo.cs", null)]
    [InlineData("{\n  \"a\": 1,\n  \"b\": [", null)]
    [InlineData("[Fact]\npublic void M() { }", "csharp")]
    [InlineData("plain text", null)]
    [InlineData("23\n    internal void M()\n    {", "csharp")]
    [InlineData("using Projects;\n\nvar b = 1;", "csharp")]
    [InlineData("import json\nprint(1)", "python")]
    [InlineData("const vs = await x();", "javascript")]
    [InlineData("import x from \"y\";", "javascript")]
    [InlineData("exit 0\nerror: not found", null)]
    [InlineData("", null)]
    public void Unlabeled_code_is_guessed_from_its_json_or_xml_shape_or_a_telltale_line(string text, string? language) => Assert.Equal(language, CodeHighlighter.GuessLanguage(text));

    [Fact]
    public void Tokens_cover_the_whole_text_in_order_without_gaps()
    {
        const string code = "var x = \"a\"; // c\nreturn 1.5;";
        var tokens = CodeHighlighter.Tokens(code, "cs");
        Assert.Equal(0, tokens[0].Start);
        Assert.Equal(code.Length, tokens.Sum(t => t.Length));
        for (var i = 1; i < tokens.Count; i++)
        {
            Assert.Equal(tokens[i - 1].Start + tokens[i - 1].Length, tokens[i].Start);
        }

        Assert.Empty(CodeHighlighter.Tokens("", "cs"));
    }

    [Fact]
    public void Json_colors_keys_apart_from_string_values_and_survives_a_cut_or_single_quoted_text()
    {
        Assert.Equal("Key:\"a\"|Number:-1.5e3|Key:\"b\"|String:\"x:y\"|Key:\"c\"|Keyword:true|Keyword:null|String:\"q\"", Dump("{\n  \"a\": -1.5e3,\n  \"b\": \"x:y\",\n  \"c\": [true, null, \"q\"]\n}", "json"));
        Assert.Equal("Key:\"title\"|String:\"Undo w ca", Dump("{\"title\": \"Undo w ca", "json"));
        Assert.Equal("Key:\"p\"|String:\"a\\\"b\"", Dump("{\"p\":\"a\\\"b\"}", "json"));
        Assert.Equal("Key:'text'|String:'{\"op\"}'", Dump("{'text': '{\"op\"}'}", "json"));
    }

    [Fact]
    public void C_like_languages_color_comments_strings_numbers_and_keywords()
    {
        Assert.Equal("Keyword:var|String:\"a\"|Comment:// c|Keyword:return|Number:1.5|Keyword:return|Number:0x1F|Comment:/* b\nc */", Dump("var x = \"a\"; // c\nreturn 1.5; items2 return 0x1F /* b\nc */", "cs"));
        Assert.Equal("Keyword:const|String:`t ${x}`|Keyword:await", Dump("const s = `t ${x}`; await f()", "ts"));
        Assert.Equal("Keyword:def|String:\"\"\"doc\nstring\"\"\"|Keyword:return|Keyword:None|Comment:# done", Dump("def f():\n    \"\"\"doc\nstring\"\"\"\n    return None # done", "py"));
    }

    [Fact]
    public void Shells_and_sql_color_their_own_comments_and_keywords_case_insensitively()
    {
        Assert.Equal("Keyword:if|String:\"$x\"|Keyword:then|Keyword:echo|Keyword:fi|Comment:# note", Dump("if [ -f \"$x\" ]; then echo ok; fi # note", "bash"));
        Assert.Equal("Keyword:SELECT|Keyword:from|Keyword:WHERE|Number:3|Comment:-- c", Dump("SELECT a from t WHERE b = 3 -- c", "sql"));
        Assert.Equal("Comment:<# block #>|Keyword:Function|String:'x'", Dump("<# block #> Function F { 'x' }", "powershell"));
    }

    [Fact]
    public void Xml_colors_tags_attribute_names_and_values()
    {
        Assert.Equal("Comment:<!-- c -->|Tag:<Project|Key:Sdk|String:\"Microsoft.NET.Sdk\"|Tag:>|Tag:<Nullable|Tag:>|Tag:</Nullable|Tag:>|Tag:</Project|Tag:>", Dump("<!-- c --><Project Sdk=\"Microsoft.NET.Sdk\"><Nullable>enable</Nullable></Project>", "xml"));
        Assert.Equal("Tag:<?xml|Key:version|String:\"1.0\"|Tag:?>|Tag:<a|Key:href|String:'x'|Tag:/>", Dump("<?xml version=\"1.0\"?><a href='x'/>", "html"));
    }

    [Fact]
    public void Yaml_colors_keys_at_line_start_but_not_values_or_urls()
    {
        Assert.Equal("Key:name|Key:on|Keyword:true|Key:url|Comment:# c|Key:steps|Key:run", Dump("name: Build\non: true\nurl: http://x\n# c\nsteps:\n  - run: dotnet test", "yaml"));
    }

    [Fact]
    public void Yaml_quotes_open_strings_only_at_the_start_of_a_scalar()
    {
        Assert.Equal("Key:description|Key:name|String:'x'|Key:items|String:\"y\"|String:'z'", Dump("description: Check the user's config\nname: 'x'\nitems:\n  - \"y\"\n  - [a, 'z']", "yaml"));
    }

    [Fact]
    public void A_yaml_quoted_scalar_after_a_tag_or_anchor_is_a_string()
    {
        var tokens = Dump("Value: !Sub \"arn:${Bucket} #1\"\nname: &n 'x'\nplain: ! \"a #2\"", "yaml");
        Assert.Contains("String:\"arn:${Bucket} #1\"", tokens);
        Assert.Contains("String:'x'", tokens);
        Assert.Contains("String:\"a #2\"", tokens);
        Assert.DoesNotContain("Comment", tokens);
    }

    [Theory]
    [InlineData("msg: 'Don''t do it'", "yaml", "Key:msg|String:'Don''t do it'")]
    [InlineData("$a = 'it''s'", "powershell", "String:'it''s'")]
    public void A_doubled_quote_stays_inside_a_single_quoted_string(string code, string language, string expected) =>
        Assert.Contains(expected, Dump(code, language));

    [Fact]
    public void Hash_comments_start_only_at_line_start_or_after_whitespace()
    {
        Assert.Equal("Key:pattern|Key:url", Dump("pattern: C#|F#\nurl: https://x/#/home", "yaml"));
        Assert.Equal("Comment:# real comment|Key:key|Comment:# c", Dump("# real comment\nkey: v # c", "yaml"));
        Assert.Equal("Keyword:curl|Number:5000", Dump("curl http://localhost:5000/#/home", "bash"));
        Assert.Equal("Comment:# real comment|Keyword:ls|Comment:# c", Dump("# real comment\nls x # c", "bash"));
    }

    [Fact]
    public void Ini_files_color_keys_before_equals_and_semicolon_or_hash_comments()
    {
        Assert.Equal("Comment:; comment|Key:indent_style|Key:ASPNETCORE_ENVIRONMENT|Key:version|String:\"1.0\"|Comment:# c|Key:pattern", Dump("; comment\nindent_style = space\nASPNETCORE_ENVIRONMENT=Development\nversion = \"1.0\" # c\npattern = C#", "ini"));
    }

    [Fact]
    public void Backslashes_escape_quotes_only_in_strings_that_have_escapes()
    {
        Assert.Equal("Tag:<Copy|Key:DestinationFolder|String:\"$(OutDir)\\\"|Tag:/>|Tag:<Next|Tag:/>", Dump("<Copy DestinationFolder=\"$(OutDir)\\\" />\n<Next />", "xml"));
        Assert.Equal("Keyword:cd|String:'D:\\dev\\'|Keyword:ls", Dump("cd 'D:\\dev\\'; ls", "bash"));
        Assert.Equal("Keyword:echo|String:\"a\\\"b\"", Dump("echo \"a\\\"b\"", "bash"));
        Assert.Contains("String:'it\\'s done'", Dump("echo $'it\\'s done'", "bash"));
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("log")]
    public void Plain_text_labels_get_no_coloring(string label)
    {
        const string text = "don't retry 42 times";
        var token = Assert.Single(CodeHighlighter.Tokens(text, label));
        Assert.Equal(CodeTokenKind.Text, token.Kind);
        Assert.Equal(text.Length, token.Length);
    }

    [Fact]
    public void An_unknown_language_gets_strings_and_numbers_only()
    {
        Assert.Equal("String:\"x\"|Number:42", Dump("if \"x\" // not a comment 42", null));
        Assert.Equal("String:\"x\"|Number:42", Dump("if \"x\" // not a comment 42", "brainfuck"));
    }

    [Fact]
    public void Shells_color_command_names_and_variables()
    {
        Assert.Equal("Keyword:docker|String:\"x\"|Keyword:grep|Keyword:echo|Key:$HOME|Key:${USER}|Comment:# c", Dump("docker ps --format \"x\" | grep -i y && echo $HOME ${USER}x # c", "bash"));
        Assert.Equal("Keyword:if|Keyword:./run.sh|Keyword:then|Keyword:sudo|Keyword:make|Keyword:fi", Dump("if ./run.sh; then sudo make all; fi", "bash"));
        Assert.Equal("Keyword:Get-Process|Keyword:Where-Object|Key:$_|Number:1", Dump("Get-Process -Name x | Where-Object { $_.Id -gt 1 }", "pwsh"));
        Assert.Equal("Key:$dte|String:\"VisualStudio.DTE.18.0\"|Key:$env:TEMP", Dump("$dte = [Runtime.InteropServices.Marshal]::GetActiveObject(\"VisualStudio.DTE.18.0\"); $env:TEMP", "powershell"));
    }
}
