using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace OhMyPi.VisualStudio.Tests;

/// <summary>The command table and the ids the package registers handlers for must agree, or commands silently do nothing.</summary>
public class VsctTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable";
    private static readonly XDocument Table = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "OhMyPi.VisualStudio", "OmpPackage.vsct"));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OhMyPi.sln"))) return directory.FullName;
        }
        throw new InvalidOperationException("OhMyPi.sln not found above " + AppContext.BaseDirectory);
    }

    private static XElement GuidSymbol(string name) =>
        Table.Descendants(Ns + "GuidSymbol").Single(symbol => (string)symbol.Attribute("name")! == name);

    private static Guid GuidOf(string name) => new(GuidSymbol(name).Attribute("value")!.Value);

    private static Dictionary<string, int> CommandSetIds() =>
        GuidSymbol("guidOmpCommandSet").Elements(Ns + "IDSymbol").ToDictionary(
            symbol => (string)symbol.Attribute("name")!,
            symbol => int.Parse(((string)symbol.Attribute("value")!).Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static Dictionary<string, int> PackageIdConstants() =>
        typeof(PackageIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!);

    /// <summary>Parents of <paramref name="id"/> in guidSHLMainMenu, from its own definition and its placements.</summary>
    private static IEnumerable<string> ShellParentsOf(string id) =>
        Table.Descendants()
            .Where(element => (string?)element.Attribute("guid") == "guidOmpCommandSet" && (string?)element.Attribute("id") == id)
            .Elements(Ns + "Parent")
            .Where(parent => (string)parent.Attribute("guid")! == "guidSHLMainMenu")
            .Select(parent => (string)parent.Attribute("id")!);

    [Fact]
    public void GuidsMatchThePackage()
    {
        Assert.Equal(new Guid(PackageGuids.PackageString), GuidOf("guidOmpPackage"));
        Assert.Equal(PackageGuids.CommandSet, GuidOf("guidOmpCommandSet"));
        Assert.Equal(PackageGuids.Images, GuidOf("guidOmpImages"));
    }

    [Fact]
    public void EveryPackageIdIsDefinedWithTheSameValue()
    {
        var symbols = CommandSetIds();
        foreach (var constant in PackageIdConstants())
        {
            var found = symbols.TryGetValue("cmdid" + constant.Key, out var value) || symbols.TryGetValue(constant.Key, out value);
            Assert.True(found, $"{constant.Key} is not in OmpPackage.vsct");
            Assert.True(constant.Value == value, $"{constant.Key} is 0x{constant.Value:X4} in PackageIds and 0x{value:X4} in OmpPackage.vsct");
        }
    }

    [Fact]
    public void EveryCommandHasAPackageId()
    {
        var constants = PackageIdConstants();
        var commands = Table.Descendants(Ns + "Button").Select(button => (string)button.Attribute("id")!);
        foreach (var command in commands)
        {
            Assert.True(command.StartsWith("cmdid", StringComparison.Ordinal) && constants.ContainsKey(command.Substring(5)), $"{command} has no PackageIds constant");
        }
    }

    [Fact]
    public void AddFilesIsOfferedOnFilesFoldersProjectsAndMultipleSelections()
    {
        Assert.Equal(
            new[] { "IDM_VS_CTXT_FOLDERNODE", "IDM_VS_CTXT_ITEMNODE", "IDM_VS_CTXT_PROJNODE", "IDM_VS_CTXT_XPROJ_MULTIITEM" },
            ShellParentsOf("SolutionExplorerGroup").OrderBy(id => id, StringComparer.Ordinal));
    }
}
