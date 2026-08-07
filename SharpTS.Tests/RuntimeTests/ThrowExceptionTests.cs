using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

public sealed class ThrowExceptionTests
{
    [Fact]
    public void Message_UsesInheritedConstructorForErrorLikeObjects()
    {
        var constructor = new SharpTSClass(
            "Test262Error", null, [], [], []);
        var prototype = new SharpTSObject([]);
        prototype.SetProperty("constructor", constructor);
        var instance = new SharpTSObject([]) { Prototype = prototype };
        instance.SetProperty("message", "assertion failed");

        var exception = new ThrowException(instance);

        Assert.Equal("Test262Error: assertion failed", exception.Message);
    }
}
