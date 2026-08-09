using System.Reflection;
using SharpTS.Gui;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class ManagedInteropContractTests
{
    [Fact]
    public void ImplementationOnlyTypesAndDesktopRefConstructionAreNotExported()
    {
        Type[] exported = typeof(DesktopBridge).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exported, type => type.FullName == "SharpTS.Gui.ControlRef");
        Assert.DoesNotContain(exported, type => type.FullName == "SharpTS.Gui.GuiTraceEvent");
        Assert.DoesNotContain(exported, type => type.FullName == "SharpTS.Gui.TraceRecorder");
        Assert.Empty(typeof(DesktopRef).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void DesktopRootExportsOnlyTheWindowInteropContract()
    {
        Assert.Equal(
            ["Completion", "IsDisposed"],
            PublicProperties(typeof(DesktopRoot)));
        Assert.Equal(
            ["Activate", "Close", "Dispose", "Render"],
            PublicMethods(typeof(DesktopRoot)));
        Assert.Empty(typeof(DesktopRoot).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void DesktopApplicationSessionExportsOnlyTheLifecycleInteropContract()
    {
        Assert.Equal(
            ["IsDisposed", "WindowCount"],
            PublicProperties(typeof(DesktopApplicationSession)));
        Assert.Equal(
            ["Dispose", "Shutdown"],
            PublicMethods(typeof(DesktopApplicationSession)));
        Assert.Empty(typeof(DesktopApplicationSession).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    private static string[] PublicProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] PublicMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
