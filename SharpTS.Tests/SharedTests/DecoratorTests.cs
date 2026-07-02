using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for TypeScript decorators (both Legacy Stage 2 and TC39 Stage 3).
/// Core decorator tests run in both interpreter and compiled modes.
/// Reflect.metadata tests remain interpreter-only (separate effort).
/// </summary>
public class DecoratorTests
{
    #region Legacy (Stage 2) Decorators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyClassDecorator_Simple(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Class decorated\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyClassDecorator_Factory(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Tagged with: important\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyMethodDecorator_Simple(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Method decorated: greet\nHello!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyFieldDecorator_Simple(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Field decorated: name\ntest\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyMultipleDecorators_RightToLeft(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        // Decorators are applied right-to-left (bottom-to-top)
        Assert.Equal("second\nfirst\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyParameterDecorator_Simple(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Parameter 0 on greet\nHello, World\n", output);
    }

    #endregion

    #region TC39 Stage 3 Decorators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stage3ClassDecorator_Simple(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Stage3);
        Assert.Equal("Class class: MyClass\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stage3MethodDecorator_Simple(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Stage3);
        Assert.Equal("Method method: greet\nHello!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stage3FieldDecorator_Simple(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Stage3);
        Assert.Equal("Field field: name\ntest\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stage3Decorator_ContextStatic(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode, DecoratorMode.Stage3);
        Assert.Equal("static: false\nstatic: true\n", output);
    }

    #endregion

    #region Reflect Metadata

    /// <summary>Compiled mode: Reflect metadata API is not available.</summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReflectMetadata_DefineAndGet(ExecutionMode mode)
    {
        const string source = """
            class MyClass {
                name: string = "test";
            }

            Reflect.defineMetadata("key", "value", MyClass);
            console.log(Reflect.getMetadata("key", MyClass));
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("value\n", output);
    }

    /// <summary>Compiled mode: Reflect metadata API is not available.</summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReflectMetadata_PropertyKey(ExecutionMode mode)
    {
        const string source = """
            class MyClass {
                name: string = "test";
            }

            Reflect.defineMetadata("role", "admin", MyClass, "name");
            console.log(Reflect.getMetadata("role", MyClass, "name"));
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("admin\n", output);
    }

    /// <summary>Compiled mode: Reflect metadata API is not available.</summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReflectMetadata_HasMetadata(ExecutionMode mode)
    {
        const string source = """
            class MyClass {}

            console.log(Reflect.hasMetadata("key", MyClass));
            Reflect.defineMetadata("key", "value", MyClass);
            console.log(Reflect.hasMetadata("key", MyClass));
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("false\ntrue\n", output);
    }

    /// <summary>Compiled mode: Reflect metadata API is not available.</summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReflectMetadata_GetKeys(ExecutionMode mode)
    {
        const string source = """
            class MyClass {}

            Reflect.defineMetadata("key1", "value1", MyClass);
            Reflect.defineMetadata("key2", "value2", MyClass);
            let keys = Reflect.getMetadataKeys(MyClass);
            console.log(keys.length);
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("2\n", output);
    }

    /// <summary>Compiled mode: Reflect metadata API is not available.</summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReflectMetadata_DeleteMetadata(ExecutionMode mode)
    {
        const string source = """
            class MyClass {}

            Reflect.defineMetadata("key", "value", MyClass);
            console.log(Reflect.hasMetadata("key", MyClass));
            Reflect.deleteMetadata("key", MyClass);
            console.log(Reflect.hasMetadata("key", MyClass));
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReflectMetadata_DecoratorFactory(ExecutionMode mode)
    {
        const string source = """
            @Reflect.metadata("role", "admin")
            class MyClass {}

            console.log(Reflect.getMetadata("role", MyClass));
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
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
        var exception = Assert.Throws<Exception>(() =>
            TestHarness.RunInterpreted(source, DecoratorMode.None));
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
        var exception = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted(source, DecoratorMode.Stage3));
        Assert.Contains("Parameter decorators are not supported in Stage 3", exception.Message);
    }

    #endregion

    #region Decorators on exported classes (issue #1192)

    // `@decorator` legally appears *before* `export` (the only valid decorator
    // placement TypeScript allows on an exported class). Before the fix, Declaration()
    // only checked EXPORT before parsing decorators, so `@dec export class` fell through
    // to "Decorators are not valid here." These cover every export-class form.

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyClassDecorator_OnExportClass(ExecutionMode mode)
    {
        const string source = """
            function logged(target: any): void {
                console.log("Class decorated");
            }

            @logged
            export class MyClass {
                constructor() {}
            }
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Class decorated\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyClassDecorator_OnExportDefaultClass(ExecutionMode mode)
    {
        const string source = """
            function logged(target: any): void {
                console.log("Class decorated");
            }

            @logged
            export default class MyClass {
                constructor() {}
            }
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Class decorated\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyClassDecorator_OnExportAbstractClass(ExecutionMode mode)
    {
        const string source = """
            function logged(target: any): void {
                console.log("Class decorated");
            }

            @logged
            export abstract class Shape {
                abstract area(): number;
            }

            class Square extends Shape {
                constructor(private side: number) { super(); }
                area(): number { return this.side * this.side; }
            }

            console.log(new Square(3).area());
            """;

        var output = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.Equal("Class decorated\n9\n", output);
    }

    [Fact]
    public void ClassDecorator_OnExportClass_PassesDecoratorArgument()
    {
        // Factory decorator on an exported class — the decorator arguments must still be
        // threaded through, not dropped, when the class is exported.
        const string source = """
            function tag(name: string): any {
                return function (target: any): void {
                    console.log("Tagged: " + name);
                };
            }

            @tag("widget")
            export class Widget {
                constructor() {}
            }
            """;

        var output = TestHarness.RunInterpreted(source, DecoratorMode.Legacy);
        Assert.Equal("Tagged: widget\n", output);
    }

    [Fact]
    public void Decorator_OnExportFunction_IsRejected()
    {
        // Decorators are only valid on classes/class members — an exported *function*
        // with a preceding decorator must still be a parse error.
        const string source = """
            function logged(target: any): void {}

            @logged
            export function foo(): void {}
            """;

        var exception = Assert.Throws<Exception>(() =>
            TestHarness.RunInterpreted(source, DecoratorMode.Legacy));
        Assert.Contains("Decorators are not valid here", exception.Message);
    }

    #endregion
}
