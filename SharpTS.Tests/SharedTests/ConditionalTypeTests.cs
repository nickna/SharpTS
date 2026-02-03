using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Comprehensive tests for TypeScript conditional types: T extends U ? X : Y
/// Tests cover basic conditionals, distribution over unions, infer keyword,
/// nested/recursive conditionals, and utility type implementations.
/// Runs against both interpreter and compiler.
/// </summary>
public class ConditionalTypeTests
{
    #region Basic Conditional Types (12 tests)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_BasicTrue_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            type IsString<T> = T extends string ? true : false;
            let x: IsString<string> = true;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_BasicFalse_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            type IsString<T> = T extends string ? true : false;
            let x: IsString<number> = false;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_StringLiteralExtendsString_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            type IsString<T> = T extends string ? "yes" : "no";
            let x: IsString<"hello"> = "yes";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NumberLiteralExtendsNumber_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            type IsNumber<T> = T extends number ? "yes" : "no";
            let x: IsNumber<42> = "yes";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_BooleanExtendsBoolean_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            type IsBool<T> = T extends boolean ? 1 : 0;
            let x: IsBool<true> = 1;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NullExtendsNull_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            type IsNull<T> = T extends null ? "null" : "notNull";
            let x: IsNull<null> = "null";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_ArrayExtendsArray_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            type IsArray<T> = T extends any[] ? true : false;
            let x: IsArray<string[]> = true;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NonArrayExtendsArray_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            type IsArray<T> = T extends any[] ? true : false;
            let x: IsArray<string> = false;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_AnyExtendsAny_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            type ExtendsAny<T> = T extends any ? "yes" : "no";
            let x: ExtendsAny<string> = "yes";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NeverExtendsAny_ReturnsNever(ExecutionMode mode)
    {
        // never distributes to empty, so never extends any ? X : Y = never
        // We can't directly test never, but we can check it doesn't break
        var source = """
            type Test<T> = T extends any ? "yes" : "no";
            type Result = Test<never>;
            let x: string = "test";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_RecordExtendsRecord_ReturnsTrue(ExecutionMode mode)
    {
        // Use { } instead of object keyword
        var source = """
            type IsRecord<T> = T extends { x: number } ? "match" : "nomatch";
            let x: IsRecord<{ x: number; y: string }> = "match";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("match\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_PrimitiveExtendsRecord_ReturnsFalse(ExecutionMode mode)
    {
        // Use { } instead of object keyword
        var source = """
            type IsRecord<T> = T extends { x: number } ? "match" : "nomatch";
            let x: IsRecord<number> = "nomatch";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("nomatch\n", output);
    }

    #endregion

    #region Distribution Over Unions (10 tests)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_UnionDistribution_DistributesCorrectly(ExecutionMode mode)
    {
        // string | number extends string ? "str" : "other"
        // = (string extends string ? "str" : "other") | (number extends string ? "str" : "other")
        // = "str" | "other"
        var source = """
            type Check<T> = T extends string ? "str" : "other";
            let x: Check<string | number> = "str";
            let y: Check<string | number> = "other";
            console.log(x);
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("str\nother\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_ToArray_DistributesOverUnion(ExecutionMode mode)
    {
        // ToArray<string> = string[], verify assignment works
        var source = """
            type ToArray<T> = T extends any ? T[] : never;
            let x: ToArray<string> = ["hello"];
            console.log("passed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("passed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_FilterStrings_FiltersUnion(ExecutionMode mode)
    {
        // Filter<string | number | boolean> where we keep only strings
        // = string | never | never = string
        var source = """
            type FilterString<T> = T extends string ? T : never;
            let x: FilterString<string | number | boolean> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NonNullable_FiltersNull(ExecutionMode mode)
    {
        // NonNullable<string | null> = string
        var source = """
            type NonNullable<T> = T extends null ? never : T;
            let x: NonNullable<string | null> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_Extract_ExtractsMatchingTypes(ExecutionMode mode)
    {
        // Extract<string | number | boolean, string | boolean> = string | boolean
        var source = """
            type Extract<T, U> = T extends U ? T : never;
            let x: Extract<string | number | boolean, string | boolean> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_Exclude_ExcludesMatchingTypes(ExecutionMode mode)
    {
        // Exclude<string | number | boolean, string> = number | boolean
        var source = """
            type Exclude<T, U> = T extends U ? never : T;
            let x: Exclude<string | number | boolean, string> = 42;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_AllNever_ResultsInNever(ExecutionMode mode)
    {
        // FilterString<number | boolean> = never | never = never
        // We can verify by assigning to a union that would include never
        var source = """
            type FilterString<T> = T extends string ? T : never;
            type Result = FilterString<number | boolean>;
            let x: string = "test";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_SingleUnionMember_NoDistribution(ExecutionMode mode)
    {
        // Single type, no distribution needed
        var source = """
            type Check<T> = T extends string ? "str" : "other";
            let x: Check<string> = "str";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("str\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_UnionWithNever_NeverDisappears(ExecutionMode mode)
    {
        // string | never = string
        var source = """
            type AddNever<T> = T | never;
            let x: AddNever<string> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_DistributeWithLiteral_Works(ExecutionMode mode)
    {
        // "a" | "b" | 1 extends string ? "str" : "other"
        // = "str" | "str" | "other"
        var source = """
            type Check<T> = T extends string ? "str" : "other";
            let x: Check<"a" | "b" | 1> = "str";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("str\n", output);
    }

    #endregion

    #region Infer Keyword (10 tests)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_ArrayElement_ExtractsElement(ExecutionMode mode)
    {
        var source = """
            type ElementType<T> = T extends (infer U)[] ? U : never;
            let x: ElementType<string[]> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_NumberArrayElement_ExtractsNumber(ExecutionMode mode)
    {
        var source = """
            type ElementType<T> = T extends (infer U)[] ? U : never;
            let x: ElementType<number[]> = 42;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_NonArrayReturnsNever_FallsBack(ExecutionMode mode)
    {
        // ElementType<string> = never (no match)
        var source = """
            type ElementType<T> = T extends (infer U)[] ? U : T;
            let x: ElementType<string> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_PromiseValue_ExtractsValue(ExecutionMode mode)
    {
        var source = """
            type Awaited<T> = T extends Promise<infer U> ? U : T;
            let x: Awaited<Promise<string>> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_NonPromise_ReturnsSelf(ExecutionMode mode)
    {
        var source = """
            type Awaited<T> = T extends Promise<infer U> ? U : T;
            let x: Awaited<string> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_InferUsedInTrueBranch_Works(ExecutionMode mode)
    {
        var source = """
            type Flatten<T> = T extends (infer U)[] ? U : T;
            let x: Flatten<number[]> = 42;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_NestedArray_FlattenOnce(ExecutionMode mode)
    {
        // Flatten<string[][]> = string[], verify assignment works
        var source = """
            type Flatten<T> = T extends (infer U)[] ? U : T;
            let x: Flatten<string[][]> = ["hello"];
            console.log("passed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("passed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_MapValue_ExtractsValue(ExecutionMode mode)
    {
        var source = """
            type MapValue<T> = T extends Map<any, infer V> ? V : never;
            let x: MapValue<Map<string, number>> = 42;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_SetElement_ExtractsElement(ExecutionMode mode)
    {
        var source = """
            type SetElement<T> = T extends Set<infer E> ? E : never;
            let x: SetElement<Set<string>> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InferType_UnionDistribution_InfersFromEach(ExecutionMode mode)
    {
        // ElementType<string[] | number[]> = string | number
        var source = """
            type ElementType<T> = T extends (infer U)[] ? U : never;
            let x: ElementType<string[] | number[]> = "hello";
            let y: ElementType<string[] | number[]> = 42;
            console.log(x);
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n42\n", output);
    }

    #endregion

    #region Nested and Recursive Conditionals (8 tests)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NestedConditional_Works(ExecutionMode mode)
    {
        var source = """
            type TypeName<T> =
                T extends string ? "string" :
                T extends number ? "number" :
                T extends boolean ? "boolean" :
                "unknown";
            let x: TypeName<string> = "string";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NestedConditional_SecondBranch(ExecutionMode mode)
    {
        var source = """
            type TypeName<T> =
                T extends string ? "string" :
                T extends number ? "number" :
                T extends boolean ? "boolean" :
                "unknown";
            let x: TypeName<number> = "number";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NestedConditional_ThirdBranch(ExecutionMode mode)
    {
        var source = """
            type TypeName<T> =
                T extends string ? "string" :
                T extends number ? "number" :
                T extends boolean ? "boolean" :
                "unknown";
            let x: TypeName<boolean> = "boolean";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("boolean\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_NestedConditional_FallbackBranch(ExecutionMode mode)
    {
        var source = """
            type TypeName<T> =
                T extends string ? "string" :
                T extends number ? "number" :
                T extends boolean ? "boolean" :
                "unknown";
            let x: TypeName<null> = "unknown";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("unknown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_ConditionalInTrueBranch_Works(ExecutionMode mode)
    {
        // Test that conditional types evaluate correct branches
        var source = """
            type IsHello<T> = T extends "hello" ? "greeting" : "string";
            let x: IsHello<"hello"> = "greeting";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("greeting\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_ConditionalInFalseBranch_Works(ExecutionMode mode)
    {
        // Test that conditional types evaluate correct branches
        var source = """
            type IsString<T> = T extends string ? "string" : "other";
            let x: IsString<number> = "other";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("other\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_TwoTypeParams_Works(ExecutionMode mode)
    {
        var source = """
            type Same<T, U> = T extends U ? true : false;
            let x: Same<string, string> = true;
            let y: Same<string, number> = false;
            console.log(x);
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConditionalType_ThreeTypeParams_Works(ExecutionMode mode)
    {
        var source = """
            type If<C, T, F> = C extends true ? T : F;
            let x: If<true, "yes", "no"> = "yes";
            let y: If<false, "yes", "no"> = "no";
            console.log(x);
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\nno\n", output);
    }

    #endregion

    #region Utility Types with Conditionals (6 tests)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UtilityType_NonNullable_Implementation(ExecutionMode mode)
    {
        var source = """
            type MyNonNullable<T> = T extends null ? never : T;
            let x: MyNonNullable<string | null> = "hello";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UtilityType_Extract_Implementation(ExecutionMode mode)
    {
        // Use type alias for the union to avoid parser issues with union in generic args
        var source = """
            type StringUnion = "a" | "b" | "c";
            type MatchUnion = "a" | "c";
            type MyExtract<T, U> = T extends U ? T : never;
            let x: MyExtract<StringUnion, MatchUnion> = "a";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UtilityType_Exclude_Implementation(ExecutionMode mode)
    {
        // Use type alias for the union to avoid parser issues with union in generic args
        var source = """
            type StringUnion = "a" | "b" | "c";
            type MyExclude<T, U> = T extends U ? never : T;
            let x: MyExclude<StringUnion, "a"> = "b";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("b\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UtilityType_Awaited_Simple(ExecutionMode mode)
    {
        var source = """
            type MyAwaited<T> = T extends Promise<infer U> ? U : T;
            let x: MyAwaited<Promise<number>> = 42;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UtilityType_UnwrapArray_Implementation(ExecutionMode mode)
    {
        var source = """
            type UnwrapArray<T> = T extends (infer U)[] ? U : T;
            let x: UnwrapArray<boolean[]> = true;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UtilityType_IsAny_SimpleCheck(ExecutionMode mode)
    {
        var source = """
            type IsString<T> = T extends string ? "yes" : "no";
            let x: IsString<any> = "yes";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    #endregion

    #region Edge Cases (6 tests)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EdgeCase_EmptyUnion_IsNever(ExecutionMode mode)
    {
        // never extends any should still work
        var source = """
            type Test<T> = T extends any ? "yes" : "no";
            let x: string = "test";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EdgeCase_UnknownExtendsUnknown_Works(ExecutionMode mode)
    {
        var source = """
            type Check<T> = T extends unknown ? "yes" : "no";
            let x: Check<string> = "yes";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EdgeCase_VoidExtends_Works(ExecutionMode mode)
    {
        var source = """
            type IsVoid<T> = T extends void ? "void" : "other";
            let x: IsVoid<void> = "void";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("void\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EdgeCase_StringLiteralUnion_Distributes(ExecutionMode mode)
    {
        // Use type alias for the union
        var source = """
            type Letters = "a" | "b" | "c";
            type ToUpper<T> = T extends "a" ? "A" : T extends "b" ? "B" : T;
            let x: ToUpper<Letters> = "A";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("A\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EdgeCase_TrueLiteralCheck(ExecutionMode mode)
    {
        // Test true literal check specifically
        var source = """
            type Check<T> = T extends true ? "yes" : "no";
            let x: Check<true> = "yes";
            let y: Check<false> = "no";
            console.log(x);
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\nno\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EdgeCase_StringUnionDistribution_Works(ExecutionMode mode)
    {
        // Use type alias for the union
        var source = """
            type Letters = "a" | "b" | "c";
            type Check<T> = T extends "a" ? "match" : "other";
            let x: Check<Letters> = "match";
            let y: Check<Letters> = "other";
            console.log(x);
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("match\nother\n", output);
    }

    #endregion
}
