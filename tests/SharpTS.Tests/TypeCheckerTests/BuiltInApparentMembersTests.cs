using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Guards the shared apparent-members projection for decomposable built-in object types (#512):
/// <see cref="BuiltInTypes.GetInstanceMemberNames"/> (used by keyof) and
/// <see cref="BuiltInTypes.GetInstanceMemberType"/> (used by structural assignability and
/// conditional infer-matching) must agree on one model. The two are authored from separate
/// declarations (a name list and a name-keyed resolver switch), so this test pins them together:
/// every advertised member name must resolve to a non-null type. Without this, a name added to one
/// but not the other would make <c>keyof T</c> surface a key whose <c>T[K]</c> cannot be resolved.
/// </summary>
public class BuiltInApparentMembersTests
{
    private static readonly TypeInfo Any = TypeInfo.Any.Shared;

    /// <summary>Representative instances of every structurally-decomposable built-in type.</summary>
    public static IEnumerable<object[]> DecomposableTypes()
    {
        yield return ["Date", TypeInfo.Date.Shared];
        yield return ["RegExp", TypeInfo.RegExp.Shared];
        yield return ["Map", new TypeInfo.Map(Any, Any)];
        yield return ["Set", new TypeInfo.Set(Any)];
        yield return ["WeakMap", new TypeInfo.WeakMap(Any, Any)];
        yield return ["WeakSet", new TypeInfo.WeakSet(Any)];
        yield return ["WeakRef", new TypeInfo.WeakRef(Any)];
        yield return ["FinalizationRegistry", new TypeInfo.FinalizationRegistry(Any)];
        yield return ["Promise", new TypeInfo.Promise(Any)];
        yield return ["Iterator", new TypeInfo.Iterator(Any)];
        yield return ["Generator", new TypeInfo.Generator(Any)];
        yield return ["AsyncGenerator", new TypeInfo.AsyncGenerator(Any)];
        // #530 follow-up: the remaining GetXxxMemberType-backed built-ins.
        yield return ["Error", new TypeInfo.Error()];
        yield return ["AggregateError", new TypeInfo.Error("AggregateError")];
        yield return ["Timeout", TypeInfo.Timeout.Shared];
        yield return ["Buffer", TypeInfo.Buffer.Shared];
        yield return ["EventEmitter", TypeInfo.EventEmitter.Shared];
        yield return ["AbortController", TypeInfo.AbortController.Shared];
        yield return ["AbortSignal", TypeInfo.AbortSignal.Shared];
    }

    [Theory]
    [MemberData(nameof(DecomposableTypes))]
    public void EveryAdvertisedMemberNameResolves(string label, TypeInfo type)
    {
        var names = BuiltInTypes.GetInstanceMemberNames(type);
        Assert.NotNull(names);
        Assert.NotEmpty(names);

        foreach (var name in names)
        {
            Assert.True(
                BuiltInTypes.GetInstanceMemberType(type, name) is not null,
                $"keyof {label} advertises member '{name}' but GetInstanceMemberType could not resolve it. " +
                $"The {label} member-name list and its GetXxxMemberType resolver have drifted apart.");
        }
    }

    [Theory]
    [MemberData(nameof(DecomposableTypes))]
    public void AbsentMemberDoesNotResolve(string label, TypeInfo type)
    {
        // The resolver must not over-match: a name that is not a real member resolves to null, so an
        // infer-match / structural check against it correctly fails.
        Assert.True(
            BuiltInTypes.GetInstanceMemberType(type, "definitelyNotARealMember") is null,
            $"{label} resolved a member type for the absent name 'definitelyNotARealMember' — the resolver over-matches.");
        Assert.DoesNotContain("definitelyNotARealMember", BuiltInTypes.GetInstanceMemberNames(type)!);
    }

    [Fact]
    public void IteratorVariantsShareOneMemberList()
    {
        // Iterator/Generator/AsyncGenerator all project through GetIteratorMemberType, so they expose
        // the identical apparent-member set.
        var iterator = BuiltInTypes.GetInstanceMemberNames(new TypeInfo.Iterator(Any));
        var generator = BuiltInTypes.GetInstanceMemberNames(new TypeInfo.Generator(Any));
        var asyncGenerator = BuiltInTypes.GetInstanceMemberNames(new TypeInfo.AsyncGenerator(Any));

        Assert.Equal(iterator, generator);
        Assert.Equal(iterator, asyncGenerator);
    }

    [Fact]
    public void AggregateErrorSurfacesErrorsMemberButPlainErrorDoesNot()
    {
        // GetErrorMemberType exposes `errors` only for AggregateError, so the apparent-member name
        // list must be name-aware: AggregateError advertises it, a plain Error does not (#530).
        Assert.Contains("errors", BuiltInTypes.GetInstanceMemberNames(new TypeInfo.Error("AggregateError"))!);
        Assert.DoesNotContain("errors", BuiltInTypes.GetInstanceMemberNames(new TypeInfo.Error())!);
        Assert.NotNull(BuiltInTypes.GetInstanceMemberType(new TypeInfo.Error("AggregateError"), "errors"));
        Assert.Null(BuiltInTypes.GetInstanceMemberType(new TypeInfo.Error(), "errors"));
    }

    [Fact]
    public void NonDecomposableTypesReturnNull()
    {
        // string/Array are NOT part of the dedicated apparent-members projection (it models the
        // index-signature-free records); keyof handles them through their own ExtractKeys cases, which
        // read the StringApparentMemberNames / ArrayApparentMemberNames lists guarded below (#527).
        Assert.Null(BuiltInTypes.GetInstanceMemberNames(TypeInfo.String.Shared));
        Assert.Null(BuiltInTypes.GetInstanceMemberNames(new TypeInfo.Array(Any)));
        Assert.Null(BuiltInTypes.GetInstanceMemberNames(Any));
        Assert.Null(BuiltInTypes.GetInstanceMemberType(TypeInfo.String.Shared, "length"));
    }

    /// <summary>
    /// The keyof member-name lists for the index-signature built-ins (#527) must stay in sync with their
    /// GetXxxMemberType resolvers: every advertised name must resolve, or <c>keyof string</c> /
    /// <c>keyof T[]</c> would surface a key whose <c>T[K]</c> cannot be resolved.
    /// </summary>
    [Fact]
    public void StringApparentMemberNames_AllResolve()
    {
        Assert.NotEmpty(BuiltInTypes.StringApparentMemberNames);
        foreach (var name in BuiltInTypes.StringApparentMemberNames)
            Assert.True(
                BuiltInTypes.GetStringMemberType(name) is not null,
                $"keyof string advertises member '{name}' but GetStringMemberType could not resolve it.");
    }

    [Fact]
    public void ArrayApparentMemberNames_AllResolve()
    {
        Assert.NotEmpty(BuiltInTypes.ArrayApparentMemberNames);
        foreach (var name in BuiltInTypes.ArrayApparentMemberNames)
            Assert.True(
                BuiltInTypes.GetArrayMemberType(name, Any) is not null,
                $"keyof T[] advertises member '{name}' but GetArrayMemberType could not resolve it.");
    }
}
