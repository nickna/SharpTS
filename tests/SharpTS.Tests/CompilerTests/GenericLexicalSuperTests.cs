using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GenericLexicalSuperTests
{
    [Fact]
    public void GenericClassExpressionInitializesEachInstanceBeforeTheArrowResumes()
    {
        const string source = """
            let initialized = 0;
            const C = class<T> extends class { constructor() {} } {
                value: T;
                order = ++initialized;
                constructor(value: T) {
                    const initialize = () => {
                        super();
                        console.log(this.order);
                    };
                    initialize();
                    this.value = value;
                }
            };
            const first = new C<number>(7);
            const second = new C<string>('s');
            console.log(first.value, second.value, first.order, second.order);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("1\n2\n7 s 1 2\n", output);
    }

    [Fact]
    public void LexicalSuperPreservesLiveParametersAndNewTargetThroughGenericInheritance()
    {
        const string source = """
            class Base<T> {
                target: any;
                constructor(public base: T) { this.target = new.target; }
            }
            const C = class<T> extends Base<T> {
                order = this.base;
                constructor(public current: T, replacement: T, lexical: boolean) {
                    current = replacement;
                    const initialize = () => super(current);
                    if (lexical) initialize();
                    else super(current);
                }
            };
            const first = new C<number>(1, 7, true);
            const second = new C<string>('old', 'new', false);
            console.log(first.base, first.order, first.current, first.target === C);
            console.log(second.base, second.order, second.current, second.target === C);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("7 7 7 true\nnew new new true\n", output);
    }

    [Fact]
    public void InheritedGenericConstructorKeepsTheMostDerivedNewTarget()
    {
        const string source = """
            let received: any;
            class Base<T> {
                constructor(value: T) { received = new.target; }
            }
            const Parent = class<T> extends Base<T> {
                order = 1;
                constructor(public current: T, replacement: T) {
                    current = replacement;
                    const initialize = () => super(current);
                    initialize();
                }
            };
            class Child extends Parent<number> {}
            const item = new Child(1, 7);
            console.log(item.order, item.current, received === Child);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("1 7 true\n", output);
    }
}
