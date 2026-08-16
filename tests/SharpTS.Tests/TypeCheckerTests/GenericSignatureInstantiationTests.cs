using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Contextual signature instantiation (#188 Step 2): relating generic signatures by inferring the
/// source's type parameters from the target — including both-generic relating (target parameters
/// stay rigid), constraint clamping, inference-conflict rigidity, alpha-renaming against free type
/// parameters, and the erased relating rule for overloaded sides. Mirrors tsc's
/// <c>signatureRelatedTo</c>/<c>signaturesRelatedTo</c> on the
/// <c>assignmentCompatWith*Signatures*</c> conformance cluster.
/// </summary>
public class GenericSignatureInstantiationTests
{
    [Fact]
    public void BothGeneric_IdenticalConstrainedSignatures_AreCompatible()
    {
        var source = """
            class Base { foo: string; }
            var p: <T extends Base>(x: T) => T;
            var q: <T extends Base>(x: T) => T;
            p = q;
            """;
        TestHarness.RunInterpreted(source); // must not throw
    }

    [Fact]
    public void BothGeneric_ReturnShapeMismatch_IsError()
    {
        // S inferred as T; instantiated source returns T[] where the target wants T.
        var source = """
            var e: <T>(x: T) => T;
            var f: <S>(x: S) => S[];
            e = f;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BothGeneric_RestParameterStructureMismatch_IsError_BothDirections()
    {
        // assignmentCompatWithGenericCallSignatures2: <T>(x: T, ...y: T[][]) vs <S>(x: S, ...y: S[]).
        var source = """
            interface A { <T>(x: T, ...y: T[][]): void }
            interface B { <S>(x: S, ...y: S[]): void }
            var a: A;
            var b: B;
            a = b;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        var reverse = """
            interface A { <T>(x: T, ...y: T[][]): void }
            interface B { <S>(x: S, ...y: S[]): void }
            var a: A;
            var b: B;
            b = a;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(reverse));
    }

    [Fact]
    public void InferenceConflict_KeepsParameterRigid_IsError()
    {
        // T is matched against both U and V — incomparable rigid parameters — so T stays rigid
        // and the relation fails (tsc errors here too).
        var source = """
            var a15: <T>(x: { a: T; b: T }) => T[];
            var b15: <U, V>(x: { a: U; b: V; }) => U[];
            b15 = a15;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void InferenceConflict_OtherDirection_IsCompatible()
    {
        // U and V each infer to T consistently.
        var source = """
            var a15: <T>(x: { a: T; b: T }) => T[];
            var b15: <U, V>(x: { a: U; b: V; }) => U[];
            a15 = b15;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ConstraintViolatingInference_ClampsToConstraint()
    {
        // T infers to Base which violates `extends Derived`; tsc clamps T to Derived and the
        // shapes then relate (assignmentCompatWithCallSignatures4 a10 = b10).
        var source = """
            class Base { foo: string; }
            class Derived extends Base { bar: string; }
            var a10: (...x: Base[]) => Base;
            var b10: <T extends Derived>(...x: T[]) => T;
            a10 = b10;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ConcreteSource_AssignedToGenericTarget_IsError()
    {
        var source = """
            var a: (x: number) => number[];
            var b: <T>(x: T) => T[];
            b = a;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void GenericSource_InstantiatesAgainstConcreteTarget()
    {
        var source = """
            var a: (x: number) => number[];
            var b: <T>(x: T) => T[];
            a = b;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void SignatureTypeParameter_DoesNotCapture_FreeTypeParameterOfSameName()
    {
        // The signature's bound T must not unify with foo's free T by name
        // (assignmentCompatWithGenericCallSignaturesWithOptionalParameters).
        var source = """
            class Base2 { a: <T>() => T; }
            class Target<T> { a: () => T; }
            function foo<T>() {
                var b: Base2;
                var t: Target<T>;
                b.a = t.a;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void OverloadedTarget_RelatesGenericSourceErased()
    {
        // Either side overloaded → erased relating (tsc signaturesRelatedTo), so a generic single-
        // signature source satisfies a two-overload target it couldn't satisfy instantiated.
        var source = """
            var a15: {
                (x: number): number[];
                (x: string): string[];
            };
            var b15: <T>(x: T) => T[];
            a15 = b15;
            b15 = a15;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void GenericInterfaceCallSignature_ParametersResolveToTypeParameters()
    {
        // Interface call-signature type params must be in scope for the member types — T must not
        // collapse to any (which made <T>(x: T): T relate vacuously).
        var source = """
            interface E { <T>(x: T): T }
            interface F { <S>(x: S): S[] }
            var e: E;
            var f: F;
            e = f;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void OptionalParameterArity_TooManyRequiredParams_IsError_AfterSimilarOkAssignment()
    {
        // Regression guard for the ToString cache collision: `(x?: number) => 1` and
        // `(x: number) => 1` rendered identically, so the second (invalid) assignment reused the
        // first one's cached "compatible" verdict.
        var source = """
            var a: () => number;
            a = (x?: number) => 1;
            a = (x: number) => 1;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ArrayGenericSyntax_IsNotAny()
    {
        var source = """
            class Base { foo: string; }
            var q: Array<Base>;
            q = 1;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void FunctionTypeWithGenericArgumentInParams_ParsesAsFunction()
    {
        // `(y: Array<Base>) => void` must not be mistaken for a generic type reference.
        var source = """
            class Base { foo: string; }
            var c: (y: Array<Base>) => void;
            c = 1;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void TwoCallSignatureObjectType_SplitsBothMembers()
    {
        // SplitObjectMembers regression: the '>' of '=>' drove bracket depth negative, fusing the
        // two signatures into one unparseable member.
        var source = """
            var q: { (x: number): number[]; (x: string): string[] };
            var f: (x: boolean) => boolean;
            q = f;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ParameterBivariance_AllowsNarrowerSourceParameter()
    {
        // tsc's default (pre-strictFunctionTypes) parameter relation is bivariant.
        var source = """
            class Base { foo: string; }
            class Derived extends Base { bar: string; }
            var a: (x: Base) => void;
            var b: (x: Derived) => void;
            a = b;
            b = a;
            """;
        TestHarness.RunInterpreted(source);
    }
}
