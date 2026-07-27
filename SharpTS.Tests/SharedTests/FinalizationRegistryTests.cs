using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the FinalizationRegistry built-in type.
/// </summary>
public class FinalizationRegistryTests
{
    [Theory, ModeData]
    public void FinalizationRegistry_CreateAndRegister(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const registry = new FinalizationRegistry((heldValue: any) => {
                console.log('cleaned: ' + heldValue);
            });
            const obj = { name: 'test' };
            registry.register(obj, 'my-held-value');
            console.log('registered');
            """, mode);
        Assert.Contains("registered", result);
    }

    [Theory, ModeData]
    public void FinalizationRegistry_Unregister_ReturnsTrue(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const registry = new FinalizationRegistry((heldValue: any) => {});
            const obj = { name: 'test' };
            const token = { id: 1 };
            registry.register(obj, 'value', token);
            const removed = registry.unregister(token);
            console.log(removed);
            """, mode);
        Assert.Contains("true", result);
    }

    [Theory, ModeData]
    public void FinalizationRegistry_Unregister_UnknownToken_ReturnsFalse(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const registry = new FinalizationRegistry((heldValue: any) => {});
            const obj = { name: 'test' };
            const token = { id: 1 };
            registry.register(obj, 'value', token);
            const unknownToken = { id: 2 };
            const removed = registry.unregister(unknownToken);
            console.log(removed);
            """, mode);
        Assert.Contains("false", result);
    }

    [Theory, ModeData]
    public void FinalizationRegistry_Typeof(ExecutionMode mode)
    {
        var result = TestHarness.Run("""
            const registry = new FinalizationRegistry((v: any) => {});
            console.log(typeof registry === 'object');
            """, mode);
        Assert.Contains("true", result);
    }

    [Theory, ModeData]
    public void FinalizationRegistry_Constructor_RequiresCallback(ExecutionMode mode)
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            TestHarness.Run("""
                const registry = new FinalizationRegistry();
                """, mode);
        });
        Assert.Contains("FinalizationRegistry", ex.Message);
    }
}
