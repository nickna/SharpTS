using SharpTS.Tests.CompilerTests;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RuntimeSuperclassTests
{
    [Theory]
    [InlineData("awaited-parent.ts", "right\n2 right\ntrue\ntrue false\n")]
    [InlineData("observed-parent.ts", "right\nfunction right\ntrue\ntrue false\n")]
    [InlineData("abrupt-parent.ts", "parent \n")]
    public void InvestigationFixturesMatchInBothEnginesAndVerifiedOutput(string name, string expected)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures", "DynamicSuperclass")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "tests", "fixtures", "DynamicSuperclass", name));
        Verify(source, expected);
        // Hosted modules invoke a reserved main entry point after initialization.
        // These scripts already call their runner, so give that runner a neutral name.
        HistoricalRuntimeDeploymentTests.AssertDeployment(name,
            source.Replace("main(", "runtimeParentProbe(", StringComparison.Ordinal), expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingParentSelectionUsesTheOriginalReceiverAndEachDefinition(bool declaration)
    {
        var body = """
            {
                static initialized = (order += "static ");
                own = 10;
                constructor(value: number) { super(value); this.value += this.own; }
                child() { return super.read() + this.own; }
            }
            """;
        var definition = declaration ? $"class Child extends selected {body}; return Child;"
            : $"return class extends selected {body};";
        var source = """
            let order = "";
            class Root {
                value = 0;
                read() { return this.value; }
            }
            class Left extends Root {
                static side = "left";
                receiver: any;
                constructor(value: number) { super(); this.value = value; this.receiver = this; }
                read() { return super.read() + 1; }
            }
            class Right extends Root {
                static side = "right";
                receiver: any;
                constructor(value: number) { super(); this.value = value * 2; this.receiver = this; }
                read() { return super.read() + 2; }
            }
            function select(parent: any) {
                order += "select ";
                return new Promise<any>(resolve => setTimeout(() => { order += "settle "; resolve(parent); }, 1));
            }
            async function make(parent: any) {
                const selected = await select(parent);
                order += "ready ";
            """ + definition + """
            }
            async function main() {
                const First: any = await make(Left), Second: any = await make(Right);
                const first: any = new First(3), second: any = new Second(4);
                console.log(first.read(), first.child(), second.read(), second.child());
                console.log(first.receiver === first, second.receiver === second, first.own, second.own);
                console.log(First.side, Second.side, Object.getPrototypeOf(First) === Left, Object.getPrototypeOf(Second) === Right);
                console.log(Object.getPrototypeOf(First.prototype) === Left.prototype, Object.getPrototypeOf(Second.prototype) === Right.prototype);
                console.log(first instanceof Root, first instanceof Left, first instanceof Right, second instanceof Root, second instanceof Left, second instanceof Right);
                console.log(order);
            }
            main().then(() => {}, error => console.log("rejected", error.message));
            """;
        Verify(source, "14 24 20 30\ntrue true 10 10\nleft right true true\ntrue true\ntrue true false true false true\nselect settle ready static select settle ready static \n");
    }

    [Theory, ModeData]
    public void ImplicitDerivedConstructorForwardsArguments(ExecutionMode mode)
    {
        var source = """
            class Base { value = 0; constructor(value: number) { this.value = value; } read() { return this.value; } }
            function make(parent: any) { return class extends parent { own = 7; }; }
            const Child: any = make(Base), instance: any = new Child(9);
            console.log(instance.read(), instance.own, instance instanceof Base);
            """;
        Assert.Equal("9 7 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ParentAdaptersPreserveDefaultsArgumentsAndStaticReceiver(ExecutionMode mode)
    {
        var source = """
            class Root { static read() { return this.side; } static side = "root"; }
            class Parent extends Root {
                static side = "parent";
                static read() { return this.side; }
                value = 0;
                count = 0;
                total = 0;
                constructor(value: number = 7, ...rest: number[]) {
                    super(); this.value = value; this.count = arguments.length; this.total = rest.length;
                }
                read(...values: number[]) { console.log(arguments.length, values[0]); return this.value; }
            }
            function make(parent: any) { return class extends parent { static side = "child"; }; }
            const Child: any = make(Parent), first: any = new Child(), second: any = new Child(9, 10, 11);
            console.log(first.read(3), first.count, first.total, second.read(4), second.count, second.total);
            console.log(Child.read(), Parent.read(), Root.read());
            """;
        Assert.Equal("1 3\n1 4\n7 0 0 9 3 2\nchild parent root\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SelectedExpressionParentAndSameNameParameterUseRuntimeIdentity(ExecutionMode mode)
    {
        var source = """
            class Parent { value = 1; read() { return this.value; } }
            const Selected = class { value = 2; read() { return this.value; } };
            function make(Parent: any) { return class extends Parent { own = 3; }; }
            const Child: any = make(Selected), instance: any = new Child();
            console.log(instance.read(), instance.own, instance instanceof Selected, instance instanceof Parent);
            console.log(Object.getPrototypeOf(Child) === Selected, Object.getPrototypeOf(Child.prototype) === Selected.prototype);
            """;
        Assert.Equal("2 3 true false\ntrue true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InvalidParentAndRejectedAwaitPrecedeStaticInitialization(ExecutionMode mode)
    {
        var source = """
            let initialized = "";
            const reason = new Error("original");
            function make(parent: any) { return class extends parent { static value = (initialized += "bad"); }; }
            try { make(7); } catch (error: any) { console.log(error instanceof TypeError, initialized); }
            async function run() {
                try { class Child extends (await Promise.reject<any>(reason)) { static value = (initialized += "bad"); } }
                catch (error: any) { console.log(error === reason, error.message, initialized); }
            }
            run();
            """;
        Assert.Equal("true \ntrue original \n", TestHarness.Run(source, mode));
    }

    private static void Verify(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.RunInterpreted(source, TimeSpan.FromSeconds(30)));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
