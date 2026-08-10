using SharpTS.Cli;
using SharpTS.References;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.CliTests;

public sealed class GuiApplicationCliTests
{
    [Fact]
    public void Create_WritesEscapedPinnedApplicationContract()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string root = directory.GetPath("quoted app");

        Assert.Equal(0, GuiApplicationCli.Create(new ParsedCommand.NewAvalonia(
            "A \"quoted\" app", root, "0.3.0-preview.1")));

        SharpTsApplication application = SharpTsManifestLoader.Load(
            Path.Combine(root, "sharpts.json")).Application!;
        Assert.Equal("avalonia", application.Type);
        Assert.Equal("main.tsx", application.Entry);
        Assert.Equal("0.3.0-preview.1", application.GuiSdkVersion);
        string source = File.ReadAllText(Path.Combine(root, "main.tsx"));
        Assert.Contains("title={\"A \\u0022quoted\\u0022 app\"}", source);
        Assert.Contains(">{\"A \\u0022quoted\\u0022 app\"}</TextBlock>", source);
        Assert.Contains("setTimeout((() => application.dispose()) as any, 0);",
            File.ReadAllText(Path.Combine(root, "headless.tests.tsx")));
    }

    [Fact]
    public void ResolveHost_UsesExplicitThenManifestThenConservativeInference()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.CreateFile("main.tsx", """
            // A comment mentioning @sharpts/gui is not an import.
            import { createElement } from "react";
            """);

        Assert.Equal("avalonia", GuiApplicationCli.ResolveHost(" AVALONIA ", "console", entry));
        Assert.Equal("console", GuiApplicationCli.ResolveHost(null, "console", entry));
        Assert.Equal("console", GuiApplicationCli.ResolveHost(null, null, entry));

        File.WriteAllText(entry, "import { Window } from \"@sharpts/gui\";");
        Assert.Equal("avalonia", GuiApplicationCli.ResolveHost(null, null, entry));
    }

    [Fact]
    public void ResolveHost_RejectsMultipleImportedJsxRuntimes()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.CreateFile("main.tsx", """
            import { Window } from "@sharpts/gui";
            import React from "react";
            """);

        var exception = Assert.Throws<InvalidOperationException>(
            () => GuiApplicationCli.ResolveHost(null, null, entry));
        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveHost_IgnoresCommentStringAndTypeOnlyFalsePositives()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.CreateFile("main.tsx", """
            /*
            import { Window } from "@sharpts/gui";
            */
            const example = `import { Window } from "@sharpts/gui";`;
            import type { Element } from "@sharpts/gui";
            """);

        Assert.Equal("console", GuiApplicationCli.ResolveHost(null, null, entry));
    }

    [Fact]
    public void ResolveHost_RejectsAlternativeJsxImportSourceWithGuiImport()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.CreateFile("main.tsx", """
            /** @jsxImportSource react */
            import { Window } from "@sharpts/gui";
            const view = <Window />;
            """);

        Assert.Throws<InvalidOperationException>(() => GuiApplicationCli.ResolveHost(null, null, entry));
    }

    [Fact]
    public void ResolvePackageSource_PreservesUrlsAndRootsPaths()
    {
        using var directory = CliTestHelper.CreateTempDirectory();

        Assert.Equal("https://packages.example.test/v3/index.json",
            GuiApplicationCli.ResolvePackageSource("https://packages.example.test/v3/index.json", directory.Path));
        Assert.Equal(Path.Combine(directory.Path, "feed"),
            GuiApplicationCli.ResolvePackageSource("feed", directory.Path));
    }

    [Fact]
    public void MaterializeProject_IsDeterministicAndIgnoresCSharpGlobs()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string root = directory.GetPath("app with spaces");
        Directory.CreateDirectory(root);
        string entry = Path.Combine(root, "main.tsx");
        File.WriteAllText(entry, "export {};\n");

        string project = GuiApplicationCli.MaterializeProject(root, entry, "0.3.0-preview.1");
        string first = File.ReadAllText(project);
        DateTime firstWrite = File.GetLastWriteTimeUtc(project);
        string repeated = GuiApplicationCli.MaterializeProject(root, entry, "0.3.0-preview.1");

        Assert.Equal(project, repeated);
        Assert.Equal(first, File.ReadAllText(repeated));
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(repeated));
        Assert.Contains("<AssemblyName>app_with_spaces</AssemblyName>", first);
        Assert.Contains("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>", first);
        Assert.DoesNotContain("BaseIntermediateOutputPath", first);
        Assert.Contains("SharpTS.Gui.Sdk/0.3.0-preview.1", first);
    }
}
