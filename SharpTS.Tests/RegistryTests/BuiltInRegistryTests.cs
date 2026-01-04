using Xunit;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Tests.RegistryTests;

public class BuiltInRegistryTests
{
    #region Namespace Lookup Tests

    [Theory]
    [InlineData("Math")]
    [InlineData("Object")]
    [InlineData("Array")]
    [InlineData("JSON")]
    [InlineData("console")]
    public void TryGetNamespace_BuiltIns_ReturnsTrue(string name)
    {
        var result = BuiltInRegistry.Instance.TryGetNamespace(name, out var ns);

        Assert.True(result);
        Assert.NotNull(ns);
        Assert.Equal(name, ns!.Name);
    }

    [Fact]
    public void TryGetNamespace_UnknownNamespace_ReturnsFalse()
    {
        var result = BuiltInRegistry.Instance.TryGetNamespace("Unknown", out var ns);

        Assert.False(result);
        Assert.Null(ns);
    }

    [Fact]
    public void TryGetNamespace_Math_IsSingleton()
    {
        BuiltInRegistry.Instance.TryGetNamespace("Math", out var ns);

        Assert.True(ns!.IsSingleton);
        Assert.NotNull(ns.SingletonFactory);
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Array")]
    [InlineData("JSON")]
    public void TryGetNamespace_StaticNamespaces_AreNotSingletons(string name)
    {
        BuiltInRegistry.Instance.TryGetNamespace(name, out var ns);

        Assert.False(ns!.IsSingleton);
    }

    #endregion

    #region Singleton Tests

    [Fact]
    public void GetSingleton_Math_ReturnsSharpTSMathInstance()
    {
        var singleton = BuiltInRegistry.Instance.GetSingleton("Math");

        Assert.NotNull(singleton);
        Assert.IsType<SharpTSMath>(singleton);
        Assert.Same(SharpTSMath.Instance, singleton);
    }

    [Fact]
    public void GetSingleton_Object_ReturnsNull()
    {
        var singleton = BuiltInRegistry.Instance.GetSingleton("Object");

        Assert.Null(singleton);
    }

    [Fact]
    public void GetSingleton_UnknownNamespace_ReturnsNull()
    {
        var singleton = BuiltInRegistry.Instance.GetSingleton("Unknown");

        Assert.Null(singleton);
    }

    #endregion

    #region Static Method Tests

    [Fact]
    public void GetStaticMethod_ObjectKeys_ReturnsMethod()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("Object", "keys");

        Assert.NotNull(method);
        Assert.Equal(1, method.Arity());
    }

    [Fact]
    public void GetStaticMethod_ObjectValues_ReturnsMethod()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("Object", "values");

