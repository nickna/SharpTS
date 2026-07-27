using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for Phase 3h URL-migration blocker #1: CheckSet didn't handle
/// <c>TypeInfo.Interface</c>, so assigning to a field on an interface-typed
/// value threw "Only instances and objects have properties." The type check
/// aborted mid-body, TypeMap entries for subsequent expressions weren't
/// populated, and the IL emitter fell through to generic dispatch.
/// </summary>
/// <remarks>
/// The downstream symptom was array mutators — notably
/// <c>url.path.push(buffer)</c> — losing their type-specific dispatch and
/// storing their argument wrapped in an <c>object[]</c>. Reading the element
/// back showed <c>"System.Object[]"</c> instead of the expected string.
/// </remarks>
public class InterfaceFieldAssignmentTests
{
    [Theory, ModeData]
    public void AssignPrimitiveField_OnInterface(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                interface Rec { name: string; count: number; }
                function make(): Rec { return { name: '', count: 0 }; }
                const r: Rec = make();
                r.name = 'hello';
                r.count = 42;
                console.log(r.name + ' ' + r.count);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello 42\n", output);
    }

    [Theory, ModeData]
    public void AssignArrayField_ThenPush_PreservesElementType(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                interface Rec { scheme: string; path: string[]; }
                function make(): Rec { return { scheme: '', path: [] }; }
                const r: Rec = make();
                r.scheme = 'https';
                r.path.push('a');
                r.path.push('b');
                console.log(r.path.length + ' ' + r.path[0] + ' ' + r.path[1] + ' ' + typeof r.path[0]);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2 a b string\n", output);
    }

    [Theory, ModeData]
    public void AssignField_InsideSwitchStateMachine(ExecutionMode mode)
    {
        // Exactly the WHATWG URL parser pattern: interface param, switch-heavy
        // function assigns a primitive field in one case then pushes to an
        // array field in another. Before the fix, the second push silently
        // stored an object[] wrapper instead of the string.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                interface Rec { scheme: string; path: string[]; }
                function run(input: string): Rec {
                    const r: Rec = { scheme: '', path: [] };
                    let state = 1;
                    let buffer = '';
                    let i = 0;
                    while (i <= input.length) {
                        const c = i < input.length ? input.charCodeAt(i) : -1;
                        switch (state) {
                            case 1:
                                if (c === 58) { r.scheme = buffer; buffer = ''; state = 2; }
                                else if (c >= 0) buffer += input.charAt(i);
                                else state = 99;
                                break;
                            case 2:
                                if (c === -1 || c === 47) { r.path.push(buffer); buffer = ''; }
                                else buffer += input.charAt(i);
                                break;
                            case 99: break;
                        }
                        if (state === 99) break;
                        i++;
                    }
                    return r;
                }
                const r = run('https://a/b/c');
                console.log(r.scheme + ' | ' + r.path.length + ' | ' + r.path[4] + ' | ' + typeof r.path[4]);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("https | 5 | c | string\n", output);
    }
}
