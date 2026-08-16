using SharpTS.Modules;
using SharpTS.Modules.Stdlib;
using SharpTS.Modules.Stdlib.Providers;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the Phase 3a primitive layer — the narrow C# interop surface that
/// stdlib TypeScript modules may consume, hidden from user code.
/// </summary>
public class PrimitiveModuleGatingTests
{
    [Fact]
    public void PrimitiveRegistry_RecognizesKnownPrimitives()
    {
        Assert.True(PrimitiveRegistry.IsPrimitive("primitive:os"));
        Assert.False(PrimitiveRegistry.IsPrimitive("os"));
        Assert.False(PrimitiveRegistry.IsPrimitive("primitive:"));
        Assert.False(PrimitiveRegistry.IsPrimitive("primitive:unknown"));
    }

    [Fact]
    public void PrimitiveRegistry_ExtractsBareName()
    {
        Assert.Equal("os", PrimitiveRegistry.GetPrimitiveName("primitive:os"));
        Assert.Null(PrimitiveRegistry.GetPrimitiveName("os"));
    }

    [Fact]
    public void PrimitiveProvider_ResolvesKnownPrimitive()
    {
        var provider = new PrimitiveProvider();
        Assert.True(provider.TryResolve("primitive:os", out var module));
        Assert.NotNull(module);
        Assert.Equal("primitive:os", module!.Specifier);
        Assert.Equal("primitive:os", module.VirtualPath);
        Assert.Equal("primitive", module.Origin);
        Assert.IsType<CSharpBuiltInSource>(module.Source);
    }

    [Fact]
    public void PrimitiveProvider_SkipsUserFacingSpecifiers()
    {
        var provider = new PrimitiveProvider();
        Assert.False(provider.TryResolve("os", out var module));
        Assert.Null(module);
    }

    [Fact]
    public void UserCode_CannotImportPrimitiveOs()
    {
        // Origin-gating: importing primitive:* from user code must fail with a
        // clear diagnostic mentioning the namespace. The check happens at
        // resolution time, before module loading.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { platform } from 'primitive:os';
                console.log(platform());
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));

        // Drill into inner exceptions — module resolution errors may be wrapped by
        // the interpreter's top-level executor.
        var message = FlattenMessage(ex);
        Assert.Contains("primitive:os", message);
        Assert.Contains("primitive:", message);
    }

    private static string FlattenMessage(Exception ex)
    {
        var parts = new List<string>();
        Exception? current = ex;
        while (current is not null)
        {
            parts.Add(current.Message);
            current = current.InnerException;
        }
        return string.Join(" | ", parts);
    }
}
