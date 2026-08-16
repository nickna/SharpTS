using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Round-5 fixes (#226 parser-gap cluster): export declare, constructor types as generic type
/// arguments, conditional types as object-member values, bigint literal types,
/// typeof undefined, TS2539 for assigning to undefined, the any-check-type conditional rule,
/// and union normalization (any absorbs, never drops).
/// </summary>
public class Round5ParserGapsTests
{
    [Fact]
    public void ExportDeclareFunction_Parses()
    {
        TestHarness.RunInterpreted("""
            export declare function foo<T>(obj: T): T;
            """);
    }

    [Fact]
    public void ConstructorType_AsGenericTypeArgument_Parses()
    {
        TestHarness.RunInterpreted("""
            type A3 = InstanceType<new (x: string) => string>;
            type A5 = InstanceType<abstract new (x: string) => string>;
            """);
    }

    [Fact]
    public void ConditionalType_AsObjectMemberValue_Parses()
    {
        TestHarness.RunInterpreted("""
            type BadNested<T> = { x: T extends number ? T : string };
            """);
    }

    [Fact]
    public void BigintLiteralTypes_Parse()
    {
        TestHarness.RunInterpreted("""
            type B = 1n | 2n;
            """);
    }

    [Fact]
    public void AssigningToUndefined_IsTs2539_NotParseError()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var x = undefined = null;
            """));
    }

    [Fact]
    public void TypeofUndefined_Resolves()
    {
        // Under the product's strictNullChecks default, null is not assignable to undefined —
        // the point here is that `typeof undefined` PARSES and resolves to the undefined type.
        TestHarness.RunInterpreted("""
            var y: typeof undefined = undefined;
            """);
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var y: typeof undefined = "not undefined";
            """));
    }

    [Fact]
    public void AnyCheckType_ConditionalYieldsBothBranches()
    {
        // `any extends infer U ? U : never` is any | never = any — null assignable.
        TestHarness.RunInterpreted("""
            type Weird = any extends infer U ? U : never;
            const a: Weird = null;
            const b: string = a;
            """);
    }

    [Fact]
    public void UnionNormalization_AnyAbsorbs_NeverDrops()
    {
        TestHarness.RunInterpreted("""
            const c1: any | never = null;
            function probe(x: string | never) {
                let s: string = x;
            }
            """);
    }
}
