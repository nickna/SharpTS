using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Index signatures on instantiated generic interfaces participate in assignability with the
/// instantiation's arguments substituted — in BOTH directions (assignmentCompatWith
/// {String,Numeric}Indexer2). Previously the substitution helpers only handled generic CLASSES,
/// so a generic interface's indexer silently vanished from the comparison.
/// </summary>
public class GenericInterfaceIndexerTests
{
    [Fact]
    public void InstantiatedInterfaceIndexer_AsSource_IsChecked()
    {
        // A<Base> has [x: string]: Base; Base is not assignable to Derived.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: string]: T; }
            var a1: A<Base>;
            var b1: { [x: string]: Derived; };
            b1 = a1;
            """));
        // The compatible direction stays legal.
        TestHarness.RunInterpreted("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: string]: T; }
            var a1: A<Base>;
            var b1: { [x: string]: Derived; };
            a1 = b1;
            """);
    }

    [Fact]
    public void OpenInstantiationIndexer_AsTarget_RejectsConcreteValues()
    {
        // Inside foo<T>, A<T>'s indexer is [x: string]: T — a concrete Derived indexer is not
        // assignable to naked T.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: string]: T; }
            function foo<T extends Base>() {
                var b3: { [x: string]: Derived; };
                var a3: A<T>;
                a3 = b3;
            }
            """));
    }

    [Fact]
    public void InstantiatedInterfaceNumberIndexer_IsChecked()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: number]: T; }
            var a1: A<Base>;
            var b1: { [x: number]: Derived; };
            b1 = a1;
            """));
    }
}
