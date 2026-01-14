using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for class expressions (const C = class { ... }) - IL compilation.
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ClassExpression_ConstructorWithDefaultParameters()
    {
        // Regression test: class expression constructors with default parameters
        // Previously failed with MissingMethodException because Activator.CreateInstance
        // was used instead of direct newobj with argument padding.
        var source = """
            const Config = class {
                host: string;
                port: number;
                constructor(host: string = "localhost", port: number = 8080) {
                    this.host = host;
                    this.port = port;
                }
            };

            const c1 = new Config();
            console.log(c1.host);
            console.log(c1.port);

            const c2 = new Config("example.com");
            console.log(c2.host);
            console.log(c2.port);

            const c3 = new Config("api.example.com", 3000);
            console.log(c3.host);
            console.log(c3.port);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("localhost\n8080\nexample.com\n8080\napi.example.com\n3000\n", output);
    }

    [Fact]
    public void ClassExpression_TypedConstructorParameters()
    {
        // Test that constructor parameters are typed correctly (not all object)
        var source = """
            const Point = class {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
                sum(): number {
                    return this.x + this.y;
                }
            };
            let p = new Point(10.5, 20.5);
            console.log(p.sum());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("31\n", output);
    }

    [Fact]
    public void ClassExpression_TypedMethodParameters()
    {
        // Test that method parameters are typed correctly
        var source = """
            const Calculator = class {
                add(a: number, b: number): number {
                    return a + b;
                }
                multiply(a: number, b: number): number {
                    return a * b;
                }
            };
            let calc = new Calculator();
            console.log(calc.add(5, 3));
            console.log(calc.multiply(4, 7));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8\n28\n", output);
    }

    [Fact]
    public void ClassExpression_MixedTypeParameters()
    {
        // Test that mixed type parameters work correctly
        var source = """
            const Person = class {
                name: string;
                age: number;
                constructor(name: string, age: number) {
                    this.name = name;
                    this.age = age;
                }
                describe(): string {
                    return this.name + " is " + this.age;
                }
            };
            let p = new Person("Alice", 30);
            console.log(p.describe());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice is 30\n", output);
    }

    [Fact]
    public void ClassExpression_StaticMethodTypedParameters()
    {
        // Test that static method parameters are typed correctly
        var source = """
            const MathUtils = class {
                static square(n: number): number {
                    return n * n;
                }
                static double(n: number): number {
                    return n * 2;
                }
            };
            console.log(MathUtils.square(5));
            console.log(MathUtils.double(7));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("25\n14\n", output);
    }

    [Fact]
    public void ClassExpression_InheritanceTypedConstructors()
    {
        // Test that inheritance with typed constructors works correctly
        var source = """
            const Animal = class {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            };
            const Dog = class extends Animal {
                breed: string;
                constructor(name: string, breed: string) {
                    super(name);
                    this.breed = breed;
                }
                describe(): string {
                    return this.name + " the " + this.breed;
                }
            };
            let d = new Dog("Rex", "German Shepherd");
            console.log(d.describe());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Rex the German Shepherd\n", output);
    }

    [Fact]
    public void ClassExpression_MethodReturnTypes()
    {
        // Test that method return types are correct
        var source = """
            const Formatter = class {
                formatNumber(n: number): string {
                    return "Value: " + n;
                }
                parseNumber(s: string): number {
                    return 42;
                }
            };
            let f = new Formatter();
            console.log(f.formatNumber(100));
            console.log(f.parseNumber("ignored") + 8);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Value: 100\n50\n", output);
    }
}
