using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.LspBridge.Handlers;
using SharpTS.LspBridge.Protocol;
using SharpTS.Tests.LspTests.Helpers;
using Xunit;

namespace SharpTS.Tests.LspTests.Handlers;

/// <summary>
/// Unit tests for ResolveTypeHandler.
/// </summary>
public class ResolveTypeHandlerTests : IDisposable
{
    private readonly AssemblyReferenceLoader _loader;
    private readonly ResolveTypeHandler _handler;

    public ResolveTypeHandlerTests()
    {
        _loader = LspBridgeTestHelper.CreateRuntimeLoader();
        _handler = new ResolveTypeHandler(_loader);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    [Fact]
    public void Handle_ValidTypeName_ReturnsTypeInfo()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);

        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("exists").GetBoolean());
        Assert.Equal("System.String", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public void Handle_AttributeWithoutSuffix_ResolvesWithSuffix()
    {
        // "Obsolete" should resolve to "System.ObsoleteAttribute"
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.Obsolete" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);

        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("exists").GetBoolean());
        Assert.Equal("System.ObsoleteAttribute", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public void Handle_AttributeWithSuffix_ResolvesDirect()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);

        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("exists").GetBoolean());
        Assert.Equal("System.ObsoleteAttribute", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public void Handle_NonexistentType_ReturnsExistsFalse()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "NonExistent.FakeType" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Body);

        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.False(body.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public void Handle_MissingTypeName_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { });

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_NullArguments_ReturnsError()
    {
        var request = new BridgeRequest
        {
            Seq = 1,
            Command = "resolve-type",
            Arguments = null
        };

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_EmptyTypeName_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "" });

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_AttributeType_SetsIsAttributeTrue()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("isAttribute").GetBoolean());
    }

    [Fact]
    public void Handle_NonAttributeType_SetsIsAttributeFalse()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.False(body.GetProperty("isAttribute").GetBoolean());
    }

    [Fact]
    public void Handle_AbstractType_SetsIsAbstractTrue()
    {
        // System.Array is abstract
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.Array" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("isAbstract").GetBoolean());
    }

    [Fact]
    public void Handle_SealedType_SetsIsSealedTrue()
    {
        // System.String is sealed
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("isSealed").GetBoolean());
    }

    [Fact]
    public void Handle_ValidType_ReturnsAssemblyName()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("assembly", out var assembly));
        Assert.NotNull(assembly.GetString());
        Assert.NotEmpty(assembly.GetString()!);
    }

    [Fact]
    public void Handle_GenericType_Resolves()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.Collections.Generic.List`1" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public void Handle_IntType_Resolves()
    {
        var request = LspBridgeTestHelper.CreateRequest("resolve-type", 1, new { typeName = "System.Int32" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.GetProperty("exists").GetBoolean());
        Assert.Equal("System.Int32", body.GetProperty("fullName").GetString());
    }
}
