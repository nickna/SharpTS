using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for TypeScript decorators (both Legacy Stage 2 and TC39 Stage 3).
/// </summary>
public class DecoratorTests
{
    #region Legacy (Stage 2) Decorators

    [Fact]
    public void LegacyClassDecorator_Simple()
    {
        const string source = """
            function logged(target: any): void {
                console.log("Class decorated");
            }

            @logged
            class MyClass {
                constructor() {}
            }
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("Class decorated\n", output);
    }

    [Fact]
    public void LegacyClassDecorator_Factory()
    {
        const string source = """
            function tag(name: string): any {
                function decorator(target: any): void {
                    console.log("Tagged with: " + name);
                }
                return decorator;
            }

            @tag("important")
            class MyClass {
                constructor() {}
            }
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("Tagged with: important\n", output);
    }

    [Fact]
    public void LegacyMethodDecorator_Simple()
    {
        const string source = """
            function logMethod(target: any, propertyKey: string, descriptor: any): void {
                console.log("Method decorated: " + propertyKey);
            }

            class MyClass {
                @logMethod
                greet(): void {
                    console.log("Hello!");
                }
            }

            let obj = new MyClass();
            obj.greet();
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("Method decorated: greet\nHello!\n", output);
    }

    [Fact]
    public void LegacyFieldDecorator_Simple()
    {
        const string source = """
            function logField(target: any, propertyKey: string): void {
                console.log("Field decorated: " + propertyKey);
            }

            class MyClass {
                @logField
                name: string = "test";
            }

            let obj = new MyClass();
            console.log(obj.name);
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("Field decorated: name\ntest\n", output);
    }

    [Fact]
    public void LegacyMultipleDecorators_RightToLeft()
    {
        const string source = """
            function first(target: any): void {
                console.log("first");
            }

            function second(target: any): void {
                console.log("second");
            }

            @first
            @second
            class MyClass {
                constructor() {}
            }
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        // Decorators are applied right-to-left (bottom-to-top)
        Assert.Equal("second\nfirst\n", output);
    }

    [Fact]
    public void LegacyParameterDecorator_Simple()
    {
        const string source = """
            function logParam(target: any, propertyKey: string | null, paramIndex: number): void {
                console.log("Parameter " + paramIndex + " on " + propertyKey);
            }

            class MyClass {
                greet(@logParam name: string): void {
                    console.log("Hello, " + name);
                }
            }

            let obj = new MyClass();
            obj.greet("World");
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("Parameter 0 on greet\nHello, World\n", output);
    }

    #endregion

    #region TC39 Stage 3 Decorators

    [Fact]
    public void Stage3ClassDecorator_Simple()
    {
        const string source = """
            function logged(value: any, context: any): void {
                console.log("Class " + context.kind + ": " + context.name);
            }

            @logged
            class MyClass {
                constructor() {}
            }
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Stage3);
        Assert.Equal("Class class: MyClass\n", output);
    }

    [Fact]
    public void Stage3MethodDecorator_Simple()
    {
        const string source = """
            function logMethod(value: any, context: any): void {
                console.log("Method " + context.kind + ": " + context.name);
            }

            class MyClass {
                @logMethod
                greet(): void {
                    console.log("Hello!");
                }
            }

            let obj = new MyClass();
            obj.greet();
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Stage3);
        Assert.Equal("Method method: greet\nHello!\n", output);
    }

    [Fact]
    public void Stage3FieldDecorator_Simple()
    {
        const string source = """
            function logField(value: any, context: any): void {
                console.log("Field " + context.kind + ": " + context.name);
            }

            class MyClass {
                @logField
                name: string = "test";
            }

            let obj = new MyClass();
            console.log(obj.name);
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Stage3);
        Assert.Equal("Field field: name\ntest\n", output);
    }

    [Fact]
    public void Stage3Decorator_ContextStatic()
    {
        const string source = """
            function logStatic(value: any, context: any): void {
                console.log("static: " + context.static);
            }

            class MyClass {
                @logStatic
                instanceMethod(): void {}

                @logStatic
                static staticMethod(): void {}
            }
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Stage3);
        Assert.Equal("static: false\nstatic: true\n", output);
    }

    #endregion

    #region Reflect Metadata

    [Fact]
    public void ReflectMetadata_DefineAndGet()
    {
        const string source = """
            class MyClass {
                name: string = "test";
            }

            Reflect.defineMetadata("key", "value", MyClass);
            console.log(Reflect.getMetadata("key", MyClass));
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("value\n", output);
    }

    [Fact]
    public void ReflectMetadata_PropertyKey()
    {
        const string source = """
            class MyClass {
                name: string = "test";
            }

            Reflect.defineMetadata("role", "admin", MyClass, "name");
            console.log(Reflect.getMetadata("role", MyClass, "name"));
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("admin\n", output);
    }

    [Fact]
    public void ReflectMetadata_HasMetadata()
    {
        const string source = """
            class MyClass {}

            console.log(Reflect.hasMetadata("key", MyClass));
            Reflect.defineMetadata("key", "value", MyClass);
            console.log(Reflect.hasMetadata("key", MyClass));
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void ReflectMetadata_GetKeys()
    {
        const string source = """
            class MyClass {}

            Reflect.defineMetadata("key1", "value1", MyClass);
            Reflect.defineMetadata("key2", "value2", MyClass);
            let keys = Reflect.getMetadataKeys(MyClass);
            console.log(keys.length);
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void ReflectMetadata_DeleteMetadata()
    {
        const string source = """
            class MyClass {}

            Reflect.defineMetadata("key", "value", MyClass);
            console.log(Reflect.hasMetadata("key", MyClass));
            Reflect.deleteMetadata("key", MyClass);
            console.log(Reflect.hasMetadata("key", MyClass));
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void ReflectMetadata_DecoratorFactory()
    {
        const string source = """
            @Reflect.metadata("role", "admin")
            class MyClass {}

            console.log(Reflect.getMetadata("role", MyClass));
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("admin\n", output);
    }

    #endregion

    #region Error Cases

    [Fact]
    public void DecoratorWithoutFlag_ThrowsError()
    {
        const string source = """
            function logged(target: any): void {}

            @logged
            class MyClass {}
            """;

        // Without decorator mode, the parser won't recognize @ as a decorator
        // It will throw an error about unexpected token
        var exception = Assert.Throws<Exception>(() =>
            TestHarness.RunInterpreted(source, DecoratorMode.None));
        // Parser throws "Expect expression." because @ is not expected
        Assert.Contains("Expect expression", exception.Message);
    }

    [Fact]
    public void Stage3ParameterDecorator_ThrowsError()
    {
        const string source = """
            function logParam(value: any, context: any): void {}

            class MyClass {
                greet(@logParam name: string): void {}
            }
            """;

        // Parameter decorators are not supported in Stage 3
        var exception = Assert.Throws<Exception>(() =>
            TestHarness.RunInterpreted(source, DecoratorMode.Stage3));
        Assert.Contains("Parameter decorators are not supported in Stage 3", exception.Message);
    }

    #endregion
}
