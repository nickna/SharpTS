using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a compiler gap surfaced by the Phase 3i util migration
/// attempt: built-in global class names (Date, RegExp, Map, Set, WeakMap,
/// WeakSet, Promise, Array, Function) were not resolvable as bare values
/// (e.g. in <c>x instanceof Date</c>). The compile-time <c>new Date()</c>
/// pattern match masked the gap for construction, but any use of the name
/// as a value threw <c>ReferenceError: Undefined variable 'Date'</c>.
/// </summary>
/// <remarks>
/// Fixed by routing unresolved bare identifiers through a built-in class
/// lookup in <c>ILEmitter.EmitVariable</c> that emits the matching .NET
/// Type token. The runtime's <c>InstanceOf</c> helper then matches instances
/// via <c>Type.IsAssignableFrom</c>.
/// </remarks>
public class BuiltInClassValueResolutionTests
{
    [Theory, ModeData]
    public void InstanceOf_Date(ExecutionMode mode)
    {
        var source = @"
            console.log(new Date() instanceof Date);
            console.log('hello' instanceof Date);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void InstanceOf_RegExp(ExecutionMode mode)
    {
        var source = @"
            console.log(/abc/ instanceof RegExp);
            console.log('abc' instanceof RegExp);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void InstanceOf_Map(ExecutionMode mode)
    {
        var source = @"
            console.log(new Map() instanceof Map);
            console.log({} instanceof Map);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void InstanceOf_Set(ExecutionMode mode)
    {
        var source = @"
            console.log(new Set() instanceof Set);
            console.log([] instanceof Set);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void InstanceOf_Error(ExecutionMode mode)
    {
        var source = @"
            console.log(new Error('boom') instanceof Error);
            console.log('oops' instanceof Error);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void InstanceOf_Date_InsideFunction(ExecutionMode mode)
    {
        // The original repro lived inside a function body. Keep one test at
        // that shape because the receiver for the instanceof check is a
        // parameter (no narrowing, no inference).
        var source = @"
            function check(x: any): string {
                if (x instanceof Date) return 'date';
                return typeof x;
            }
            console.log(check(new Date()));
            console.log(check('hello'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("date\nstring\n", output);
    }

    [Theory, ModeData]
    public void InstanceOf_Date_InsideImportedModule(ExecutionMode mode)
    {
        // Exercises the stdlib-module code path — specifically the same
        // shape the util.ts migration needs.
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function check(x: any): string {
                    if (x instanceof Date) return 'date';
                    if (x instanceof RegExp) return 'regexp';
                    if (x instanceof Map) return 'map';
                    if (x instanceof Set) return 'set';
                    return typeof x;
                }
                """,
            ["main.ts"] = """
                import { check } from './lib';
                console.log(check(new Date()));
                console.log(check(/x/));
                console.log(check(new Map()));
                console.log(check(new Set()));
                console.log(check(42));
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("date\nregexp\nmap\nset\nnumber\n", output);
    }
}
