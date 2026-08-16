using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Measured variance for same-generic instantiations (#202): when a generic interface's type
/// parameters carry no explicit variance annotation, two instantiations relate by the variance
/// MEASURED from the member positions the parameter occupies (tsc's getVariances model) — with
/// the callback rule applied during measurement so callback-parameter positions count as outputs.
/// </summary>
public class MeasuredVarianceTests
{
    [Fact]
    public void CovariantPosition_SubtypeInstantiation_IsAssignable()
    {
        var source = """
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface Producer<T> { value: T; }
            var p: Producer<Base>;
            var q: Producer<Derived>;
            p = q;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void CovariantPosition_SupertypeInstantiation_IsError()
    {
        var source = """
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface Producer<T> { value: T; }
            var p: Producer<Base>;
            var q: Producer<Derived>;
            q = p;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void CallbackParameterPosition_MeasuresCovariant()
    {
        // T appears only in a callback's parameter — an output position (tsc's Promise rule):
        // P<Derived> assignable to P<Base>, not conversely.
        var source = """
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface P<T> {
                then(cb: (value: T) => void): void;
            }
            var a: P<Base>;
            var b: P<Derived>;
            a = b;
            """;
        TestHarness.RunInterpreted(source);

        var reverse = """
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface P<T> {
                then(cb: (value: T) => void): void;
            }
            var a: P<Base>;
            var b: P<Derived>;
            b = a;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(reverse));
    }

    [Fact]
    public void DirectMethodParameterPosition_MeasuresBivariant()
    {
        // T used directly as a method parameter measures bivariantly (tsc's method exemption),
        // so both directions are accepted — TypeScript's well-known method unsoundness.
        var source = """
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface Sink<T> { put(value: T): void; }
            var s: Sink<Base>;
            var t: Sink<Derived>;
            s = t;
            t = s;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void SelfReferentialConstraints_RelateViaApparentTypes()
    {
        // assignmentCompatWithGenericCallSignatures4: x = y is fine (the constraint chain
        // discharges), y = x is not.
        var source = """
            interface I2<T> { p: T }
            var x: <T extends I2<T>>(z: T) => void;
            var y: <T extends I2<I2<T>>>(z: T) => void;
            x = y;
            """;
        TestHarness.RunInterpreted(source);

        var reverse = """
            interface I2<T> { p: T }
            var x: <T extends I2<T>>(z: T) => void;
            var y: <T extends I2<I2<T>>>(z: T) => void;
            y = x;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(reverse));
    }
}
