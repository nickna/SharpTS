using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class CaseDistinctClassFieldTests
{
    [Fact]
    public void CollidingClrPropertiesAreDistinctWhileOrdinaryFieldsRetainTheirAbi()
    {
        const string source = "class Values { x: number = 1; X: number = 2; value: number = 3; }";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"case_distinct_fields_{Guid.NewGuid():N}");
        compiler.Compile(statements, types, new DeadCodeAnalyzer(types).Analyze(statements));
        byte[] bytes = compiler.SaveToBytes();
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        var assembly = Assembly.Load(bytes);
        var owner = assembly.GetType("Values")!;
        var constructor = Assert.Single(owner.GetConstructors(), candidate => candidate.GetParameters().Length == 0);
        object instance = constructor.Invoke([]);
        Assert.Equal(1d, owner.GetProperty("x")!.GetValue(instance));
        Assert.Equal(2d, owner.GetProperty("X")!.GetValue(instance));
        Assert.Equal(typeof(double), owner.GetProperty("Value")!.PropertyType);
        owner.GetProperty("Value")!.SetValue(instance, 7d);
        Assert.Equal(7d, owner.GetMethod("GetProperty")!.Invoke(instance, ["value"]));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
    }

    [Theory, ModeData]
    public void CaseDistinctFieldsRetainSeparateValuesDescriptorsAndWrites(ExecutionMode mode)
    {
        Assert.Equal("1 2\n3 4\n3 4\nx,X\ntrue 4\n5 4\n", TestHarness.Run("""
            class Values { x: number = 1; X: number = 2; }
            const value = new Values();
            console.log(value.x, value.X);
            value.x = 3;
            value.X = 4;
            console.log(value.x, value.X);
            console.log(Object.getOwnPropertyDescriptor(value, 'x')!.value, Object.getOwnPropertyDescriptor(value, 'X')!.value);
            console.log(Object.keys(value).join(','));
            const dynamic: any = value;
            delete dynamic['x'];
            console.log(dynamic['x'] === undefined, dynamic['X']);
            dynamic['x'] = 5;
            console.log(dynamic['x'], dynamic['X']);
            """, mode));
    }

    [Theory, ModeData]
    public void GenericOwnersAndClassExpressionsUseExactGuestKeys(ExecutionMode mode)
    {
        Assert.Equal("left right\nnext right\n1 2\n", TestHarness.Run("""
            class Values<T> { constructor(public x: T, public X: T) {} }
            const value = new Values<string>('left', 'right');
            console.log(value.x, value.X);
            value.x = 'next';
            console.log(value.x, value.X);
            const C = class { x: number = 1; X: number = 2; };
            const dynamic: any = new C();
            console.log(dynamic['x'], dynamic['X']);
            """, mode));
    }

    [Theory, ModeData]
    public void InheritedCaseDistinctFieldsKeepTheirOwnerMapping(ExecutionMode mode)
    {
        Assert.Equal("1 2 3\n4 2 3\n4 2\n", TestHarness.Run("""
            class Base { x: number = 1; X: number = 2; }
            class Derived extends Base { y: number = 3; }
            const value = new Derived();
            console.log(value.x, value.X, value.y);
            value.x = 4;
            console.log(value.x, value.X, value.y);
            const dynamic: any = value;
            console.log(dynamic['x'], dynamic['X']);
            """, mode));
    }

    [Theory, ModeData]
    public void CaseDistinctFieldsAcrossOwnersPreserveSameKeyOverrides(ExecutionMode mode)
    {
        Assert.Equal("3 2\n4 5 4\n4 5\nx,X\n", TestHarness.Run("""
            class Base { x: number = 1; }
            class Middle extends Base { X: number = 2; }
            class Leaf extends Middle { x: number = 3; }
            const value = new Leaf();
            console.log(value.x, value.X);
            value.x = 4;
            value.X = 5;
            const base: Base = value;
            console.log(value.x, value.X, base.x);
            const dynamic: any = value;
            console.log(dynamic['x'], dynamic['X']);
            console.log(Object.keys(value).join(','));
            """, mode));
    }

    [Theory, ModeData]
    public void LocalCollisionsReuseOnlyTheirExactInheritedKey(ExecutionMode mode)
    {
        Assert.Equal("2 3\n4 3\nfirst upper\nnext upper\n", TestHarness.Run("""
            class Base { x: number = 1; }
            class Child extends Base { x: number = 2; X: number = 3; }
            const value = new Child();
            const base: Base = value;
            console.log(value.x, value.X);
            value.x = 4;
            console.log(base.x, value.X);
            class Generic<T> { constructor(public x: T) {} }
            class Derived extends Generic<string> { X: string = 'upper'; }
            const generic = new Derived('first');
            console.log(generic.x, generic.X);
            generic.x = 'next';
            console.log(generic.x, generic.X);
            """, mode));
    }

    [Theory, ModeData]
    public void QuotedFieldKeysCannotCollideWithGeneratedClrAliases(ExecutionMode mode)
    {
        Assert.Equal("1 2 3 4\n1 5 6 4\n1 2 3 4\n", TestHarness.Run("""
            class Base { x: number = 1; }
            class Child extends Base { X: number = 2; '<field>X': number = 3; '<field>X$1': number = 4; }
            const value = new Child();
            console.log(value.x, value.X, value['<field>X'], value['<field>X$1']);
            value.X = 5;
            value['<field>X'] = 6;
            console.log(value.x, value.X, value['<field>X'], value['<field>X$1']);
            class Reverse extends Base { '<field>X$1': number = 4; '<field>X': number = 3; X: number = 2; }
            const reverse = new Reverse();
            console.log(reverse.x, reverse.X, reverse['<field>X'], reverse['<field>X$1']);
            """, mode));
    }
}
