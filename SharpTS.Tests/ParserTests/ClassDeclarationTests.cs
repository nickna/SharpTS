using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for class declaration parsing.
/// Covers basic classes, inheritance, access modifiers, static members, and generics.
/// </summary>
public class ClassDeclarationTests
{
    #region Helpers

    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    private static Stmt.Class ParseClass(string source)
    {
        var statements = Parse(source);
        Assert.Single(statements);
        return Assert.IsType<Stmt.Class>(statements[0]);
    }

    #endregion

    #region Basic Class

    [Fact]
    public void Class_Empty()
    {
        var classStmt = ParseClass("class Foo { }");
        Assert.Equal("Foo", classStmt.Name.Lexeme);
        Assert.Empty(classStmt.Methods);
        Assert.Empty(classStmt.Fields);
    }

    [Fact]
    public void Class_WithConstructor()
    {
        var source = """
            class Foo {
                constructor() { }
            }
            """;
        var classStmt = ParseClass(source);
        // Constructors are stored as methods with name "constructor"
        Assert.Contains(classStmt.Methods, m => m.Name.Lexeme == "constructor");
    }

    [Fact]
    public void Class_WithMethod()
    {
        var source = """
            class Foo {
                doSomething() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Methods);
        Assert.Equal("doSomething", classStmt.Methods[0].Name.Lexeme);
    }

    [Fact]
    public void Class_WithField()
    {
        var source = """
            class Foo {
                x: number;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Fields);
        Assert.Equal("x", classStmt.Fields[0].Name.Lexeme);
    }

