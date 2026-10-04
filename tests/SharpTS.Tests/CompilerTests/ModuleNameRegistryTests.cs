using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ModuleNameRegistryTests
{
    [Fact]
    public void PathOwnersRemainUniqueStableAndProtected()
    {
        string[] paths = ["left/index.ts", "right/index.ts", "index__0.ts", "a-b.ts", "a_b.ts", "main.ts"];
        var names = ModuleNameRegistry.Collect(paths);
        Assert.Equal(paths.Length, names.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("main", names["main.ts"]);
        Assert.Equal("index__0", names["index__0.ts"]);
        var reversed = ModuleNameRegistry.Collect(paths.Reverse());
        foreach (string path in paths)
            Assert.Equal(names[path], reversed[path]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)names).Clear());
        paths[0] = "changed.ts";
        Assert.True(names.ContainsKey("left/index.ts"));
        Assert.Empty(ModuleNameRegistry.Collect([]));
        Assert.Single(ModuleNameRegistry.Collect(["main.ts", "main.ts"]));
    }
}
