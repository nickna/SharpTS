using System.Collections;
using SharpTS.Compilation.Emitters.Modules;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ModuleExportCatalogTests
{
    [Theory]
    [MemberData(nameof(BuiltInModuleEmitterRegistryTests.DefaultModules), MemberType = typeof(BuiltInModuleEmitterRegistryTests))]
    public void ExportListsCannotChangeAnotherCompilerStrategy(string key, Type expectedType)
    {
        var first = BuiltInModuleEmitterRegistry.CreateDefault();
        var second = BuiltInModuleEmitterRegistry.CreateDefault();
        var emitter = first.GetEmitter(key)!;
        Assert.IsType(expectedType, emitter);
        var exports = emitter.GetExportedMembers();
        var expected = exports.ToArray();
        Assert.NotEmpty(expected);
        var list = Assert.IsAssignableFrom<IList<string>>(exports);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = "__corrupted_export__");
        Assert.Throws<NotSupportedException>(() => list.Add("__injected_export__"));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(list.Clear);
        var untyped = Assert.IsAssignableFrom<IList>(exports);
        Assert.Throws<NotSupportedException>(() => untyped[0] = "__corrupted_export__");
        Assert.Equal(expected, emitter.GetExportedMembers());
        Assert.Equal(expected, second.GetEmitter(key)!.GetExportedMembers());
        Assert.Same(exports, emitter.GetExportedMembers());
        Assert.Same(exports, second.GetEmitter(key)!.GetExportedMembers());
    }

    [Fact]
    public void HttpsKeepsHttpExportOrderAndPropertyClassification()
    {
        var http = new HttpModuleEmitter();
        var https = new HttpsModuleEmitterProxy();
        Assert.Same(http.GetExportedMembers(), https.GetExportedMembers());
        foreach (var name in http.GetExportedMembers())
            Assert.Equal(http.IsExportedProperty(name), https.IsExportedProperty(name));
    }

    [Theory]
    [InlineData("isPrimary")]
    [InlineData("isWorker")]
    [InlineData("isMaster")]
    [InlineData("workers")]
    [InlineData("worker")]
    [InlineData("settings")]
    [InlineData("schedulingPolicy")]
    [InlineData("SCHED_NONE")]
    [InlineData("SCHED_RR")]
    public void ClusterPropertiesRemainOrdinalAndOnlySchedulingPolicyIsLive(string name)
    {
        var emitter = new ClusterModuleEmitter();
        Assert.True(emitter.IsExportedProperty(name));
        Assert.False(emitter.IsExportedProperty(name + "!"));
        Assert.False(emitter.IsExportedProperty("fork"));
        Assert.Equal(name == "schedulingPolicy", emitter.HasLivePropertyGet(name));
    }
}
