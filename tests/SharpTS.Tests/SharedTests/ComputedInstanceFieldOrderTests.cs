using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ComputedInstanceFieldOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["neutral", """
            const events: string[] = [];
            const keys = {
                get erased(): 'erased' { events.push('erased-key'); return 'erased'; },
                get actual(): 'actual' { events.push('actual-key'); return 'actual'; }
            };
            const Model = class {
                before = (events.push('before'), 1);
                [keys.actual] = (events.push('actual-value'), 2);
                after = (events.push('after'), 3);
            };
            console.log(events.join(','));
            events.length = 0;
            const first = new Model(), second = new Model();
            console.log(events.join(','));
            console.log(Object.hasOwn(first, 'erased'), Object.hasOwn(second, 'erased'), Object.keys(first).join(','), first.before, first.after);
            """.ReplaceLineEndings("\r\n") + "\r\n", "actual-key\nbefore,actual-value,after,before,actual-value,after\nfalse false before,actual,after 1 3\n"];
        yield return ["computed-field-deletion-and-recreation", """
            const key = 'actual';
            const Model = class { before = 1; [key] = 2; after = 3; };
            const value: any = new Model();
            console.log(delete value.actual, Object.keys(value).join(','));
            value.actual = 4;
            console.log(Object.keys(value).join(','), value.actual);
            """.ReplaceLineEndings("\n") + "\n", "true before,after\nbefore,after,actual 4\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedFieldsRetainTheirCreationOrder(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        string actual;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            actual = TestHarness.Run(source, ExecutionMode.Interpreted);
        }
        Assert.Equal(expected, actual);
    }
}
