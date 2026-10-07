using System.Collections.Frozen;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorTypeRendererTests
{
    [Fact]
    public void UnavailableInferenceIsDistinctFromRealAnyAndClassValueFromInstance()
    {
        Assert.Equal("any", EditorTypeRenderer.Render(TypeInfo.Any.Shared).Text);
        Assert.True(EditorTypeRenderer.Render(TypeInfo.Any.Shared).IsAvailable);
        Assert.False(EditorTypeRenderer.Render(new TypeInfo.Inferred()).IsAvailable);
        var type = new TypeInfo.MutableClass("Model").Freeze();
        Assert.Equal("typeof Model", EditorTypeRenderer.Render(type, EditorTypeRenderContext.Value).Text);
        Assert.Equal("Model", EditorTypeRenderer.Render(new TypeInfo.Instance(type), EditorTypeRenderContext.Value).Text);
        Assert.DoesNotContain("$ClassExpr_", EditorTypeRenderer.Render(new TypeInfo.MutableClass("$ClassExpr_12")).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicOverloadsDoNotExposeImplementationAndParameterRangesAreExact()
    {
        var first = new TypeInfo.Function([TypeInfo.Primitive.Number], TypeInfo.String.Shared, ParamNames: ["input"]);
        var second = new TypeInfo.Function([TypeInfo.String.Shared], TypeInfo.Primitive.Number, ParamNames: ["text"]);
        var implementation = new TypeInfo.Function([TypeInfo.Any.Shared], TypeInfo.Any.Shared, ParamNames: ["secretImplementation"]);
        string text = EditorTypeRenderer.Render(new TypeInfo.OverloadedFunction([first, second], implementation)).Text;
        Assert.Contains("input: number", text, StringComparison.Ordinal);
        Assert.Contains("text: string", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secretImplementation", text, StringComparison.Ordinal);
        var signature = EditorTypeRenderer.RenderSignature(new TypeInfo.Function([TypeInfo.Primitive.Number,
            new TypeInfo.Array(TypeInfo.String.Shared)], TypeInfo.Void.Shared, RequiredParams: 1, HasRestParam: true,
            ParamNames: ["first", "rest"], ThisType: TypeInfo.Any.Shared));
        Assert.Equal("first: number", signature.Label[signature.Parameters[0].LabelRange.Start..signature.Parameters[0].LabelRange.End]);
        Assert.Equal("...rest: Array<string>", signature.Label[signature.Parameters[1].LabelRange.Start..signature.Parameters[1].LabelRange.End]);
        Assert.Equal(2, signature.Parameters.Count);
    }

    [Fact]
    public void SubstitutionIsPureAndMethodTypeParametersShadowClassMappings()
    {
        var outer = new TypeInfo.TypeParameter("T");
        var inner = new TypeInfo.TypeParameter("T");
        var substitutions = new Dictionary<string, TypeInfo> { ["T"] = TypeInfo.Primitive.Number };
        Assert.Equal("Array<number>", EditorTypeRenderer.Render(new TypeInfo.Array(outer), substitutions: substitutions).Text);
        var method = new TypeInfo.GenericFunction([inner], [inner], inner, ParamNames: ["item"]);
        var signature = EditorTypeRenderer.RenderSignature(method, substitutions: substitutions);
        Assert.Equal("<T>(item: T): T", signature.Label);
        Assert.Same(inner, method.ParamTypes[0]);
        var mapped = new TypeInfo.MappedType("T", outer, inner);
        Assert.Equal("{ [T in number]: T }", EditorTypeRenderer.Render(mapped, substitutions: substitutions).Text);
    }

    [Fact]
    public void EnumTypedValuesDoNotImplyAnEnumNamespaceValue()
    {
        var type = new TypeInfo.Enum("Choice", new Dictionary<string, object> { ["First"] = 0 }.ToFrozenDictionary(), EnumKind.Numeric);
        Assert.Equal("Choice", EditorTypeRenderer.Render(type).Text);
        Assert.Equal("Choice", EditorTypeRenderer.Render(type, EditorTypeRenderContext.Value).Text);
    }

    [Fact]
    public void CyclesWideStructuresAndLongLiteralsRespectBounds()
    {
        var fields = new Dictionary<string, TypeInfo>();
        var cycle = new TypeInfo.Union([]);
        cycle.Types.Add(cycle);
        var rendered = EditorTypeRenderer.Render(cycle);
        Assert.True(rendered.IsTruncated);
        for (int i = 0; i < 1000; i++) fields["field" + i] = new TypeInfo.StringLiteral(new string('x', 10000));
        rendered = EditorTypeRenderer.Render(new TypeInfo.Record(fields.ToFrozenDictionary()), limits: new(MaxNodes: 8, MaxCharacters: 64));
        Assert.True(rendered.Text.Length <= 64);
        Assert.True(rendered.IsTruncated);
        TypeInfo deep = TypeInfo.Any.Shared;
        for (int i = 0; i < 100; i++) deep = new TypeInfo.Array(deep);
        Assert.True(EditorTypeRenderer.Render(deep, limits: new(MaxDepth: 3)).IsTruncated);
    }

    [Fact]
    public void ConstructorProjectionUsesConstructedTypeInsteadOfVoid()
    {
        var type = new TypeInfo.MutableClass("Model").Freeze();
        var signature = EditorTypeRenderer.RenderSignature(new TypeInfo.Function([], TypeInfo.Void.Shared),
            isConstructor: true, constructedType: new TypeInfo.Instance(type));
        Assert.Equal("new (): Model", signature.Label);
    }

    [Fact]
    public void BroadFunctionAndCappedRecordSignaturesHaveHonestPresentations()
    {
        Assert.Equal("Function", EditorTypeRenderer.Render(new TypeInfo.FunctionSupertype()).Text);
        var calls = Enumerable.Range(0, 3).Select(_ => new TypeInfo.CallSignature(null, [], TypeInfo.Void.Shared)).ToList();
        var constructors = Enumerable.Range(0, 3).Select(_ => new TypeInfo.ConstructorSignature(null, [], TypeInfo.Object.Shared)).ToList();
        var record = new TypeInfo.Record(new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            CallSignatures: calls, ConstructorSignatures: constructors);
        var presentation = EditorTypeRenderer.Render(record, limits: new(MaxCandidates: 2));
        Assert.True(presentation.IsTruncated);
        Assert.Contains("…", presentation.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CharacterBudgetNeverSplitsASurrogatePair()
    {
        var presentation = EditorTypeRenderer.Render(new TypeInfo.StringLiteral(new string('x', 14) + "😀"),
            limits: new(MaxCharacters: 16));
        Assert.True(presentation.IsTruncated);
        Assert.True(presentation.Text.Length <= 16);
        Assert.False(char.IsHighSurrogate(presentation.Text[^1]));
        Assert.DoesNotContain(presentation.Text, char.IsLowSurrogate);
    }
}
