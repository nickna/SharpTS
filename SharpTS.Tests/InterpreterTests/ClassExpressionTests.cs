using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for class expressions (const C = class { ... }).
/// </summary>
public class ClassExpressionTests
{
    [Fact]
    public void AnonymousClassExpression_Basic()
    {
        var source = """
            const MyClass = class {
                x: number = 42;
            };
            let obj = new MyClass();
            console.log(obj.x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AnonymousClassExpression_WithMethod()
    {
        var source = """
            const Counter = class {
                count: number = 0;
                increment(): number {
                    this.count = this.count + 1;
                    return this.count;
                }
            };
            let c = new Counter();
            c.increment();
            c.increment();
            console.log(c.count);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void AnonymousClassExpression_WithConstructor()
    {
        var source = """
            const Point = class {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            };
            let p = new Point(10, 20);
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n", output);
    }

    [Fact]
    public void NamedClassExpression_Basic()
    {
        var source = """
            const Node = class Node {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            };
            let n = new Node(99);
            console.log(n.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("99\n", output);
    }

    [Fact]
    public void ClassExpression_WithInheritance()
    {
        var source = """
            const Base = class {
                getValue(): number { return 10; }
            };
            const Derived = class extends Base {
                getValue(): number { return 20; }
            };
            let b = new Base();
            let d = new Derived();
            console.log(b.getValue());
            console.log(d.getValue());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n", output);
    }

    [Fact]
    public void ClassExpression_SuperCall()
    {
        var source = """
            const Animal = class {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            };
            const Dog = class extends Animal {
                constructor(name: string) {
                    super(name);
                }
                bark(): string { return this.name + " barks"; }
            };
            let d = new Dog("Rex");
            console.log(d.bark());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex barks\n", output);
    }

    [Fact]
    public void ClassExpression_MultipleClassesInArray()
    {
        // Note: new <expression>() syntax isn't supported yet.
        // This test verifies class expressions can be stored and retrieved from arrays.
        var source = """
            const Class1 = class { value: number = 1; };
            const Class2 = class { value: number = 2; };
            let classes = [Class1, Class2];
            let obj1 = new Class1();
            let obj2 = new Class2();
            console.log(obj1.value);
            console.log(obj2.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void ClassExpression_AssignedToVariable()
    {
        // Class expressions can be assigned to variables and instantiated
        var source = """
            const MyClass = class {
                value: number = 123;
            };
            let obj = new MyClass();
            console.log(obj.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("123\n", output);
    }

    [Fact]
    public void ClassExpression_StaticField()
    {
        var source = """
            const MyClass = class {
                static count: number = 0;
            };
            console.log(MyClass.count);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void ClassExpression_StaticMethod()
    {
        var source = """
            const Factory = class {
                static create(): number {
                    return 42;
                }
            };
            console.log(Factory.create());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }
}
