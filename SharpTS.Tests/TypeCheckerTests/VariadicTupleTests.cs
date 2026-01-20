using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for TypeScript 4.0+ variadic tuple type support.
/// Tests spread element parsing, tuple flattening during substitution,
/// constraint validation, and inference from variadic patterns.
///
/// Note: Full variadic tuple type alias instantiation requires additional
/// work in type alias parsing. These tests cover the foundational type
/// system structures and basic functionality.
/// </summary>
public class VariadicTupleTests
{
    #region Basic Spread Parsing

    // Note: Full type alias instantiation with variadic tuples requires
    // additional work in ParseGenericTypeReference. These tests are skipped
    // until that work is complete.

    [Fact]
    public void SpreadType_InTuple_ParsingSucceeds()
    {
        // Test that ...T syntax is parsed correctly in tuple types
        var source = """
            type Prepend<E, T extends unknown[]> = [E, ...T];
            let x: Prepend<string, [number, boolean]> = ["hello", 42, true];
            console.log(x[0], x[1], x[2]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello 42 true\n", result);
    }

    [Fact]
    public void SpreadType_EmptyTuple_Works()
    {
        var source = """
            type Prepend<E, T extends unknown[]> = [E, ...T];
            let x: Prepend<string, []> = ["hello"];
            console.log(x[0]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    #endregion

    #region Tuple Flattening/Substitution

    // Note: These tests verify the substitution logic works correctly
    // but full integration requires type alias instantiation support.

    [Fact]
    public void VariadicTuple_PrependSubstitution_FlattensCorrectly()
    {
        var source = """
            type Prepend<E, T extends unknown[]> = [E, ...T];
            type Result = Prepend<string, [number, boolean]>;
            let r: Result = ["hello", 42, true];
            console.log(r[0], r[1], r[2]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello 42 true\n", result);
    }

    [Fact]
    public void VariadicTuple_AppendSubstitution_FlattensCorrectly()
    {
        var source = """
            type Append<T extends unknown[], E> = [...T, E];
            type Result = Append<[string, number], boolean>;
            let r: Result = ["hello", 42, true];
            console.log(r[0], r[1], r[2]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello 42 true\n", result);
    }

    [Fact]
    public void VariadicTuple_ConcatSubstitution_FlattensCorrectly()
    {
        var source = """
            type Concat<T extends unknown[], U extends unknown[]> = [...T, ...U];
            type Result = Concat<[string], [number, boolean]>;
            let r: Result = ["a", 1, true];
            console.log(r[0], r[1], r[2]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("a 1 true\n", result);
    }

    [Fact]
    public void VariadicTuple_NestedSubstitution_Works()
    {
        var source = """
            type Prepend<E, T extends unknown[]> = [E, ...T];
            type Inner = Prepend<number, [boolean]>;
            type Outer = Prepend<string, Inner>;
            let r: Outer = ["hello", 42, true];
            console.log(r[0], r[1], r[2]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello 42 true\n", result);
    }

    #endregion

    #region Constraint Validation

    // Note: Constraint validation tests require type alias support to
    // properly trigger the constraint checking.

    [Fact]
    public void VariadicTuple_MissingConstraint_ThrowsError()
    {
        // Without 'extends unknown[]' constraint, spread should error
        var source = """
            type Bad<T> = [...T];
            let x: Bad<[number]> = [42];
            """;

        var exception = Assert.Throws<TypeSystem.Exceptions.TypeCheckException>(() =>
        {
            TestHarness.RunInterpreted(source);
        });
        Assert.Contains("array type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VariadicTuple_WithConstraint_Succeeds()
    {
        var source = """
            type Good<T extends unknown[]> = [...T];
            let x: Good<[number, string]> = [42, "hello"];
            console.log(x[0], x[1]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42 hello\n", result);
    }

    [Fact]
    public void VariadicTuple_ArrayConstraint_Succeeds()
    {
        var source = """
            type WithArray<T extends number[]> = [...T];
            let x: WithArray<number[]> = [1, 2, 3];
            console.log(x.length);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", result);
    }

    #endregion

    #region Tuple Element Structure

    [Fact]
    public void TupleElement_RequiredKind_IsCorrect()
    {
        var elem = new TypeInfo.TupleElement(
            new TypeInfo.String(),
            TupleElementKind.Required,
            "name"
        );

        Assert.True(elem.IsRequired);
        Assert.False(elem.IsOptional);
        Assert.False(elem.IsSpread);
    }

    [Fact]
    public void TupleElement_OptionalKind_IsCorrect()
    {
        var elem = new TypeInfo.TupleElement(
            new TypeInfo.String(),
            TupleElementKind.Optional,
            null
        );

        Assert.False(elem.IsRequired);
        Assert.True(elem.IsOptional);
        Assert.False(elem.IsSpread);
    }

    [Fact]
    public void TupleElement_SpreadKind_IsCorrect()
    {
        var elem = new TypeInfo.TupleElement(
            new TypeInfo.Array(new TypeInfo.String()),
            TupleElementKind.Spread,
            null
        );

        Assert.False(elem.IsRequired);
        Assert.False(elem.IsOptional);
        Assert.True(elem.IsSpread);
    }

    [Fact]
    public void TupleElement_ToString_FormatsCorrectly()
    {
        var requiredWithName = new TypeInfo.TupleElement(
            new TypeInfo.String(),
            TupleElementKind.Required,
            "name"
        );
        Assert.Contains("name:", requiredWithName.ToString());

        var optional = new TypeInfo.TupleElement(
            new TypeInfo.String(),
            TupleElementKind.Optional
        );
        Assert.Contains("?", optional.ToString());

        var spread = new TypeInfo.TupleElement(
            new TypeInfo.Array(new TypeInfo.String()),
            TupleElementKind.Spread
        );
        Assert.Contains("...", spread.ToString());
    }

    #endregion

    #region Tuple Properties

    [Fact]
    public void Tuple_HasSpread_TrueWhenContainsSpread()
    {
        var elements = new List<TypeInfo.TupleElement>
        {
            new(new TypeInfo.String(), TupleElementKind.Required),
            new(new TypeInfo.Array(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER)), TupleElementKind.Spread),
        };
        var tuple = new TypeInfo.Tuple(elements, 1);

        Assert.True(tuple.HasSpread);
    }

    [Fact]
    public void Tuple_HasSpread_FalseWhenNoSpread()
    {
        var elements = new List<TypeInfo.TupleElement>
        {
            new(new TypeInfo.String(), TupleElementKind.Required),
            new(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER), TupleElementKind.Optional),
        };
        var tuple = new TypeInfo.Tuple(elements, 1);

        Assert.False(tuple.HasSpread);
    }

    [Fact]
    public void Tuple_MaxLength_NullWhenHasSpread()
    {
        var elements = new List<TypeInfo.TupleElement>
        {
            new(new TypeInfo.String(), TupleElementKind.Required),
            new(new TypeInfo.Array(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER)), TupleElementKind.Spread),
        };
        var tuple = new TypeInfo.Tuple(elements, 1);

        Assert.Null(tuple.MaxLength);
    }

    [Fact]
    public void Tuple_ConcreteElementTypes_ExcludesSpreads()
    {
        var elements = new List<TypeInfo.TupleElement>
        {
            new(new TypeInfo.String(), TupleElementKind.Required),
            new(new TypeInfo.Array(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER)), TupleElementKind.Spread),
            new(new TypeInfo.Primitive(Parsing.TokenType.TYPE_BOOLEAN), TupleElementKind.Required),
        };
        var tuple = new TypeInfo.Tuple(elements, 2);

        var concrete = tuple.ConcreteElementTypes.ToList();
        Assert.Equal(2, concrete.Count);
        Assert.IsType<TypeInfo.String>(concrete[0]);
        Assert.IsType<TypeInfo.Primitive>(concrete[1]);
    }

    #endregion

    #region SpreadType Record

    [Fact]
    public void SpreadType_ToString_FormatsCorrectly()
    {
        var spread = new TypeInfo.SpreadType(
            new TypeInfo.Array(new TypeInfo.String())
        );

        Assert.StartsWith("...", spread.ToString());
    }

    [Fact]
    public void SpreadType_Inner_PreservedCorrectly()
    {
        var inner = new TypeInfo.Array(new TypeInfo.String());
        var spread = new TypeInfo.SpreadType(inner);

        Assert.Equal(inner, spread.Inner);
    }

    #endregion

    #region Multiple Spreads

    [Fact]
    public void VariadicTuple_MultipleSpreads_Works()
    {
        var source = """
            type Surround<T extends unknown[], U extends unknown[]> = [...T, string, ...U];
            type Result = Surround<[number], [boolean]>;
            let r: Result = [42, "middle", true];
            console.log(r[0], r[1], r[2]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42 middle true\n", result);
    }

    #endregion

    #region Factory Method

    [Fact]
    public void Tuple_FromTypes_CreatesCorrectElements()
    {
        var types = new List<TypeInfo>
        {
            new TypeInfo.String(),
            new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER),
        };
        var names = new List<string?> { "name", null };

        var tuple = TypeInfo.Tuple.FromTypes(types, 1, null, names);

        Assert.Equal(2, tuple.Elements.Count);
        Assert.Equal("name", tuple.Elements[0].Name);
        Assert.Null(tuple.Elements[1].Name);
        Assert.Equal(TupleElementKind.Required, tuple.Elements[0].Kind);
        Assert.Equal(TupleElementKind.Optional, tuple.Elements[1].Kind);
        Assert.Equal(1, tuple.RequiredCount);
    }

    [Fact]
    public void Tuple_FromTypes_AllRequired()
    {
        var types = new List<TypeInfo>
        {
            new TypeInfo.String(),
            new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER),
        };

        var tuple = TypeInfo.Tuple.FromTypes(types, 2);

        Assert.All(tuple.Elements, e => Assert.Equal(TupleElementKind.Required, e.Kind));
        Assert.Equal(2, tuple.RequiredCount);
    }

    #endregion
}
