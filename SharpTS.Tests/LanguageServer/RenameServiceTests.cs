using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public class RenameServiceTests
{
    [Fact]
    public void CompleteWorkspaceRenameEditsDeclarationsAliasesAndClosedImporters()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile(
            "tsconfig.json",
            """{ "include": ["**/*.ts"] }""");
        string dependencyPath = directory.CreateFile(
            "dependency.ts",
            "export const original = 1;\n");
        string importerPath = directory.CreateFile(
            "importer.ts",
            """
            import { original as local } from "./dependency";
            console.log(local);
            local;
            """);
        string dependency = File.ReadAllText(dependencyPath);

        WorkspaceEdit? edit = Service().Rename(
            dependencyPath,
            dependency,
            PositionOf(dependency, "original"),
            "renamed",
            new Dictionary<string, string> { [dependencyPath] = dependency },
            [directory.Path]);

        Assert.NotNull(edit);
        Assert.NotNull(edit.Changes);
        Assert.Equal(2, edit.Changes.Count);
        Assert.Equal(
            [new Range(0, 13, 0, 21)],
            edit.Changes[DocumentUri.FromFileSystemPath(dependencyPath)]
                .Select(textEdit => textEdit.Range)
                .ToArray());
        Assert.Equal(
            [
                new Range(2, 0, 2, 5),
                new Range(1, 12, 1, 17),
                new Range(0, 21, 0, 26),
                new Range(0, 9, 0, 17),
            ],
            edit.Changes[DocumentUri.FromFileSystemPath(importerPath)]
                .Select(textEdit => textEdit.Range)
                .ToArray());
        Assert.All(
            edit.Changes.Values.SelectMany(edits => edits),
            textEdit => Assert.Equal("renamed", textEdit.NewText));
    }

    [Fact]
    public void RenameRefusesAnIncompleteWorkspace()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile(
            "project/value.ts",
            "export const value = 1;\nvalue;\n");
        directory.CreateFile(
            "project/tsconfig.json",
            """{ "include": ["*.ts"] }""");
        directory.CreateFile("broken/tsconfig.json", "{ not json");
        string source = File.ReadAllText(path);

        WorkspaceEdit? edit = Service().Rename(
            path,
            source,
            PositionOf(source, "value"),
            "renamed",
            new Dictionary<string, string> { [path] = source },
            [directory.Path]);

        Assert.Null(edit);
    }

    [Fact]
    public void RenameRefusesOpenDocumentFallbackWithoutAConfig()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.GetPath("value.ts");
        const string source = "const value = 1;\nvalue;\n";

        WorkspaceEdit? edit = Service().Rename(
            path,
            source,
            PositionOf(source, "value"),
            "renamed",
            new Dictionary<string, string> { [path] = source },
            [directory.Path]);

        Assert.Null(edit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("class")]
    [InlineData("value; other")]
    [InlineData("123name")]
    public void RenameRefusesInvalidBindingIdentifiers(string newName)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """{ "include": ["*.ts"] }""");
        string path = directory.CreateFile(
            "value.ts",
            "const value = 1;\nvalue;\n");
        string source = File.ReadAllText(path);

        WorkspaceEdit? edit = Service().Rename(
            path,
            source,
            PositionOf(source, "value"),
            newName,
            new Dictionary<string, string> { [path] = source },
            [directory.Path]);

        Assert.Null(edit);
    }

    [Fact]
    public void PrepareRenameReturnsOnlyACompleteBoundToken()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """{ "include": ["*.ts"] }""");
        string path = directory.CreateFile(
            "value.ts",
            "const value = 1;\nvalue;\n");
        string source = File.ReadAllText(path);
        var service = Service();

        Range? range = service.Prepare(
            path,
            source,
            PositionOf(source, "value", occurrence: 1),
            new Dictionary<string, string> { [path] = source },
            [directory.Path]);
        Range? whitespace = service.Prepare(
            path,
            source,
            new Position(0, 5),
            new Dictionary<string, string> { [path] = source },
            [directory.Path]);

        Assert.Equal(new Range(1, 0, 1, 5), range);
        Assert.Null(whitespace);
    }

    [Fact]
    public async Task HandlersUseTheInitializedWorkspaceCompletenessBoundary()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """{ "include": ["*.ts"] }""");
        string path = directory.CreateFile(
            "value.ts",
            "const value = 1;\nvalue;\n");
        string source = File.ReadAllText(path);
        DocumentUri uri = DocumentUri.FromFileSystemPath(path);
        var store = new DocumentStore();
        store.Set(uri.ToString(), source);
        var workspace = new NavigationWorkspaceContext();
        workspace.Initialize(new InitializeParams
        {
            RootUri = DocumentUri.FromFileSystemPath(directory.Path),
        });
        var references = new ReferenceService();
        var rename = new RenameService(references);

        var prepared = await new PrepareRenameHandler(
            store,
            rename,
            workspace).Handle(
            new PrepareRenameParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = PositionOf(source, "value", occurrence: 1),
            },
            CancellationToken.None);
        WorkspaceEdit? edit = await new RenameHandler(
            store,
            rename,
            workspace).Handle(
            new RenameParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = PositionOf(source, "value", occurrence: 1),
                NewName = "renamed",
            },
            CancellationToken.None);

        Assert.NotNull(prepared);
        Assert.NotNull(edit);
        Assert.NotNull(edit.Changes);
        Assert.Equal(
            2,
            Assert.Single(edit.Changes).Value.Count());
    }

    private static RenameService Service() =>
        new(new ReferenceService());

    private static Position PositionOf(
        string source,
        string text,
        int occurrence = 0)
    {
        int offset = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            offset = source.IndexOf(
                text,
                offset + 1,
                StringComparison.Ordinal);
            Assert.True(offset >= 0);
        }

        string before = source[..offset];
        string[] lines = before.Split('\n');
        return new Position(lines.Length - 1, lines[^1].Length);
    }
}