        Assert.NotNull(method);
        Assert.Equal(1, method.Arity());
    }

    [Fact]
    public void GetStaticMethod_ObjectEntries_ReturnsMethod()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("Object", "entries");

        Assert.NotNull(method);
        Assert.Equal(1, method.Arity());
    }

    [Fact]
    public void GetStaticMethod_ArrayIsArray_ReturnsMethod()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("Array", "isArray");

        Assert.NotNull(method);
        Assert.Equal(1, method.Arity());
    }

    [Fact]
    public void GetStaticMethod_JSONParse_ReturnsMethod()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("JSON", "parse");

        Assert.NotNull(method);
        Assert.Equal(1, method.Arity());
    }

    [Fact]
    public void GetStaticMethod_JSONStringify_ReturnsMethod()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("JSON", "stringify");

        Assert.NotNull(method);
        Assert.Equal(1, method.Arity());
    }

    [Fact]
    public void GetStaticMethod_ConsoleLog_ReturnsMethod()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("console", "log");

        Assert.NotNull(method);
        Assert.Equal(0, method.Arity()); // Variadic, min arity is 0
    }

    [Fact]
    public void GetStaticMethod_UnknownMethod_ReturnsNull()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("Object", "unknown");

        Assert.Null(method);
    }

    [Fact]
    public void GetStaticMethod_UnknownNamespace_ReturnsNull()
    {
        var method = BuiltInRegistry.Instance.GetStaticMethod("Unknown", "method");

        Assert.Null(method);
    }

    #endregion

    #region Instance Member Tests - String

    [Fact]
    public void GetInstanceMember_StringLength_ReturnsValue()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember("hello", "length");

        Assert.Equal(5.0, member);
    }

    [Fact]
    public void GetInstanceMember_StringCharAt_ReturnsMethod()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember("hello", "charAt");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_StringToUpperCase_ReturnsMethod()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember("hello", "toUpperCase");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_StringUnknown_ReturnsNull()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember("hello", "unknown");

        Assert.Null(member);
    }

    #endregion

    #region Instance Member Tests - Array

    [Fact]
    public void GetInstanceMember_ArrayLength_ReturnsValue()
    {
        var arr = new SharpTSArray([1.0, 2.0, 3.0]);

        var member = BuiltInRegistry.Instance.GetInstanceMember(arr, "length");

        Assert.Equal(3.0, member);
    }

    [Fact]
    public void GetInstanceMember_ArrayPush_ReturnsMethod()
    {
        var arr = new SharpTSArray([1.0, 2.0]);

        var member = BuiltInRegistry.Instance.GetInstanceMember(arr, "push");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_ArrayMap_ReturnsMethod()
    {
        var arr = new SharpTSArray([1.0, 2.0]);

        var member = BuiltInRegistry.Instance.GetInstanceMember(arr, "map");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_ArrayFilter_ReturnsMethod()
    {
        var arr = new SharpTSArray([1.0, 2.0]);

        var member = BuiltInRegistry.Instance.GetInstanceMember(arr, "filter");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_ArrayUnknown_ReturnsNull()
    {
        var arr = new SharpTSArray([1.0, 2.0]);

        var member = BuiltInRegistry.Instance.GetInstanceMember(arr, "unknown");

        Assert.Null(member);
    }

    #endregion

    #region Instance Member Tests - Math

    [Fact]
    public void GetInstanceMember_MathPI_ReturnsValue()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember(SharpTSMath.Instance, "PI");

        Assert.Equal(Math.PI, member);
    }

    [Fact]
    public void GetInstanceMember_MathE_ReturnsValue()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember(SharpTSMath.Instance, "E");

        Assert.Equal(Math.E, member);
    }

    [Fact]
    public void GetInstanceMember_MathAbs_ReturnsMethod()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember(SharpTSMath.Instance, "abs");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_MathFloor_ReturnsMethod()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember(SharpTSMath.Instance, "floor");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_MathRandom_ReturnsMethod()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember(SharpTSMath.Instance, "random");

        Assert.NotNull(member);
        Assert.IsType<BuiltInMethod>(member);
    }

    [Fact]
    public void GetInstanceMember_MathUnknown_ReturnsNull()
    {
        var member = BuiltInRegistry.Instance.GetInstanceMember(SharpTSMath.Instance, "unknown");

        Assert.Null(member);
    }

    #endregion

    #region HasInstanceMembers Tests

    [Fact]
    public void HasInstanceMembers_String_ReturnsTrue()
    {
        Assert.True(BuiltInRegistry.Instance.HasInstanceMembers("hello"));
    }

    [Fact]
    public void HasInstanceMembers_Array_ReturnsTrue()
    {
        Assert.True(BuiltInRegistry.Instance.HasInstanceMembers(new SharpTSArray([])));
    }

    [Fact]
    public void HasInstanceMembers_Math_ReturnsTrue()
    {
        Assert.True(BuiltInRegistry.Instance.HasInstanceMembers(SharpTSMath.Instance));
    }

    [Fact]
    public void HasInstanceMembers_Integer_ReturnsFalse()
    {
        Assert.False(BuiltInRegistry.Instance.HasInstanceMembers(42));
    }

    [Fact]
    public void HasInstanceMembers_Object_ReturnsFalse()
    {
        // SharpTSObject is handled differently (not via registry)
        Assert.False(BuiltInRegistry.Instance.HasInstanceMembers(new SharpTSObject(new Dictionary<string, object?>())));
    }

    #endregion
}
