using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for #276: in compiled mode, accessing a built-in singleton's
/// method through a <em>value</em> (rather than the bare <c>Math.method(...)</c>
/// syntactic form) yielded <c>undefined</c>. The dedicated static emitters
/// (MathStaticEmitter / JSONStaticEmitter) intercept <c>Math.max(...)</c> at
/// compile time before the receiver is evaluated; when the singleton is used as
/// a value the lookup fell through to the runtime <c>_mathSingleton</c> /
/// <c>_jsonSingleton</c> dictionaries, which were created empty and never
/// populated. They are now lazily populated with the same identity-cached
/// <c>$TSFunction</c> wrappers the value-form static emitters hand out.
/// </summary>
public class BuiltInSingletonValueFormTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_AsValue_MethodsDispatch(ExecutionMode mode)
    {
        var source = @"
            const m: any = Math;
            console.log(typeof m.max);
            console.log(m.max(1, 2));
            console.log(m.min(5, 3, 8));
            console.log(m.floor(3.7));
            console.log(m.abs(-4));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n2\n3\n3\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Json_AsValue_MethodsDispatch(ExecutionMode mode)
    {
        var source = @"
            const j: any = JSON;
            console.log(typeof j.stringify);
            console.log(j.stringify({ a: 1 }));
            console.log(j.parse('{""b"":2}').b);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n{\"a\":1}\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_AsValue_MethodIdentityIsStable(ExecutionMode mode)
    {
        // The value-form wrapper must be the same identity-cached method the bare
        // `Math.max` syntactic form hands out (ECMA-262: built-in methods are
        // single objects). The interpreter's namespace path now binds to the Math
        // singleton, so it returns the same receiver-cached method as the
        // instance path — identity holds in both modes (#288).
        var source = @"
            const m: any = Math;
            console.log(m.max === Math.max);
            console.log(m.floor === Math.floor);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_AsValue_MethodsAreNonEnumerable(ExecutionMode mode)
    {
        // Math's methods are non-enumerable per ECMA-262 §17, so `Object.keys(Math)`
        // is empty. Compiled mode installs non-enumerable descriptors; the
        // interpreter treats the Math singleton's own enumerable properties as just
        // its user-assigned extras (none here). Both modes return [] (#288).
        var source = @"
            const m: any = Math;
            console.log(Object.keys(m).length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Math_AsValue_ValuesAndEntriesAreEmpty(ExecutionMode mode)
    {
        // Object.values/entries on Math return only its own enumerable properties;
        // the built-in members are non-enumerable, so with no user extras the
        // result is empty (previously the interpreter threw). Scoped to
        // interpreted mode: compiled Object.values/entries(Math) currently also
        // enumerate the non-enumerable built-in methods — tracked separately.
        // (Tests avoid assigning to Math: its singleton is process-global, so an
        // extra would leak into other in-process interpreted runs.) (#288)
        var source = @"
            const m: any = Math;
            console.log(Object.values(m).length);
            console.log(Object.entries(m).length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n0\n", output);
    }
}
