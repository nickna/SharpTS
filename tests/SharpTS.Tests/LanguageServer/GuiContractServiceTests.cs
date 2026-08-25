using SharpTS.LanguageServer.Services;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class GuiContractServiceTests
{
    private readonly GuiContractService _service = new();
    private readonly string _document = Path.Combine(
        FindRoot(), "tests", "fixtures", "SharpTS.Gui.Sdk.Consumer", "main.tsx");

    [Fact]
    public void CompletesTagsPropsEventsRefsAndEnumValues()
    {
        var tags = _service.Completion(_document, "const view = <Te", 0, 16);
        Assert.Contains(tags!.Items, item => item.Label == "TextBlock");

        var props = _service.Completion(_document, "const view = <Button ", 0, 21);
        Assert.Contains(props!.Items, item => item.Label == "ref");
        Assert.Contains(props.Items, item => item.Label == "onClick");
        Assert.Contains(props.Items, item => item.Label == "gridRow");

        var values = _service.Completion(_document, "const view = <StackPanel orientation=\"h", 0, 39);
        Assert.Contains(values!.Items, item => item.Label == "horizontal");
        Assert.DoesNotContain(values.Items, item => item.Label == "vertical");
    }

    [Fact]
    public void UsesGeneratedDocumentationAndDeclarationForHoverAndDefinition()
    {
        const string source = "const view = <Button onClick={() => {}}>ok</Button>;";
        var hover = _service.Hover(_document, source, 0, source.IndexOf("Button", StringComparison.Ordinal) + 2);
        Assert.Contains("Clickable", hover!.Contents.MarkupContent!.Value, StringComparison.Ordinal);

        var definition = _service.Definition(_document, source, 0, source.IndexOf("Button", StringComparison.Ordinal) + 2);
        Assert.NotNull(definition);
        Assert.EndsWith("control-surface.generated.ts", definition!.Uri.GetFileSystemPath(), StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        for (string? directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
            if (File.Exists(Path.Combine(directory, "SharpTS.sln"))) return directory;
        throw new InvalidOperationException("Could not locate SharpTS.sln.");
    }
}
