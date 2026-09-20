using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ExternalTypeRegistryTests
{
    [Fact]
    public void AliasesShareOneProtectedLiveViewAndRetainSourceOrder()
    {
        var registry = new ExternalTypeRegistry();
        var view = registry.Types;
        registry.DeclareOrReplace("Alias", typeof(string));
        registry.DeclareOrReplace("Qualified.Alias", typeof(string));
        Assert.Same(typeof(string), view["Alias"]);
        registry.DeclareOrReplace("Alias", typeof(int));
        Assert.Same(typeof(int), view["Alias"]);
        Assert.Same(typeof(string), view["Qualified.Alias"]);
        Assert.False(view.ContainsKey("alias"));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, Type>)view).Add("Injected", typeof(object)));
        registry.CompleteDeclarations();
        Assert.Same(view, registry.Types);
        Assert.Throws<InvalidOperationException>(() => registry.DeclareOrReplace("Alias", typeof(bool)));
        Assert.Throws<InvalidOperationException>(() => registry.DeclareOrReplace("New", typeof(bool)));
        Assert.Throws<InvalidOperationException>(registry.CompleteDeclarations);
        Assert.Same(typeof(int), view["Alias"]);
    }

    [Fact]
    public void HintsAreCopiedMergedAndSurviveAliasReplacement()
    {
        var registry = new ExternalTypeRegistry();
        registry.DeclareOrReplace("Host", typeof(string));
        var hints = new Dictionary<string, string> { ["constructor"] = "char[]", ["Read"] = "int" };
        registry.RegisterOverloadHints(typeof(string), hints);
        hints["Read"] = "mutated";
        registry.RegisterOverloadHints(typeof(string), new Dictionary<string, string> { ["Write"] = "string" });
        Assert.Equal("int", registry.GetOverloadHint(typeof(string), "Read"));
        registry.RegisterOverloadHints(typeof(string), new Dictionary<string, string> { ["Read"] = "long" });
        registry.DeclareOrReplace("Host", typeof(int));
        registry.CompleteDeclarations();
        Assert.Equal("char[]", registry.GetOverloadHint(typeof(string), "constructor"));
        Assert.Equal("long", registry.GetOverloadHint(typeof(string), "Read"));
        Assert.Equal("string", registry.GetOverloadHint(typeof(string), "Write"));
        Assert.Null(registry.GetOverloadHint(typeof(string), "read"));
        Assert.Null(registry.GetOverloadHint(typeof(int), "Read"));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterOverloadHints(typeof(string), hints));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterOverloadHints(typeof(string), new Dictionary<string, string>()));
    }

    [Fact]
    public void InvalidDeclarationsCannotPartiallyChangeExistingMetadata()
    {
        var registry = new ExternalTypeRegistry();
        registry.DeclareOrReplace("Host", typeof(string));
        registry.RegisterOverloadHints(typeof(string), new Dictionary<string, string> { ["Read"] = "int" });
        Assert.Throws<ArgumentNullException>(() => registry.DeclareOrReplace("Host", null!));
        Assert.Throws<ArgumentException>(() => registry.DeclareOrReplace("", typeof(int)));
        Assert.Throws<ArgumentNullException>(() => registry.RegisterOverloadHints(typeof(string),
            new Dictionary<string, string> { ["Read"] = "changed", ["Bad"] = null! }));
        Assert.Equal("int", registry.GetOverloadHint(typeof(string), "Read"));
        Assert.Null(registry.GetOverloadHint(typeof(string), "Bad"));
        Assert.Same(typeof(string), registry.Types["Host"]);
        Assert.False(registry.IsComplete);
        registry.CompleteDeclarations();
        Assert.True(registry.IsComplete);
    }

    [Fact]
    public void EmptyCompletionAndSeparateMappersDoNotShareDeclarations()
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("ExternalMetadata_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Main");
        var first = new TypeMapper(module);
        var second = new TypeMapper(module);
        first.RegisterExternalType("Host", typeof(string));
        first.RegisterOverloadHints(typeof(string), new Dictionary<string, string> { ["Read"] = "int" });
        Assert.Same(first.ExternalTypeDeclarations.Types, first.ExternalTypes);
        Assert.Empty(second.ExternalTypes);
        Assert.Null(second.GetOverloadHint(typeof(string), "Read"));
        second.ExternalTypeDeclarations.CompleteDeclarations();
        Assert.True(second.ExternalTypeDeclarations.IsComplete);
        Assert.Throws<InvalidOperationException>(() => second.RegisterExternalType("Host", typeof(int)));
        first.RegisterExternalType("Other", typeof(int));
        first.ExternalTypeDeclarations.CompleteDeclarations();
        Assert.Equal("int", first.GetOverloadHint(typeof(string), "Read"));
        Assert.Equal(2, first.ExternalTypes.Count);
    }
}