    [Fact]
    public void Class_WithFieldInitializer()
    {
        var source = """
            class Foo {
                x: number = 5;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Fields);
        Assert.NotNull(classStmt.Fields[0].Initializer);
    }

    #endregion

    #region Inheritance

    [Fact]
    public void Class_Extends()
    {
        var source = """
            class Bar { }
            class Foo extends Bar { }
            """;
        var statements = Parse(source);
        Assert.Equal(2, statements.Count);
        var fooClass = Assert.IsType<Stmt.Class>(statements[1]);
        Assert.NotNull(fooClass.Superclass);
        Assert.Equal("Bar", fooClass.Superclass!.Lexeme);
    }

    [Fact]
    public void Class_Implements()
    {
        var source = """
            interface IBar { }
            class Foo implements IBar { }
            """;
        var statements = Parse(source);
        Assert.Equal(2, statements.Count);
        var fooClass = Assert.IsType<Stmt.Class>(statements[1]);
        Assert.NotNull(fooClass.Interfaces);
        Assert.Single(fooClass.Interfaces);
        Assert.Equal("IBar", fooClass.Interfaces[0].Lexeme);
    }

    [Fact]
    public void Class_ExtendsAndImplements()
    {
        var source = """
            class Bar { }
            interface IFoo { }
            class Foo extends Bar implements IFoo { }
            """;
        var statements = Parse(source);
        var fooClass = Assert.IsType<Stmt.Class>(statements[2]);
        Assert.NotNull(fooClass.Superclass);
        Assert.NotNull(fooClass.Interfaces);
        Assert.Single(fooClass.Interfaces);
    }

    [Fact]
    public void Class_ImplementsMultiple()
    {
        var source = """
            interface IA { }
            interface IB { }
            class Foo implements IA, IB { }
            """;
        var statements = Parse(source);
        var fooClass = Assert.IsType<Stmt.Class>(statements[2]);
        Assert.NotNull(fooClass.Interfaces);
        Assert.Equal(2, fooClass.Interfaces.Count);
    }

    #endregion

    #region Access Modifiers

    [Fact]
    public void Class_PublicMethod()
    {
        var source = """
            class Foo {
                public doSomething() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(AccessModifier.Public, classStmt.Methods[0].Access);
    }

    [Fact]
    public void Class_PrivateMethod()
    {
        var source = """
            class Foo {
                private doSomething() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(AccessModifier.Private, classStmt.Methods[0].Access);
    }

    [Fact]
    public void Class_ProtectedMethod()
    {
        var source = """
            class Foo {
                protected doSomething() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(AccessModifier.Protected, classStmt.Methods[0].Access);
    }

    [Fact]
    public void Class_PrivateField()
    {
        var source = """
            class Foo {
                private x: number;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(AccessModifier.Private, classStmt.Fields[0].Access);
    }

    #endregion

    #region Static Members

    [Fact]
    public void Class_StaticMethod()
    {
        var source = """
            class Foo {
                static create() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Methods[0].IsStatic);
    }

    [Fact]
    public void Class_StaticField()
    {
        var source = """
            class Foo {
                static count: number = 0;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Fields[0].IsStatic);
    }

    [Fact]
    public void Class_PublicStaticMethod()
    {
        var source = """
            class Foo {
                public static getInstance() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Methods[0].IsStatic);
        Assert.Equal(AccessModifier.Public, classStmt.Methods[0].Access);
    }

    #endregion

    #region Abstract Classes

    [Fact]
    public void Class_Abstract()
    {
        var source = """
            abstract class Foo {
                abstract doSomething(): void;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.IsAbstract);
    }

    [Fact]
    public void Class_AbstractMethod()
    {
        var source = """
            abstract class Foo {
                abstract doSomething(): void;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Methods[0].IsAbstract);
    }

    #endregion

    #region Getters and Setters

    [Fact]
    public void Class_Getter()
    {
        var source = """
            class Foo {
                get value() { return this._value; }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.NotNull(classStmt.Accessors);
        Assert.Single(classStmt.Accessors);
        Assert.Equal("value", classStmt.Accessors[0].Name.Lexeme);
        Assert.Equal("get", classStmt.Accessors[0].Kind.Lexeme);
    }

    [Fact]
    public void Class_Setter()
    {
        var source = """
            class Foo {
                set value(v: number) { this._value = v; }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.NotNull(classStmt.Accessors);
        Assert.Single(classStmt.Accessors);
        Assert.Equal("set", classStmt.Accessors[0].Kind.Lexeme);
    }

    [Fact]
    public void Class_GetterAndSetter()
    {
        var source = """
            class Foo {
                get value() { return this._value; }
                set value(v: number) { this._value = v; }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.NotNull(classStmt.Accessors);
        Assert.Equal(2, classStmt.Accessors.Count);
    }

    #endregion

    #region Readonly Fields

    [Fact]
    public void Class_ReadonlyField()
    {
        var source = """
            class Foo {
                readonly id: number;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Fields[0].IsReadonly);
    }

    [Fact]
    public void Class_PrivateReadonlyField()
    {
        var source = """
            class Foo {
                private readonly _id: number;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Fields[0].IsReadonly);
        Assert.Equal(AccessModifier.Private, classStmt.Fields[0].Access);
    }

    #endregion

    #region Generic Classes

    [Fact]
    public void Class_GenericSingleParam()
    {
        var source = """
            class Box<T> {
                value: T;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.NotNull(classStmt.TypeParams);
        Assert.Single(classStmt.TypeParams);
        Assert.Equal("T", classStmt.TypeParams[0].Name.Lexeme);
    }

    [Fact]
    public void Class_GenericMultipleParams()
    {
        var source = """
            class Pair<K, V> {
                key: K;
                value: V;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.NotNull(classStmt.TypeParams);
        Assert.Equal(2, classStmt.TypeParams.Count);
    }

    [Fact]
    public void Class_GenericWithConstraint()
    {
        var source = """
            class Container<T extends object> {
                value: T;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.NotNull(classStmt.TypeParams);
        Assert.Single(classStmt.TypeParams);
        Assert.NotNull(classStmt.TypeParams[0].Constraint);
    }

    #endregion

    #region Async Methods

    [Fact]
    public void Class_AsyncMethod()
    {
        var source = """
            class Foo {
                async fetchData() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Methods[0].IsAsync);
    }

    [Fact]
    public void Class_PrivateAsyncMethod()
    {
        var source = """
            class Foo {
                private async doWork() { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.True(classStmt.Methods[0].IsAsync);
        Assert.Equal(AccessModifier.Private, classStmt.Methods[0].Access);
    }

    #endregion

    #region Override Keyword

    [Fact]
    public void Class_OverrideMethod()
    {
        var source = """
            class Foo extends Bar {
                override doSomething() { }
            }
            """;
        var statements = Parse(source);
        var fooClass = Assert.IsType<Stmt.Class>(statements[0]);
        Assert.True(fooClass.Methods[0].IsOverride);
    }

    #endregion
}
