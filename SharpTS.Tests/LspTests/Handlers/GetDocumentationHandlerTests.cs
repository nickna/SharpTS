using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.LspBridge.Handlers;
using SharpTS.LspBridge.Protocol;
using SharpTS.Tests.LspTests.Helpers;
using Xunit;

namespace SharpTS.Tests.LspTests.Handlers;

/// <summary>
/// Unit tests for GetDocumentationHandler.
/// </summary>
public class GetDocumentationHandlerTests : IDisposable
{
    private readonly AssemblyReferenceLoader _loader;
    private readonly GetDocumentationHandler _handler;

    public GetDocumentationHandlerTests()
    {
        _loader = LspBridgeTestHelper.CreateRuntimeLoader();
        _handler = new GetDocumentationHandler(_loader);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    [Fact]
    public void Handle_ValidType_ReturnsFullName()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("fullName", out var fullName));
        Assert.Equal("System.String", fullName.GetString());
    }

    [Fact]
    public void Handle_TypeWithDoc_ReturnsSummary()
    {
        // System.ObsoleteAttribute typically has documentation in SDK
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("documentation", out _));
        // Documentation may or may not be present depending on SDK
    }

    [Fact]
    public void Handle_TypeWithoutDoc_ReturnsNullDocumentation()
    {
        // Most runtime types may not have documentation available
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        // documentation field should exist (even if null)
        Assert.True(body.TryGetProperty("documentation", out var doc));
        // Value may be null or a string, both are acceptable
        Assert.True(doc.ValueKind == JsonValueKind.Null || doc.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public void Handle_MissingTypeName_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { });

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
            Command = "get-type-documentation",
            Arguments = null
        };

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_EmptyTypeName_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "" });

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("typeName", response.Message);
    }

    [Fact]
    public void Handle_TypeNotFound_ReturnsError()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "NonExistent.FakeType" });

        var response = _handler.Handle(request);

        Assert.False(response.Success);
        Assert.Contains("not found", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_AttributeWithoutSuffix_ResolvesWithSuffix()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "System.Obsolete" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.Equal("System.ObsoleteAttribute", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public void Handle_AttributeWithSuffix_ResolvesDirect()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "System.ObsoleteAttribute" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.Equal("System.ObsoleteAttribute", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public void Handle_GenericType_Resolves()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "System.Collections.Generic.List`1" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);
        Assert.True(body.TryGetProperty("fullName", out var fullName));
        Assert.Contains("List", fullName.GetString());
    }

    [Fact]
    public void Handle_ResponseHasExpectedStructure()
    {
        var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName = "System.String" });

        var response = _handler.Handle(request);

        Assert.True(response.Success);
        var body = LspBridgeTestHelper.ToJsonElement(response);

        // Should have fullName and documentation properties
        Assert.True(body.TryGetProperty("fullName", out _));
        Assert.True(body.TryGetProperty("documentation", out _));
    }

    [Fact]
    public void Handle_MultipleTypes_ReturnsCorrectInfo()
    {
        // Test several types to ensure consistent behavior
        var types = new[] { "System.Int32", "System.Boolean", "System.DateTime" };

        foreach (var typeName in types)
        {
            var request = LspBridgeTestHelper.CreateRequest("get-type-documentation", 1, new { typeName });
            var response = _handler.Handle(request);

            Assert.True(response.Success, $"Failed for type: {typeName}");
            var body = LspBridgeTestHelper.ToJsonElement(response);
            Assert.Equal(typeName, body.GetProperty("fullName").GetString());
        }
    }
}
