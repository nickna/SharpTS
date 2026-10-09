using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class EditorSemanticAnalysisTests
{
    [Fact]
    public async Task SharedSnapshotsPublishQueryValuesAndKeepAnOldCaptureStableAfterEdits()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json",
            """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts"]}""");
        const string source = "class Box { value: number = 1; } function echo<T>(input: T): T { return input; } " +
            "const box = new Box(); const answer = echo(42); box.value;";
        string path = directory.CreateFile("box.ts", source);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(path, source);
        using var repeated = await service.GetDocumentAsync(path, source);
        Assert.NotNull(first); Assert.NotNull(repeated);
        var model = first.Model;
        var facts = model.EditorFacts;
        Assert.Same(facts, repeated.Model.EditorFacts);
        Assert.True(facts.Count > 0); Assert.True(facts.EstimatedBytes > 0);
        int memberOffset = source.LastIndexOf("value", StringComparison.Ordinal);
        var member = model.Document.EditorSyntax!.FindMember(memberOffset);
        Assert.NotNull(member);
        var receiver = facts.GetReceiverMembers(model.Document, member.Receiver);
        Assert.Equal("number", Assert.Single(receiver.Members, candidate => candidate.Name == "value").Type.Text);
        var invocation = facts.FindInvocation(model.Document, source.LastIndexOf("echo(42)", StringComparison.Ordinal) + 5);
        Assert.NotNull(invocation);
        Assert.Equal(EditorInvocationStatus.Selected, invocation.Status);
        Assert.NotNull(invocation.SelectedSignature);
        var visible = facts.GetVisibleBindings(model.Document, memberOffset);
        Assert.Contains(visible, binding => binding.LocalName == "box" && binding.Availability == EditorFactAvailability.Available);

        const string prefix = "// changed capture\n";
        using var edited = await service.GetDocumentAsync(path, prefix + source);
        Assert.NotNull(edited);
        Assert.NotSame(facts, edited.Model.EditorFacts);
        Assert.NotEqual(facts.Generation, edited.Model.EditorFacts.Generation);
        Assert.Same(receiver, facts.GetReceiverMembers(model.Document, member.Receiver));
        Assert.Empty(edited.Model.EditorFacts.GetReceiverMembers(model.Document, member.Receiver).Members);
        Assert.Null(edited.Model.EditorFacts.GetInvocation(model.Document, invocation.Owner));
        Assert.Equal(EditorInvocationStatus.Selected, facts.GetInvocation(model.Document, invocation.Owner)!.Status);
        Assert.Equal(2, service.Statistics.Checks);
    }
}
