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
    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Json_StaticMethodIdentityIsStable(ExecutionMode mode)
    {
        // #299: JSON.parse / JSON.stringify are single built-in function objects
        // per ECMA-262 §25.5, so repeated access returns the SAME callable.
        // Previously the interpreter synthesized a fresh wrapper per access while
        // compiled mode was already stable — both now agree.
        var source = @"
            console.log(JSON.stringify === JSON.stringify);
            console.log(JSON.parse === JSON.parse);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Math_ValuesAndEntriesExcludeNonEnumerableBuiltIns(ExecutionMode mode)
    {
        // #298: Object.values/entries must apply the same own-enumerable filter
        // Object.keys does. Math's built-in methods are non-enumerable, so only
        // the user-assigned extra surfaces — keys, values, and entries agree.
        var source = @"
            const m: any = Math;
            m.foo = 42;
            console.log(Object.keys(m).length);
            console.log(Object.values(m).length);
            console.log(Object.values(m)[0]);
            console.log(Object.entries(m).length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n42\n1\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Json_ValuesAndEntriesExcludeNonEnumerableBuiltIns(ExecutionMode mode)
    {
        // #298: same own-enumerable filter for the JSON singleton.
        var source = @"
            const j: any = JSON;
            j.bar = 7;
            console.log(Object.keys(j).length);
            console.log(Object.values(j).length);
            console.log(Object.entries(j).length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n1\n", output);
    }

    [Theory, ModeData]
    public void PlainObject_ValuesAndEntriesUnaffectedByEnumerableFilter(ExecutionMode mode)
    {
        // Guard the #298 fix: a plain object literal (no installed descriptors,
        // enumerable by default) must still enumerate every own property.
        var source = @"
            const o = { a: 1, b: 2, c: 3 };
            console.log(Object.values(o).join(','));
            console.log(Object.entries(o).length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n3\n", output);
    }

    [Theory, InterpretedOnlyData]
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
