using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests that <c>private</c>/<c>protected</c> visibility is enforced for static
/// methods accessed as <c>Class.method</c>, and that static and instance methods
/// sharing a name keep independent access modifiers (issue #722). Static methods
/// carry their own access map (StaticMethodAccess) distinct from instance methods,
/// so a same-named pair no longer overwrite each other's visibility.
/// Runs in both interpreter and compiled modes (type checking is shared).
/// </summary>
public class StaticMethodAccessTests
{
    [Theory, ModeData]
    public void PrivateStaticMethod_ExternalAccess_Errors(ExecutionMode mode)
    {
        var source = """
            class C {
                private static run() { return "static"; }
            }
            console.log(C.run());
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("is private", ex.Message);
    }

    [Theory, ModeData]
    public void PrivateStaticMethod_InternalAccess_Allowed(ExecutionMode mode)
    {
        var source = """
            class C {
                private static secret() { return "s"; }
                static reveal() { return C.secret(); }
            }
            console.log(C.reveal());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("s\n", output);
    }

    [Theory, ModeData]
    public void ProtectedStaticMethod_FromSubclass_Allowed(ExecutionMode mode)
    {
        var source = """
            class Base {
                protected static make() { return "base"; }
            }
            class Sub extends Base {
                static build() { return Sub.make(); }
            }
            console.log(Sub.build());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("base\n", output);
    }

    [Theory, ModeData]
    public void ProtectedStaticMethod_ExternalAccess_Errors(ExecutionMode mode)
    {
        var source = """
            class Base {
                protected static make() { return "base"; }
            }
            console.log(Base.make());
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("is protected", ex.Message);
    }

    [Theory, ModeData]
    public void PrivateInstanceMethod_NotMaskedByPublicStaticTwin(ExecutionMode mode)
    {
        // Issue #722 collision: a public `static run` must not overwrite the
        // visibility of a same-named private instance `run`. External instance
        // access must still be rejected as private.
        var source = """
            class C {
                private run() { return "instance"; }
                static run() { return "static"; }
            }
            console.log(new C().run());
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("is private", ex.Message);
    }

    [Theory, ModeData]
    public void PublicStaticTwin_StillAccessible_WhenInstanceTwinIsPrivate(ExecutionMode mode)
    {
        // The mirror of the above: the public static `run` remains accessible
        // even though the same-named instance `run` is private.
        var source = """
            class C {
                private run() { return "instance"; }
                static run() { return "static"; }
            }
            console.log(C.run());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("static\n", output);
    }

    // --- Static fields (same StaticFieldAccess treatment as static methods) ---

    [Theory, ModeData]
    public void PrivateStaticField_ExternalRead_Errors(ExecutionMode mode)
    {
        var source = """
            class C {
                private static x = 1;
            }
            console.log(C.x);
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("is private", ex.Message);
    }

    [Theory, ModeData]
    public void PrivateStaticField_ExternalAssignment_Errors(ExecutionMode mode)
    {
        var source = """
            class C {
                private static x = 1;
            }
            C.x = 5;
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("is private", ex.Message);
    }

    [Theory, ModeData]
    public void PrivateStaticField_InternalRead_Allowed(ExecutionMode mode)
    {
        var source = """
            class C {
                private static x = 1;
                static read() { return C.x; }
            }
            console.log(C.read());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void ProtectedStaticField_FromSubclass_Allowed(ExecutionMode mode)
    {
        var source = """
            class Base { protected static x = 9; }
            class Sub extends Base {
                static read() { return Sub.x; }
            }
            console.log(Sub.read());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("9\n", output);
    }

    [Theory, ModeData]
    public void PrivateInstanceField_NotMaskedByPublicStaticTwin(ExecutionMode mode)
    {
        // Field collision mirror of issue #722: a public `static x` must not
        // overwrite the visibility of a same-named private instance `x`.
        var source = """
            class C {
                private x = 1;
                static x = 2;
            }
            console.log(new C().x);
            """;
        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("is private", ex.Message);
    }

    [Theory, ModeData]
    public void PublicStaticField_ExternalRead_Allowed(ExecutionMode mode)
    {
        var source = """
            class C {
                static x = 7;
            }
            console.log(C.x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }
}
